#nullable enable
using System;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.History;
using AkmlSql.Shell.Shared.Ipc;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Captures SQL execution events from SSMS/VS and sends history recording requests
    /// to the out-of-process engine via fire-and-forget IPC notifications.
    /// <para>
    /// Uses <see cref="DTE.Events.CommandEvents"/> to intercept "Query.Execute" (F5)
    /// completion, the same portable pattern as
    /// <see cref="Safety.ExecutionCommandFilter"/>. This works across all SSMS 20/21/22
    /// and VS 2019/2022/2026 without version-specific COM interop.
    /// </para>
    /// </summary>
    internal static class ExecutionCapture
    {
        private static bool _initialized;
        private static bool _enabled;

        // DTE references — must keep strong references to prevent GC collection
        private static DTE? _dte;
        private static CommandEvents? _commandEvents;
        private static DocumentEvents? _documentEvents;
        private static WindowEvents? _windowEvents;
        private static DTEEvents? _dteEvents;

        // Tracks execution start time between BeforeExecute and AfterExecute
        private static DateTime? _executeStartTimeUtc;

        // Tracks the last active SQL document content hash for dedup on tab switch
        private static string? _lastActiveDocumentPath;
        private static string? _lastRecordedContentHash;

        private const string QueryExecuteCommandName = "Query.Execute";

        // Cached Query.Execute command GUID and ID — avoids resolving every DTE command
        private static string? _queryExecuteGuid;
        private static int _queryExecuteId;

        /// <summary>
        /// Spec 040 (HIS-02): set by the package when SSMS starts shutting down, before its
        /// documents close. Tabs closed by shutdown are NOT marked closed in History, so the
        /// queries open at exit can be offered for restore on the next start.
        /// </summary>
        public static volatile bool ShuttingDown;

        /// <summary>
        /// Spec 040 (T133, HIS-09) — raised after a run or draft reaches History (with its entry id,
        /// or null when the engine did not say), off the UI thread. SQL History refreshes on it.
        /// </summary>
        public static event Action<long?>? HistoryRecorded;

        // Spec 040 (HIS-13): autosave of unsaved query tabs every Tabs.AutoSaveInterval seconds.
        private static System.Windows.Threading.DispatcherTimer? _autosaveTimer;

        private static AppSettings? _settingsCache;
        private static DateTime _settingsReadUtc;

        /// <summary>Settings for capture decisions, read at most every 5 s.</summary>
        private static AppSettings CaptureSettings()
        {
            var cached = _settingsCache;
            if (cached != null && (DateTime.UtcNow - _settingsReadUtc).TotalSeconds < 5) return cached;
            try { cached = ConfigManager.Load(); }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionCapture: settings read failed; using defaults");
                cached = new AppSettings();
            }
            _settingsCache = cached;
            _settingsReadUtc = DateTime.UtcNow;
            return cached;
        }

        private static readonly int CurrentPid = System.Diagnostics.Process.GetCurrentProcess().Id;

        /// <summary>
        /// Initializes execution capture. Reads configuration to determine if history
        /// recording is enabled, and hooks into SSMS query execution completion events.
        /// </summary>
        /// <param name="package">The VS/SSMS package for service resolution.</param>
        public static void Initialize(Package package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;
            _initialized = true;

            try
            {
                var settings = ConfigManager.Load();
                _enabled = settings.History.Enabled;

                if (!_enabled)
                {
                    Log.Information("ExecutionCapture: history recording is disabled by configuration");
                    return;
                }

                _dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (_dte == null)
                {
                    Log.Warning("ExecutionCapture: DTE service not available; history capture disabled");
                    return;
                }

                // Cache the Query.Execute command GUID to avoid resolving every command
                try
                {
                    var cmd = _dte.Commands.Item(QueryExecuteCommandName);
                    if (cmd != null)
                    {
                        _queryExecuteId = cmd.ID;
                        _queryExecuteGuid = cmd.Guid;
                    }
                }
                catch
                {
                    Log.Debug("ExecutionCapture: could not cache Query.Execute GUID; will resolve per-command");
                }

                // Hook CommandEvents for Query.Execute — strong reference prevents GC
                _commandEvents = _dte.Events.CommandEvents;
                _commandEvents.BeforeExecute += OnBeforeCommandExecute;
                _commandEvents.AfterExecute += OnAfterCommandExecute;

                // Hook DocumentClosing to capture a final SQL snapshot when a tab closes
                _documentEvents = _dte.Events.DocumentEvents;
                _documentEvents.DocumentClosing += OnDocumentClosing;

                // Hook DocumentSaved so a Save/Save-As rename doesn't split one tab's session
                // (see OnDocumentSaved for how the old→new migration is correlated).
                _documentEvents.DocumentSaved += OnDocumentSaved;

                // Hook WindowActivated to record the previous tab's SQL on focus change
                _windowEvents = _dte.Events.WindowEvents;
                _windowEvents.WindowActivated += OnWindowActivated;

                // Spec 040 (HIS-02): shutdown starts before its documents close — mark it, so those
                // closes leave the queries open in History (offered for restore on the next start).
                _dteEvents = _dte.Events.DTEEvents;
                _dteEvents.OnBeginShutdown += () =>
                {
                    ShuttingDown = true;
                    _autosaveTimer?.Stop();
                };

                StartAutosave(settings.Tabs);

                // Initialize the last active document path
                try
                {
                    if (_dte.ActiveDocument != null)
                    {
                        _lastActiveDocumentPath = _dte.ActiveDocument.FullName;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionCapture: failed to read initial active document");
                }

                Log.Information("ExecutionCapture: initialized with DTE command, document, and window hooks");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecutionCapture: failed to initialize");
            }
        }

        /// <summary>
        /// Unsubscribes from all DTE events. Called during package disposal.
        /// </summary>
        public static void Shutdown()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (_commandEvents != null)
                {
                    _commandEvents.BeforeExecute -= OnBeforeCommandExecute;
                    _commandEvents.AfterExecute -= OnAfterCommandExecute;
                    _commandEvents = null;
                }

                if (_documentEvents != null)
                {
                    _documentEvents.DocumentClosing -= OnDocumentClosing;
                    _documentEvents.DocumentSaved -= OnDocumentSaved;
                    _documentEvents = null;
                }

                if (_windowEvents != null)
                {
                    _windowEvents.WindowActivated -= OnWindowActivated;
                    _windowEvents = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ExecutionCapture: error detaching event handlers during shutdown");
            }

            _dte = null;
            _executeStartTimeUtc = null;
            _lastActiveDocumentPath = null;
            _initialized = false;
        }

        // ----- DTE Command Event Handlers -----

        /// <summary>
        /// Fires before a DTE command runs. Records the start time for Query.Execute
        /// so we can compute duration in <see cref="OnAfterCommandExecute"/>.
        /// </summary>
        private static void OnBeforeCommandExecute(
            string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            try
            {
                if (!IsQueryExecuteCommand(guid, id)) return;

                _executeStartTimeUtc = DateTime.UtcNow;
                Log.Debug("ExecutionCapture: Query.Execute started at {Time}", _executeStartTimeUtc);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionCapture: error in BeforeExecute hook");
            }
        }

        /// <summary>
        /// Fires after a DTE command completes. For Query.Execute, captures the SQL text,
        /// connection info, and duration, then sends a history record via
        /// <see cref="OnExecutionCompleted"/>.
        /// </summary>
        private static void OnAfterCommandExecute(
            string guid, int id, object customIn, object customOut)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_dte == null) return;

                if (!IsQueryExecuteCommand(guid, id))
                    return;

                // Compute duration from the BeforeExecute timestamp
                long durationMs = 0;
                if (_executeStartTimeUtc.HasValue)
                {
                    durationMs = (long)(DateTime.UtcNow - _executeStartTimeUtc.Value).TotalMilliseconds;
                    _executeStartTimeUtc = null;
                }

                // Extract SQL text from the active editor
                var sqlText = GetActiveSqlText();
                if (string.IsNullOrWhiteSpace(sqlText))
                {
                    Log.Debug("ExecutionCapture: empty SQL text after Query.Execute; skipping");
                    return;
                }

                // Extract connection info
                string? server = null;
                string? database = null;
                try
                {
                    var sp = ServiceProvider.GlobalProvider as IServiceProvider;
                    if (sp != null)
                    {
                        var connectionResult = Editor.SsmsConnectionDetector.TryDetectConnection(sp);
                        if (connectionResult != null)
                        {
                            // The server connected to, not a custom connection name: History reconnects
                            // to it on Open, Re-execute and restore.
                            server = connectionResult.DataSource;
                            database = connectionResult.Database;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionCapture: failed to detect connection info");
                }

                // Source file, tab title, and session key.
                // TabTitle is sent ONLY for a document that is actually saved to disk: an unsaved
                // SSMS scratch document has a machine-generated name ("dwnhdxfq.sql") that carries
                // no user intent, and sending it would suppress the query-NN auto name.
                string? source = null;
                string? tabTitle = null;
                string? sessionKey = null;
                try
                {
                    var activeDoc = _dte.ActiveDocument;
                    if (activeDoc != null)
                    {
                        source = activeDoc.FullName;
                        sessionKey = DocumentSessionKeys.ForDocument(source);

                        // Keep the "last known active path" fresh at every execution, not just on
                        // window-activation — this is what lets OnDocumentSaved recognize a
                        // same-tab rename (Save/Save As) even when no window switch happened
                        // between the last execute and the save.
                        _lastActiveDocumentPath = source;

                        tabTitle = IsSavedToDisk(activeDoc.Path) ? activeDoc.Name : null;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionCapture: failed to read active document info");
                }

                Log.Debug("ExecutionCapture: Query.Execute completed in {Duration}ms on {Server}.{Database}",
                    durationMs, server, database);

                // AfterExecute does not expose success/failure or row count.
                // Record as Success; the engine can update status if error info becomes available.
                OnExecutionCompleted(
                    sqlText: sqlText!,
                    server: server,
                    database: database,
                    username: null,
                    durationMs: durationMs,
                    rowCount: 0,
                    status: ExecutionStatus.Success,
                    errorMessage: null,
                    source: source,
                    tabTitle: tabTitle,
                    sessionKey: sessionKey);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecutionCapture: error in AfterExecute hook");
            }
        }

        // ----- Document / Window Event Handlers -----

        /// <summary>
        /// Captures the final SQL text when a document tab is closing.
        /// Saves as a version snapshot of the existing history entry (not a new entry),
        /// so auto-saves don't pollute the query list with duplicate rows.
        /// </summary>
        private static void OnDocumentClosing(Document document)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (document == null) return;

                // Spec 040 (HIS-02): read the key BEFORE forgetting it, to mark the query closed
                // (unless SSMS is shutting down — see ShuttingDown).
                DocumentSessionKeys.TryGet(document.FullName, out var closingKey);
                SendOpenState(OpenStateReporter.OnClosing(closingKey, CurrentPid, ShuttingDown));

                // The document is closing regardless of what follows — release its session key now
                // so reopening the same file starts a brand-new session.
                DocumentSessionKeys.Forget(document.FullName);

                var textDoc = document.Object("TextDocument") as TextDocument;
                if (textDoc == null) return;

                var editPoint = textDoc.StartPoint.CreateEditPoint();
                var content = editPoint.GetText(textDoc.EndPoint);

                if (string.IsNullOrWhiteSpace(content)) return;

                // Key on FullName (history.source), not Name/tab_title: TabTitle is sent only for a
                // saved document (see OnAfterCommandExecute), so an unsaved scratch tab's tab_title
                // is NULL and a lookup keyed on it would never find the row to snapshot against.
                var source = document.FullName;
                if (string.IsNullOrEmpty(source)) return;

                // Spec 040 (HIS-14): a query tab that never ran has no History row to add a version
                // to — keep its text as a draft instead ("Not executed"), findable after it closes.
                // While SSMS shuts down the draft stays open, so the next start offers to restore it.
                if (DraftCapturePolicy.ShouldCaptureDraft(source, content, !string.IsNullOrEmpty(closingKey)))
                {
                    Log.Debug("ExecutionCapture: recording a draft on close for '{Source}'", source);
                    SendDraft(source, SafeName(document), content, Guid.NewGuid().ToString("N"), ActiveConnectionIfActive(document),
                        markOpen: ShuttingDown);
                    return;
                }

                Log.Debug("ExecutionCapture: saving version snapshot on close for '{Source}'", source);

                SaveVersionSnapshot(source, content, closingKey);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ExecutionCapture: error capturing document close snapshot");
            }
        }

        /// <summary>
        /// Fires after a document is saved. If the save changed the document's <c>FullName</c> —
        /// a Save As, or (more commonly) the FIRST save of an unsaved scratch document whose
        /// machine-generated temp name gets replaced by the user's chosen path — the session key
        /// tracked under the OLD name is migrated onto the NEW name, so executions before and
        /// after the save land in one history entry instead of splitting into two.
        /// <para>
        /// Correlation deliberately uses only string comparisons on <see cref="_lastActiveDocumentPath"/>
        /// (already kept fresh by <see cref="OnAfterCommandExecute"/> and <see cref="OnWindowActivated"/>)
        /// and <c>_dte.ActiveDocument.FullName</c> — NOT COM/RCW reference identity of the
        /// <see cref="Document"/> object, which is not something this code can verify. The extra
        /// "is this document still the active one" check guards against misattributing a background
        /// Save-All to the wrong tracked session.
        /// </para>
        /// <para>
        /// Finding 7 (PR #249 review): the actual state transition — decide whether to migrate a
        /// session key and what <see cref="_lastActiveDocumentPath"/> should become next — is
        /// delegated to <see cref="ApplyDocumentSaved"/>, a pure function of
        /// (current tracked path, DTE's reported active path, this save's new path). Before this
        /// fix, <c>_lastActiveDocumentPath = newPath</c> ran UNCONDITIONALLY, even for a save of a
        /// document that was NOT the active tab (e.g. Save-All firing DocumentSaved for every open
        /// tab in turn). That let an inactive tab's save silently hijack the tracked path; the
        /// NEXT save of the real active tab would then see a stale "old path" belonging to the
        /// OTHER tab and either wrongly migrate that unrelated tab's session key onto itself, or —
        /// on a name collision — retire it outright via <see cref="DocumentSessionKeys.Rename"/>'s
        /// collision-decline branch, splitting that tab's history even though it was never closed.
        /// </para>
        /// </summary>
        private static void OnDocumentSaved(Document document)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (document == null || _dte == null) return;

                var newPath = document.FullName;
                if (string.IsNullOrEmpty(newPath)) return;

                string? activePath = null;
                try
                {
                    activePath = _dte.ActiveDocument?.FullName;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionCapture: failed to read active document while handling DocumentSaved");
                }

                _lastActiveDocumentPath = ApplyDocumentSaved(_lastActiveDocumentPath, activePath, newPath);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionCapture: error in DocumentSaved hook");
            }
        }

        /// <summary>
        /// Pure decision for <see cref="OnDocumentSaved"/> (Finding 7, PR #249 review). Only a save
        /// of the CURRENTLY ACTIVE document can migrate a session key or update
        /// <paramref name="lastActiveDocumentPath"/> — a save of any other (inactive) tab is a
        /// complete no-op, returning <paramref name="lastActiveDocumentPath"/> unchanged. This is
        /// what keeps a Save-All (which fires DocumentSaved once per open tab, only one of which is
        /// active) from corrupting the tracked path with an unrelated tab's name, which is what
        /// let a later, genuine save of the active tab migrate — or on collision, retire via
        /// <see cref="DocumentSessionKeys.Rename"/>'s decline branch — that OTHER, still-open tab's
        /// session key.
        /// </summary>
        /// <param name="lastActiveDocumentPath">The currently tracked "last active document" path.</param>
        /// <param name="activePath"><c>_dte.ActiveDocument.FullName</c> at the moment of the save
        /// (null/empty if it could not be read).</param>
        /// <param name="newPath">The saved document's (post-save) <c>FullName</c>.</param>
        /// <returns>The new value <c>_lastActiveDocumentPath</c> should take.</returns>
        internal static string? ApplyDocumentSaved(string? lastActiveDocumentPath, string? activePath, string newPath)
        {
            if (string.IsNullOrEmpty(activePath)
                || !string.Equals(activePath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                // The saved document is not the active one -- leave tracking untouched entirely.
                return lastActiveDocumentPath;
            }

            if (!string.IsNullOrEmpty(lastActiveDocumentPath)
                && !string.Equals(lastActiveDocumentPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                DocumentSessionKeys.Rename(lastActiveDocumentPath!, newPath);
                Log.Debug("ExecutionCapture: migrated session key from '{Old}' to '{New}' after save",
                    lastActiveDocumentPath, newPath);
            }

            return newPath;
        }

        /// <summary>
        /// When the user switches to a different window, records the SQL content of the
        /// previously focused SQL document as an auto-save snapshot. This ensures that
        /// unsaved edits are captured even if the user never executes the query.
        /// </summary>
        private static void OnWindowActivated(Window gotFocus, Window lostFocus)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_dte == null) return;

                // Spec 040 (HIS-02): the document that gained focus is open in this SSMS (if it has
                // run before — a document with no session key has no history rows yet).
                try
                {
                    var gained = gotFocus?.Document?.FullName;
                    if (!string.IsNullOrEmpty(gained) && DocumentSessionKeys.TryGet(gained!, out var gainedKey))
                        SendOpenState(OpenStateReporter.OnActivated(gainedKey, CurrentPid));
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionCapture: could not read the activated document");
                }

                if (lostFocus == null) return;

                // Only capture if the lost-focus window had a document
                Document lostDoc = null;
                try
                {
                    lostDoc = lostFocus.Document;
                }
                catch
                {
                    // Some windows don't have documents (tool windows, etc.)
                    return;
                }

                if (lostDoc == null) return;

                // Only capture SQL-like documents (heuristic: .sql extension or untitled SSMS tabs)
                var name = lostDoc.Name ?? "";
                if (!name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("SQLQuery", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Skip if this is the same document we last recorded (avoid duplicate snapshots)
                var docPath = lostDoc.FullName;
                if (string.Equals(docPath, _lastActiveDocumentPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Same document — only record if we haven't already
                    // Update tracking to the new active document
                    try
                    {
                        _lastActiveDocumentPath = gotFocus?.Document?.FullName;
                    }
                    catch
                    {
                        _lastActiveDocumentPath = null;
                    }
                    return;
                }

                // Update tracking
                try
                {
                    _lastActiveDocumentPath = gotFocus?.Document?.FullName;
                }
                catch
                {
                    _lastActiveDocumentPath = null;
                }

                var textDoc = lostDoc.Object("TextDocument") as TextDocument;
                if (textDoc == null) return;

                var editPoint = textDoc.StartPoint.CreateEditPoint();
                var content = editPoint.GetText(textDoc.EndPoint);

                if (string.IsNullOrWhiteSpace(content)) return;

                // Content-hash dedup: skip if the content hasn't changed since last recording
                var contentHash = ComputeSimpleHash(content);
                if (string.Equals(contentHash, _lastRecordedContentHash, StringComparison.Ordinal))
                    return;
                _lastRecordedContentHash = contentHash;

                // Key on docPath (history.source), not name/tab_title — see the matching comment in
                // OnDocumentClosing for why: an unsaved scratch tab's tab_title is NULL.
                if (string.IsNullOrEmpty(docPath)) return;

                Log.Debug("ExecutionCapture: saving version snapshot for '{Source}' on tab switch", docPath);

                DocumentSessionKeys.TryGet(docPath, out var lostKey);
                SaveVersionSnapshot(docPath, content, lostKey);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionCapture: error in WindowActivated handler");
            }
        }

        // ----- Helpers -----

        /// <summary>
        /// Finding 6 (PR #249 review): is this document saved to disk? Answered entirely from DTE
        /// state -- <paramref name="path"/> is <c>EnvDTE.Document.Path</c>, which is empty for a
        /// document that has never been saved. The pre-fix version additionally called
        /// <c>File.Exists(activeDoc.FullName)</c> here, on the SSMS UI thread
        /// (<see cref="OnAfterCommandExecute"/> runs under <c>ThreadHelper.ThrowIfNotOnUIThread</c>).
        /// For a document on a UNC share or a stale mapped drive, that call is a blocking SMB
        /// round-trip that freezes SSMS after every F5. <c>Path</c> alone is already the signal
        /// this needs -- an SSMS scratch document that was never saved reports an empty
        /// <c>Path</c>; anything with a real directory has one, with no filesystem I/O required to
        /// know it. Extracted as a pure static so the saved/unsaved decision stays unit-testable
        /// without a live DTE <c>Document</c>.
        /// </summary>
        internal static bool IsSavedToDisk(string? path) => !string.IsNullOrEmpty(path);

        /// <summary>A DTE command matches only when both its group GUID and its ID do.</summary>
        internal static bool IsSameCommand(string? guid, int id, string expectedGuid, int expectedId) =>
            id == expectedId && string.Equals(guid, expectedGuid, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Fast check: is this command the Query.Execute command?
        /// Uses the cached GUID when available (avoids per-command COM interop).
        /// Falls back to full resolution only when the GUID wasn't cached during init.
        /// </summary>
        private static bool IsQueryExecuteCommand(string guid, int id)
        {
            // Fast path: the cached GUID and ID. Spec 040 (HIS): the GUID alone names the whole SQL
            // editor command group, so every command in it (menu and query-window commands alike)
            // used to be recorded in History as an execution that never happened.
            if (_queryExecuteGuid != null)
            {
                return IsSameCommand(guid, id, _queryExecuteGuid, _queryExecuteId);
            }

            // Slow fallback: resolve command name via COM
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (_dte == null) return false;
                var command = _dte.Commands.Item(guid, id);
                return command?.Name?.Equals(QueryExecuteCommandName, StringComparison.OrdinalIgnoreCase) == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the SQL text from the active document editor. If there is a selection,
        /// returns the selected text; otherwise returns the full document content.
        /// </summary>
        private static string? GetActiveSqlText()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (_dte?.ActiveDocument == null) return null;

                var textDoc = _dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (textDoc == null) return null;

                // If there's a selection, capture the selected text (user executed a fragment)
                var selection = textDoc.Selection;
                if (selection != null && !selection.IsEmpty)
                {
                    return selection.Text;
                }

                // Full document text
                var startPoint = textDoc.StartPoint.CreateEditPoint();
                return startPoint.GetText(textDoc.EndPoint);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionCapture: failed to get SQL text from active document");
                return null;
            }
        }

        /// <summary>
        /// Saves a version snapshot for an existing history entry, found by <paramref name="source"/>
        /// (the document's full path — matches <c>history.source</c> engine-side).
        /// Used by tab-close and tab-focus-change auto-save triggers.
        /// These record as versions of the existing entry, not new rows in the query list.
        /// <para>
        /// Keyed on the document's full path rather than its tab title: <c>TabTitle</c> is sent to
        /// the engine only for a document actually saved to disk (see
        /// <see cref="OnAfterCommandExecute"/>), so an unsaved SSMS scratch tab has no <c>tab_title</c>
        /// to match against. <c>source</c> is always populated (see <see cref="OnAfterCommandExecute"/>'s
        /// <c>source = activeDoc.FullName</c>), so it is the identifier this lookup can rely on for
        /// both saved and unsaved documents.
        /// </para>
        /// </summary>
        private static void SaveVersionSnapshot(string source, string sqlText, string? sessionKey = null)
        {
            Task.Run(async () =>
            {
                try
                {
                    var client = EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) return;

                    var request = new HistoryActionRequest
                    {
                        Action = HistoryActions.SaveVersion,
                        NewName = source, // reuse NewName field to carry the source path
                        SqlText = sqlText,
                        // Spec 040 (T064): lets the engine find this session's own row first.
                        SessionKey = string.IsNullOrEmpty(sessionKey) ? null : sessionKey
                    };

                    await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                        MessageTypes.HistoryAction, request, timeoutMs: 5000);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionCapture: failed to save version snapshot for '{Source}'", source);
                }
            });
        }

        private static void RaiseRecorded(long? entryId)
        {
            try { HistoryRecorded?.Invoke(entryId); }
            catch (Exception ex) { Log.Debug(ex, "ExecutionCapture: HistoryRecorded handler failed"); }
        }

        private static string? SafeName(Document document)
        {
            try { return IsSavedToDisk(document.Path) ? document.Name : null; }
            catch { return null; }
        }

        /// <summary>The server and database of <paramref name="document"/> when it is the active document (the only one SSMS reports).</summary>
        private static (string? Server, string? Database) ActiveConnectionIfActive(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (_dte?.ActiveDocument == null
                    || !string.Equals(_dte.ActiveDocument.FullName, document.FullName, StringComparison.OrdinalIgnoreCase))
                    return (null, null);
                if (ServiceProvider.GlobalProvider is IServiceProvider sp
                    && Editor.SsmsConnectionDetector.TryDetectConnection(sp) is { } c)
                    return (c.DataSource, c.Database);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionCapture: could not read the closing document's connection");
            }
            return (null, null);
        }

        /// <summary>
        /// Spec 040 (HIS-14): records <paramref name="sqlText"/> as a draft (<c>IsDraft</c>, stored as
        /// "not executed") in session <paramref name="sessionKey"/>, off the UI thread.
        /// </summary>
        /// <param name="markOpen">The tab is still open (autosave, shutdown): mark the query open in
        /// this shell, so restore on start can offer it.</param>
        private static void SendDraft(string source, string? tabTitle, string sqlText, string sessionKey,
            (string? Server, string? Database) connection, bool markOpen)
        {
            if (!_enabled) return;
            var limited = DraftCapturePolicy.TruncateToLimit(sqlText, CaptureSettings().History.MaxQuerySizeKb);
            var request = new HistoryRecordRequest
            {
                SqlText = limited,
                Truncated = !ReferenceEquals(limited, sqlText),
                Server = connection.Server,
                Database = connection.Database,
                Status = (int)ExecutionStatus.NotExecuted,
                Source = source,
                TabTitle = tabTitle,
                SessionKey = sessionKey,
                IsDraft = true,
            };
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) return;
                    var response = await client.SendRequestAsync<HistoryRecordResponse, HistoryRecordRequest>(
                        MessageTypes.HistoryRecord, request, timeoutMs: 5000);
                    if (response?.Success == true && markOpen)
                    {
                        await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                            MessageTypes.HistoryAction, OpenStateReporter.OnRecorded(sessionKey, CurrentPid), timeoutMs: 5000);
                    }
                    if (response?.Success == true) RaiseRecorded(response.EntryId);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionCapture: draft record failed");
                }
            });
        }

        /// <summary>
        /// Spec 040 (HIS-13): every <c>Tabs.AutoSaveInterval</c> seconds (while <c>Tabs.SessionRecovery</c>
        /// is on) the unsaved query tabs are snapshotted: a version for a tab with a History session,
        /// a draft (which starts the session) for one without.
        /// </summary>
        private static void StartAutosave(TabSettings tabs)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!tabs.SessionRecovery) return;
            _autosaveTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(15, tabs.AutoSaveInterval)),
            };
            _autosaveTimer.Tick += (_, _) => AutosaveTick();
            _autosaveTimer.Start();
        }

        private static void AutosaveTick()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ShuttingDown || _dte == null || !_enabled) { _autosaveTimer?.Stop(); return; }
            if (!CaptureSettings().Tabs.SessionRecovery) return;

            try
            {
                var open = new System.Collections.Generic.List<(string FullName, bool IsDirty)>();
                var byName = new System.Collections.Generic.Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
                foreach (Document doc in _dte.Documents)
                {
                    try
                    {
                        open.Add((doc.FullName, !doc.Saved));
                        byName[doc.FullName] = doc;
                    }
                    catch { /* a document closing mid-enumeration */ }
                }

                foreach (var name in DraftCapturePolicy.SelectAutosaveTargets(open))
                {
                    if (!byName.TryGetValue(name, out var doc)) continue;
                    if (!(doc.Object("TextDocument") is TextDocument text)) continue;
                    var content = text.StartPoint.CreateEditPoint().GetText(text.EndPoint);
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    if (DocumentSessionKeys.TryGet(name, out var key))
                    {
                        SaveVersionSnapshot(name, content, key);
                    }
                    else
                    {
                        // The draft starts the tab's History session; its later runs join it.
                        var newKey = DocumentSessionKeys.ForDocument(name);
                        SendDraft(name, SafeName(doc), content, newKey, ActiveConnectionIfActive(doc), markOpen: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionCapture: autosave tick failed");
            }
        }

        /// <summary>
        /// Sends an open/closed request from <see cref="OpenStateReporter"/> (null = nothing to send)
        /// on a background task. Failures are logged, never thrown into the UI thread.
        /// </summary>
        private static void SendOpenState(HistoryActionRequest? request)
        {
            if (request == null || !_enabled) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) return;
                    var response = await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                        MessageTypes.HistoryAction, request, timeoutMs: 5000);

                    // Spec 040 (HIS-02/HIS-13): a closed query loses its open bar in an open History
                    // window at once. (Not for "open": that fires on every tab switch.)
                    if (response?.Success == true && request.IsOpen == false) RaiseRecorded(null);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionCapture: open-state update failed");
                }
            });
        }

        /// <summary>
        /// Fast content hash for deduplication of auto-save snapshots.
        /// Uses string hash code — not cryptographic, just for detecting identical content.
        /// </summary>
        private static string ComputeSimpleHash(string content)
        {
            return content.GetHashCode().ToString("X8");
        }

        /// <summary>
        /// Called when a SQL execution completes. Sends a fire-and-forget HistoryRecord
        /// notification to the engine process. This method is safe to call from any thread
        /// and will not block the query execution flow.
        /// </summary>
        /// <param name="sqlText">The SQL text that was executed.</param>
        /// <param name="server">Server name.</param>
        /// <param name="database">Database name.</param>
        /// <param name="username">Login/username.</param>
        /// <param name="durationMs">Execution duration in milliseconds.</param>
        /// <param name="rowCount">Number of rows affected/returned.</param>
        /// <param name="status">Outcome of the execution.</param>
        /// <param name="errorMessage">Error message if execution failed.</param>
        /// <param name="source">Source file path or identifier.</param>
        /// <param name="tabTitle">Title of the editor tab/window.</param>
        /// <param name="sessionKey">Per-document session key so the engine groups this tab's executions into one history entry.</param>
        public static void OnExecutionCompleted(
            string sqlText,
            string? server,
            string? database,
            string? username,
            long durationMs,
            long rowCount,
            ExecutionStatus status,
            string? errorMessage,
            string? source,
            string? tabTitle,
            string? sessionKey = null)
        {
            if (!_enabled) return;

            // Skip recording failed executions if configured
            if (status != ExecutionStatus.Success)
            {
                try
                {
                    var settings = ConfigManager.Load();
                    if (!settings.History.RecordFailures)
                    {
                        Log.Debug("ExecutionCapture: skipping failed execution (recordFailures=false)");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ExecutionCapture: failed to check recordFailures setting");
                }
            }

            // Skip empty queries
            if (string.IsNullOrWhiteSpace(sqlText)) return;

            // Fire-and-forget: send to engine via IPC notification (RequestId=0)
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected)
                    {
                        Log.Debug("ExecutionCapture: engine not connected, skipping history record");
                        return;
                    }

                    // Spec 040 (HIS-13): cut to History.MaxQuerySizeKb (at most 1 MB, today's hard cap),
                    // with a note, so the stored text says it was cut.
                    var limited = DraftCapturePolicy.TruncateToLimit(sqlText, CaptureSettings().History.MaxQuerySizeKb);
                    var truncated = !ReferenceEquals(limited, sqlText);
                    sqlText = limited;

                    var request = new HistoryRecordRequest
                    {
                        SqlText = sqlText,
                        Truncated = truncated,
                        Server = server,
                        Database = database,
                        Username = username,
                        DurationMs = durationMs,
                        RowCount = rowCount,
                        Status = (int)status,
                        ErrorMessage = errorMessage,
                        Source = source,
                        TabTitle = tabTitle,
                        SessionKey = sessionKey
                    };

                    // Spec 040 (HIS-02): a request, not a notification, so the row exists before the
                    // query is marked open. Still off the UI thread, so execution never waits on it.
                    var response = await client.SendRequestAsync<HistoryRecordResponse, HistoryRecordRequest>(
                        MessageTypes.HistoryRecord, request, timeoutMs: 5000);

                    Log.Debug("ExecutionCapture: history record sent to engine (server={Server}, db={Database}, ok={Ok})",
                        server, database, response?.Success);

                    if (response?.Success == true && !string.IsNullOrEmpty(sessionKey))
                    {
                        await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                            MessageTypes.HistoryAction, OpenStateReporter.OnRecorded(sessionKey!, CurrentPid), timeoutMs: 5000);
                    }

                    if (response?.Success == true) RaiseRecorded(response.EntryId);
                }
                catch (Exception ex)
                {
                    // Never let history recording failures bubble up to the user
                    Log.Warning(ex, "ExecutionCapture: failed to send history record to engine");
                }
            });
        }
    }
}
