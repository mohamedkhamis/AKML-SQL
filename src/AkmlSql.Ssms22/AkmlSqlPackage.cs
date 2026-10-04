using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading; // awaiting TaskScheduler.Default (history reconcile)
using Constants = AkmlSql.Core.Constants;
using AkmlSql.Core.Logging;
using AkmlSql.Shell.Shared;
using AkmlSql.Shell.Shared.Commands;
using AkmlSql.Shell.Shared.StatusBar;
using AkmlSql.Shell.Shared.History;
using AkmlSql.Shell.Shared.Productivity.DocumentOutline;
using AkmlSql.Shell.Shared.Productivity.Grid;
using AkmlSql.Shell.Shared.Productivity.Navigation;
using AkmlSql.Shell.Shared.Safety;
using AkmlSql.Shell.Shared.Tabs;
using AkmlSql.Shell.Shared.Update;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Formatting;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Validation;
using AkmlSql.Shell.Shared.Snippets;
using AkmlSql.Shell.Shared.Refactoring;
using Serilog;

namespace AkmlSql.Ssms22
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Constants.ProductName, "AI-powered SQL development assistance", Constants.Version)]
    // Spec 040 (T181): version 2 — the command table's groups and submenus changed, so SSMS re-merges it.
    [ProvideMenuResource("Menus.ctmenu", 2)]
    [ProvideAutoLoad("B7B07F42-6013-4C67-A504-C771CBC7625A", PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(HistoryToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(DocumentOutlineToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(ReferencesToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(AiChatToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [Guid(PackageGuids.AkmlSqlPackageString)]
    public sealed class AkmlSqlPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            // Register assembly resolver BEFORE anything that loads our dependencies
            ExtensionAssemblyResolver.Register();

            await base.InitializeAsync(cancellationToken, progress);

            // Switch to UI thread for menu registration — do this FIRST
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Register menu commands BEFORE anything else (critical path)
            var commandService = await GetServiceAsync(typeof(IMenuCommandService))
                as OleMenuCommandService;

            if (commandService != null)
            {
                TryInitCommand("AboutCommand", () => AboutCommand.Initialize(this, commandService));
                TryInitCommand("CheckUpdateCommand", () => CheckUpdateCommand.Initialize(this, commandService));
                TryInitCommand("OptionsCommand", () => OptionsCommand.Initialize(this, commandService));
                TryInitCommand("FormatStylesCommand", () => FormatStylesCommand.Initialize(this, commandService));
                // Spec 030 T053 — Manage Code Analysis Rules dialog
                TryInitCommand("ManageRulesCommand", () => AkmlSql.Shell.Shared.Analysis.ManageRulesCommand.Initialize(this, commandService));
                // Spec 030 T067 — editor-context refactor commands
                TryInitCommand("InlineExecCommand", () => InlineExecCommand.Initialize(this, commandService));
                TryInitCommand("InsertToUpdateCommand", () => InsertToUpdateCommand.Initialize(this, commandService));
                TryInitCommand("InlineStoredProcedureCommand", () => InlineStoredProcedureCommand.Initialize(this, commandService));
                TryInitCommand("ScriptAsAlterCommand", () => ScriptAsAlterCommand.Initialize(this, commandService));
                TryInitCommand("FindInvalidObjectsCommand", () => FindInvalidObjectsCommand.Initialize(this, commandService));
                // Spec 030 T062 — database-wide Smart Rename (FR-018)
                TryInitCommand("SafeRenameCommand", () => SafeRenameCommand.Initialize(this, commandService));
                // Spec 030 T056 — toggle code analysis on/off
                TryInitCommand("ToggleCodeAnalysisCommand", () => AkmlSql.Shell.Shared.Analysis.ToggleCodeAnalysisCommand.Initialize(this, commandService));
                // Spec 030 T068 — disable formatting for selection
                TryInitCommand("DisableFormattingForSelectionCommand", () => DisableFormattingForSelectionCommand.Initialize(this, commandService));
                TryInitCommand("SendFeedbackCommand", () => SendFeedbackCommand.Initialize(this, commandService));
                TryInitCommand("ViewLogsCommand", () => ViewLogsCommand.Initialize(this, commandService));
                TryInitCommand("RefreshCacheCommand", () => RefreshCacheCommand.Initialize(this, commandService));

                // Phase 7 — Tab management and safety commands
                TryInitCommand("RestoreClosedTabCommand", () => RestoreClosedTabCommand.Initialize(this, commandService));
                TryInitCommand("CloseUnmodifiedCommand", () => CloseUnmodifiedCommand.Initialize(this, commandService));
                TryInitCommand("DuplicateTabCommand", () => DuplicateTabCommand.Initialize(this, commandService));
                TryInitCommand("PinTabCommand", () => PinTabCommand.Initialize(this, commandService));

                // Phase 7 US2 — SQL History panel
                TryInitCommand("HistoryPanelCommand", () => HistoryPanelCommand.Initialize(this, commandService));

                // Phase 8 US2 — Grid Copy/Export commands
                TryInitCommand("GridContextMenuWiring", () => GridContextMenuWiring.RegisterCommands(commandService));

                // Phase 8 US7 — Go to Definition & Peek Definition
                TryInitCommand("GoToDefinitionCommand", () => GoToDefinitionCommand.Initialize(this, commandService));
                TryInitCommand("PeekDefinitionCommand", () => PeekDefinitionCommand.Initialize(this, commandService));

                // Phase 8 US12 — Object Search & Find References
                TryInitCommand("ObjectSearchCommand", () => ObjectSearchCommand.Initialize(this, commandService));
                TryInitCommand("FindReferencesCommand", () => FindReferencesCommand.Initialize(this, commandService));

                // Phase 8 US3 — Command Palette
                TryInitCommand("CommandPaletteCommand", () => CommandPaletteCommand.Initialize(this, commandService));

                // Phase 8 US4 — Execute Current Statement
                TryInitCommand("ExecuteCurrentStatementCommand", () => ExecuteCurrentStatementCommand.Initialize(this, commandService));
                TryInitCommand("ExecuteToCursorCommand", () => ExecuteToCursorCommand.Initialize(this, commandService));

                // Phase 8 US5 — Document Outline
                TryInitCommand("DocumentOutlineCommand", () => DocumentOutlineCommand.Initialize(this, commandService));

                // Phase 8 US8 — Navigation commands
                TryInitCommand("NavigateStatementCommand", () => NavigateStatementCommand.Initialize(this, commandService));
                TryInitCommand("NavigateMatchingPairCommand", () => NavigateMatchingPairCommand.Initialize(this, commandService));

                // Formatting commands
                TryInitCommand("FormatDocumentCommand", () => FormatDocumentCommand.Initialize(this, commandService));
                TryInitCommand("FormatSelectionCommand", () => FormatSelectionCommand.Initialize(this, commandService));
                TryInitCommand("UnformatCommand", () => UnformatCommand.Initialize(this, commandService));

                // Phase 9 US2 — AI Explain
                TryInitCommand("AiExplainCommand", () => AiExplainCommand.Initialize(this, commandService));

                // Phase 9 US3 — AI Fix
                TryInitCommand("AiFixCommand", () => AiFixCommand.Initialize(this, commandService));

                // Phase 9 US6 — AI Chat Panel
                TryInitCommand("AiChatPanelCommand", () => AiChatPanelCommand.Initialize(this, commandService));

                // Phase 5 — Bulk Analysis
                TryInitCommand("BulkAnalysisCommand", () => BulkAnalysisCommand.Initialize(this, commandService));

                // Phase 10 — SQL Prompt Core Parity
                TryInitCommand("SnippetManagerCommand", () => SnippetManagerCommand.Initialize(this, commandService));
                // Spec 030 T044/T045 — Create Snippet from Selection + Surround With
                TryInitCommand("CreateFromSelectionCommand", () => AkmlSql.Shell.Shared.Snippets.CreateFromSelectionCommand.Initialize(this, commandService));
                TryInitCommand("SurroundWithCommand", () => AkmlSql.Shell.Shared.Snippets.SurroundWithCommand.Initialize(this, commandService));
                // Spec 030 T087 — Bulk Format wizard (FR-046)
                TryInitCommand("BulkFormatCommand", () => AkmlSql.Shell.Shared.Productivity.BulkFormatCommand.Initialize(this, commandService));
                TryInitCommand("BookmarkCommands", () => AkmlSql.Shell.Shared.Navigation.BookmarkCommands.Initialize(this, commandService));
                TryInitCommand("SplitTableCommand", () => SplitTableCommand.Initialize(this, commandService));
                // Spec 040 (T107) — AKML SQL › Active Style: style slots + Edit Styles…
                TryInitCommand("ActiveStyleMenuCommands", () => ActiveStyleMenuCommands.Initialize(this, commandService));
            }

            // Non-critical initialization — failures must not break the extension
            try
            {
                LoggerFactory.Initialize();
                Log.Information("AKML SQL package initializing for SSMS 22 (x64)");

                // Theme system init (spec 016 T015 — FR-007/FR-008/FR-009/FR-018/FR-019).
                try
                {
                    var themeSettings = AkmlSql.Core.Config.ConfigManager.Load();
                    AkmlSql.Shell.Shared.Ui.Theme.HostThemeWatcher.Instance.Initialize();
                    AkmlSql.Shell.Shared.Ui.Theme.ThemeRegistry.Instance.Initialize(
                        themeSettings.Theme,
                        AkmlSql.Shell.Shared.Ui.Theme.HostThemeWatcher.Instance.LastDetectedHostVariant,
                        AkmlSql.Shell.Shared.Ui.Theme.HostThemeWatcher.Instance.IsHighContrast);

                    // Spec 020 (FR-030): one-time first-launch theme migration. Idempotent; safe every launch.
                    AkmlSql.Shell.Shared.Ui.Theme.ThemeMigrationManager.Instance.RunIfNeeded();
                }
                catch (Exception themeEx)
                {
                    Log.Warning(themeEx, "Failed to initialize theme system; falling back to Light defaults");
                }

                var extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                LoadValidator.Validate(extensionDir);

                var statusBar = (IVsStatusbar)await GetServiceAsync(typeof(SVsStatusbar));
                if (statusBar != null)
                {
                    // Spec 030 T021 / FR-006: annotate the idle status text with the active
                    // formatting style when the user has opted into it (best-effort).
                    string? activeStyle = null;
                    try
                    {
                        var fmt = AkmlSql.Core.Config.ConfigManager.Load().Formatter;
                        if (fmt.ShowProfileInStatusBar) activeStyle = fmt.ActiveProfile;
                    }
                    catch (Exception cfgEx) { Log.Debug(cfgEx, "Status-bar active-style annotation: config load failed (using version only)"); }
                    StatusBarManager.SetLoaded(statusBar, activeStyle);
                }

                UpdateLauncher.LaunchIfDue();
                // A downloaded, verified update waiting from an earlier check: offer it once.
                UpdateStartupPrompt.ScheduleIfReady(JoinableTaskFactory, DisposalToken);

                // Launch Engine process for IntelliSense, formatting, analysis
                var engineLaunch = System.Threading.Tasks.Task.Run(() => EngineLifecycle.LaunchAsync());

                // Spec 040 (HIS-02): once the engine is up, tell History which queries this SSMS has
                // open; it closes stale open marks and reports the queries left open by an SSMS that
                // exited or crashed (kept for restore on start).
                _ = JoinableTaskFactory.RunAsync(() => ReconcileHistoryOpenStateAsync(engineLaunch));

                // Spec 040 (T104): the Active Style menu has its style list before it first opens.
                _ = engineLaunch.ContinueWith(_ => ActiveStyleCache.Instance.RefreshNow(),
                    System.Threading.Tasks.TaskScheduler.Default);

#if DEBUG
                // Spec 040 (T105): the menu table is tested against RegisteredCommands.Ids, so a
                // command registered without being listed there is flagged here.
                if (commandService != null) LogUnlistedCommands(commandService);
#endif

                ExecutionCapture.Initialize(this);
                ExecutionInterceptor.Initialize(this);
                TabManagementInitializer.Initialize(this);
                TransactionMonitor.Initialize(this);
                AiSettingsValidator.Initialize();

                // STANDARD TOOLBAR SQL HISTORY BUTTON — guarded add with accumulation
                // recovery. Adds exactly one button if none exists, does nothing if
                // one already exists, and hard-resets the Standard toolbar if more
                // than one exists (recovers from any historical accumulation bug).
                EnsureStandardToolbarHistoryButton();

                // Spec 040 (T180): the AKML SQL menu (built from AkmlMenuTable, once per process,
                // rebuilt when an older menu lacks the version marker) and the SQL editor's context
                // menu (one Format Document, one Active Style). Commands are registered above.
                EnsureTopLevelMenu(commandService);

                Log.Information("AKML SQL package initialized successfully for SSMS 22");
            }
            catch (Exception ex)
            {
                try { Log.Error(ex, "AKML SQL non-critical init failed for SSMS 22"); } catch { /* Intentional: logger may not be initialized */ }

                try
                {
                    var statusBar = (IVsStatusbar)await GetServiceAsync(typeof(SVsStatusbar));
                    if (statusBar != null)
                    {
                        StatusBarManager.SetFailed(statusBar);
                    }
                }
                catch
                {
                    // Swallow — we must never crash the IDE
                }
            }
        }

#if DEBUG
        /// <summary>
        /// Spec 040 (T105) — Debug builds only: logs every command registered in the AKML command set
        /// that <see cref="RegisteredCommands.Ids"/> does not list. Reads the service's registered
        /// commands through <c>MenuCommandService.GetCommandList</c> (protected), by reflection.
        /// </summary>
        private static void LogUnlistedCommands(OleMenuCommandService commandService)
        {
            try
            {
                var getList = typeof(MenuCommandService).GetMethod("GetCommandList",
                    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Guid) }, null);
                if (!(getList?.Invoke(commandService, new object[] { PackageGuids.AkmlSqlCmdSet }) is System.Collections.ICollection commands))
                    return;
                foreach (var command in commands)
                {
                    if (command is MenuCommand mc && !RegisteredCommands.Ids.Contains(mc.CommandID.ID))
                        Log.Warning("Command 0x{Id:X4} is registered but missing from RegisteredCommands.Ids", mc.CommandID.ID);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "RegisteredCommands check failed");
            }
        }
#endif

        /// <summary>
        /// Invokes a single command's Initialize call. Catches and logs any exception so that a
        /// failure in one command does not prevent subsequent commands from registering.
        /// </summary>
        private static void TryInitCommand(string commandName, Action init)
        {
            try
            {
                init();
            }
            catch (Exception ex)
            {
                try { Log.Warning(ex, "Command registration failed for {CommandName} — skipping (non-fatal)", commandName); } catch { /* Intentional: logger may not be initialized */ }
            }
        }

        // Process-static guard: EnsureStandardToolbarHistoryButton runs at most ONCE per process.
        private static int _toolbarHistoryDone;

        /// <summary>
        /// Ensures the SSMS Standard toolbar has exactly ONE SQL History button
        /// (SQL Prompt-style, placed next to New Query). Handles three cases:
        /// <list type="number">
        /// <item><description><b>Count == 0:</b> add exactly one button via <c>cmd.AddControl</c>.</description></item>
        /// <item><description><b>Count == 1:</b> do nothing — the correct state is already there
        ///   (either from a previous install's persisted binding or from this session's earlier add).</description></item>
        /// <item><description><b>Count &gt; 1:</b> accumulation detected. Call <c>standardBar.Reset()</c>
        ///   to wipe ALL customizations on the bar (the only reliable way to clear the persisted
        ///   state that SSMS 22 hides beneath per-control <c>Delete()</c>), then add exactly one.</description></item>
        /// </list>
        ///
        /// This combines the previous <c>CleanupStaleToolbarEntries</c> and
        /// <c>AddHistoryButtonToStandardToolbar</c> into a single guarded function
        /// so there's exactly one decision point for the toolbar state per process.
        /// </summary>
        private static void EnsureStandardToolbarHistoryButton()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _toolbarHistoryDone, 1, 0) != 0)
            {
                Log.Debug("EnsureStandardToolbarHistoryButton: already run this process, skipping");
                return;
            }

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                dynamic bars = dte.CommandBars;
                if (bars == null) return;

                dynamic standardBar = null;
                try { standardBar = bars["Standard"]; }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Standard toolbar not found — skipping History button setup");
                    return;
                }
                if (standardBar == null) return;

                int sqlHistoryCount = CountSqlHistoryControls(standardBar);
                Log.Information("EnsureStandardToolbarHistoryButton: Standard toolbar has {Count} SQL History control(s)", sqlHistoryCount);

                if (sqlHistoryCount == 1)
                {
                    // Already in the desired state — do nothing.
                    return;
                }

                if (sqlHistoryCount > 1)
                {
                    // Accumulation — reset the entire bar. This wipes all customizations
                    // but is the only reliable way to clear the persisted stale bindings
                    // that SSMS 22 hides beneath per-control Delete() APIs.
                    Log.Information("EnsureStandardToolbarHistoryButton: {Count} duplicates — resetting Standard toolbar", sqlHistoryCount);
                    try { standardBar.Reset(); }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Standard toolbar Reset() failed — falling back to Delete/Hide");
                        var knownLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SQL History" };
                        CleanupControls(standardBar, knownLabels, "Standard", depth: 0);
                    }
                    sqlHistoryCount = CountSqlHistoryControls(standardBar);
                }

                // Count is now 0 (either from the start or after reset). Add exactly one.
                if (sqlHistoryCount == 0)
                {
                    try
                    {
                        var cmd = dte.Commands.Item(
                            "{" + PackageGuids.AkmlSqlCmdSetString + "}",
                            CommandIds.CmdHistoryPanel);
                        if (cmd != null)
                        {
                            // Position 4: typically right after New Query, Open, Save.
                            var ctrl = cmd.AddControl(standardBar, 4);
                            try { ctrl.Tag = "AkmlSql.HistoryToolbar"; } catch { }
                            Log.Information("EnsureStandardToolbarHistoryButton: added SQL History button to Standard toolbar");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to add SQL History button to Standard toolbar");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnsureStandardToolbarHistoryButton failed (non-fatal)");
                System.Threading.Interlocked.Exchange(ref _toolbarHistoryDone, 0);
            }
        }

        /// <summary>
        /// Counts the controls on a CommandBar whose caption matches "SQL History"
        /// (case-insensitive, ampersand-stripped). Used by
        /// <see cref="EnsureStandardToolbarHistoryButton"/> to drive its state machine.
        /// </summary>
        private static int CountSqlHistoryControls(dynamic bar)
        {
            int count = 0;
            try
            {
                foreach (dynamic ctrl in bar.Controls)
                {
                    string cap;
                    try { cap = ((string)ctrl.Caption ?? string.Empty).Replace("&", "").Trim(); }
                    catch { continue; }
                    if (cap.Equals("SQL History", StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
            }
            catch { /* bar enumeration failed — treat as 0 */ }
            return count;
        }

        // Process-static guard: EnsureTopLevelMenu runs at most ONCE per process.
        private static int _menuInjected;

        // Spec 040 (T180): the AI ▸ popup of the AKML SQL menu, and whether it is shown now.
        private static object _aiPopup;
        private static bool _aiPopupVisible;
        private static bool _syncingAiMenu;

        /// <summary>
        /// Spec 040 (T180, X-01, FR-060, FR-034) — the "AKML SQL" menu on the DTE menu bar, built from
        /// <see cref="AkmlMenuTable.Entries"/> (the VSCT menu is invisible in SSMS 22): nested popups for
        /// the submenus, separators from <c>BeginGroup</c>, and the version marker
        /// <see cref="AkmlMenuTable.Marker"/> in the popup's <c>Tag</c>. An "AKML SQL" popup without the
        /// marker (an older build's menu) is deleted and the menu rebuilt, once; one with it is kept.
        /// Then the SQL editor's context menu gets Format Document and Active Style ▸. Runs after the
        /// commands are registered, and every COM failure is logged and skipped.
        /// </summary>
        private static void EnsureTopLevelMenu(OleMenuCommandService commandService)
        {
            if (System.Threading.Interlocked.CompareExchange(ref _menuInjected, 1, 0) != 0)
            {
                Log.Debug("EnsureTopLevelMenu: already injected this process, skipping");
                return;
            }

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                dynamic bars = dte.CommandBars;
                if (bars == null) return;

                try
                {
                    BuildAkmlMenu(dte, (object)bars);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "AKML SQL menu: could not build the menu (non-fatal)");
                }

                EnsureEditorContextMenu(dte, (object)bars);
                WatchAiMenuVisibility(commandService);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to create AKML SQL top-level menu (non-fatal)");
                System.Threading.Interlocked.Exchange(ref _menuInjected, 0);
            }
        }

        /// <summary>
        /// Spec 040 (T180) — applies <see cref="AkmlMenuTable.PlanMenuBar"/> to the menu bar: removes
        /// any "AKML SQL" popup without the current marker (or a duplicate), keeps a current one, and
        /// otherwise builds the menu just before Window.
        /// </summary>
        private static void BuildAkmlMenu(EnvDTE.DTE dte, object barsObject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dynamic bars = barsObject;
            dynamic menuBar = bars["Menu Bar"];
            if (menuBar == null) return;

            var controls = new List<object>();
            var seen = new List<(string Caption, string Tag)>();
            foreach (dynamic ctrl in menuBar.Controls)
            {
                object control = ctrl;
                controls.Add(control);
                seen.Add((ReadCaption(control), ReadTag(control)));
            }

            var plan = AkmlMenuTable.PlanMenuBar(seen);
            foreach (var index in plan.Remove)
            {
                RemoveControl(controls[index], "Menu Bar");
            }

            if (!plan.Add)
            {
                TrackAiPopup(controls[plan.Keep]);
                Log.Information("AKML SQL menu: the current menu ({Marker}) is already there, kept", AkmlMenuTable.Marker);
                return;
            }

            // A new popup just before the Window menu. msoControlPopup = 10, Temporary = true.
            dynamic popup = menuBar.Controls.Add(10, Type.Missing, Type.Missing, PositionBeforeWindow((object)menuBar), true);
            popup.Caption = AkmlMenuTable.MenuCaption;
            try { popup.Tag = AkmlMenuTable.Marker; }
            catch (Exception ex) { Log.Debug(ex, "AKML SQL menu: could not set the version marker"); }

            var stats = new MenuBuildStats();
            dynamic popupBar = popup.CommandBar;
            foreach (var entry in AkmlMenuTable.Entries)
            {
                AddMenuEntry(dte, (object)popupBar, entry, null, stats);
            }

            popup.Visible = true;
            Log.Information("AKML SQL menu built ({Marker}): {Added} items, {Failed} could not be added; removed {Removed} older menu(s)",
                AkmlMenuTable.Marker, stats.Added, stats.Failed, plan.Remove.Count);
        }

        /// <summary>The 1-based position of the Window menu (where AKML SQL goes), else the end.</summary>
        private static int PositionBeforeWindow(object menuBarObject)
        {
            dynamic menuBar = menuBarObject;
            int position = (int)menuBar.Controls.Count + 1;
            foreach (dynamic ctrl in menuBar.Controls)
            {
                if (AkmlMenuTable.NormalizeCaption(ReadCaption((object)ctrl)).StartsWith("Window", StringComparison.OrdinalIgnoreCase))
                {
                    try { position = (int)ctrl.Index; } catch { /* keep the end */ }
                    break;
                }
            }
            return position;
        }

        private sealed class MenuBuildStats
        {
            public int Added;
            public int Failed;
        }

        /// <summary>
        /// Spec 040 (T180) — adds one table entry to <paramref name="barObject"/> at <paramref name="before"/>
        /// (1-based; null = at the end): a command through <c>Command.AddControl</c> with the table's
        /// caption, or a temporary popup holding its children. Returns the control, or null.
        /// </summary>
        private static object AddMenuEntry(EnvDTE.DTE dte, object barObject, AkmlMenuEntry entry, int? before, MenuBuildStats stats)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dynamic bar = barObject;
            dynamic control = null;
            try
            {
                if (entry.IsSubmenu)
                {
                    // msoControlPopup = 10, Temporary = true: rebuilt with the menu each start.
                    control = bar.Controls.Add(10, Type.Missing, Type.Missing, before.HasValue ? (object)before.Value : Type.Missing, true);
                    control.Caption = entry.Caption;
                    dynamic childBar = control.CommandBar;
                    foreach (var child in entry.Children)
                    {
                        AddMenuEntry(dte, (object)childBar, child, null, stats);
                    }

                    if (entry.AiOnly)
                    {
                        _aiPopup = control;
                        _aiPopupVisible = IsAiMenuVisible();
                        control.Visible = _aiPopupVisible;
                    }
                    else
                    {
                        control.Visible = true;
                    }
                }
                else
                {
                    var command = dte.Commands.Item("{" + PackageGuids.AkmlSqlCmdSetString + "}", entry.CommandId);
                    control = command.AddControl(bar, before ?? (int)bar.Controls.Count + 1);
                    SetCaption((object)control, entry.Caption);
                }

                if (entry.BeginGroup) control.BeginGroup = true;
                stats.Added++;
            }
            catch (Exception ex)
            {
                stats.Failed++;
                Log.Debug(ex, "AKML SQL menu: could not add '{Entry}' (0x{Id:X4})", entry, entry.CommandId);
                if (control == null && !entry.IsSubmenu && entry.Caption != null)
                {
                    // As the old fallback did: show the item, disabled, rather than lose it silently.
                    try
                    {
                        control = bar.Controls.Add(1, Type.Missing, Type.Missing, before.HasValue ? (object)before.Value : Type.Missing, true);
                        control.Caption = entry.Caption;
                        control.Enabled = false;
                    }
                    catch { control = null; }
                }
            }
            return control;
        }

        /// <summary>Gives a command's control the table's text (the VSCT text stays the command's own).</summary>
        private static void SetCaption(object control, string caption)
        {
            if (caption == null) return;
            try
            {
                if (!string.Equals(AkmlMenuTable.NormalizeCaption(ReadCaption(control)), caption, StringComparison.Ordinal))
                {
                    ((dynamic)control).Caption = caption;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AKML SQL menu: could not set the caption '{Caption}'", caption);
            }
        }

        /// <summary>
        /// Spec 040 (T110, T180, FR-034) — the SQL query editor's context menu (picked by
        /// <see cref="ActiveStyleMenuCommands.PickEditorContextBar"/>) gets
        /// <see cref="AkmlMenuTable.EditorContextEntries"/>: Format Document, then Active Style ▸.
        /// Format Document is a persistent control (<c>Command.AddControl</c>), so it is found again on
        /// the next start: <see cref="AkmlMenuTable.PlanEditorContext"/> keeps one of each, removes
        /// extra copies (earlier builds added one per start) and adds only what is missing.
        /// </summary>
        private static void EnsureEditorContextMenu(EnvDTE.DTE dte, object barsObject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dynamic bars = barsObject;
            try
            {
                var byName = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (dynamic bar in bars)
                {
                    string name;
                    try { name = (string)bar.Name; }
                    catch { continue; }
                    if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name)) byName[name] = (object)bar;
                }
                Log.Debug("Active Style: context menu candidates: {Names}", string.Join(", ", byName.Keys.Where(n =>
                    n.IndexOf("Context", StringComparison.OrdinalIgnoreCase) >= 0
                    || string.Equals(n, "Code Window", StringComparison.OrdinalIgnoreCase))));

                // VS's generic "Code Window" used to win by coming first; SSMS's query editor menu is
                // "SQL Files Editor Context".
                var targetName = ActiveStyleMenuCommands.PickEditorContextBar(byName.Keys);
                if (targetName == null)
                {
                    Log.Information("Active Style: no editor context menu found");
                    return;
                }
                dynamic target = byName[targetName];

                var controls = new List<object>();
                var seen = new List<(string Caption, bool Visible)>();
                foreach (dynamic ctrl in target.Controls)
                {
                    object control = ctrl;
                    controls.Add(control);
                    seen.Add((ReadCaption(control), ReadVisible(control)));
                }

                var plans = AkmlMenuTable.PlanEditorContext(seen);
                foreach (var index in plans.SelectMany(p => p.Remove))
                {
                    RemoveControl(controls[index], targetName);
                }

                // What each entry ends up as: the kept control, else one added next to its neighbours.
                var placed = new object[plans.Count];
                for (var i = 0; i < plans.Count; i++)
                {
                    if (plans[i].Add) continue;
                    placed[i] = controls[plans[i].Keep];
                    if (plans[i].Reveal)
                    {
                        try { ((dynamic)placed[i]).Visible = true; }
                        catch (Exception ex) { Log.Debug(ex, "Active Style: could not show a hidden item"); }
                    }
                }

                var stats = new MenuBuildStats();
                for (var i = 0; i < plans.Count; i++)
                {
                    if (!plans[i].Add) continue;
                    placed[i] = AddMenuEntry(dte, (object)target, AkmlMenuTable.EditorContextEntries[i], PositionFor(placed, i), stats);
                }

                Log.Information("Active Style: editor context menu '{Name}' — kept {Kept}, added {Added}, removed {Removed} duplicate(s)",
                    targetName, plans.Count(p => !p.Add), stats.Added, plans.Sum(p => p.Remove.Count));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Active Style: could not add to the editor context menu");
            }
        }

        /// <summary>
        /// Where entry <paramref name="index"/> goes so the entries stay in table order: right after
        /// the nearest earlier one that is placed, else before the nearest later one, else at the end
        /// (null).
        /// </summary>
        private static int? PositionFor(object[] placed, int index)
        {
            try
            {
                for (var i = index - 1; i >= 0; i--)
                {
                    if (placed[i] != null) return (int)((dynamic)placed[i]).Index + 1;
                }
                for (var i = index + 1; i < placed.Length; i++)
                {
                    if (placed[i] != null) return (int)((dynamic)placed[i]).Index;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Active Style: could not read a menu position; adding at the end");
            }
            return null;
        }

        /// <summary>Deletes a control; when SSMS refuses, hides it instead.</summary>
        private static void RemoveControl(object control, string barName)
        {
            var caption = ReadCaption(control);
            try
            {
                ((dynamic)control).Delete();
                Log.Information("Removed '{Caption}' from '{Bar}'", caption, barName);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Delete failed for '{Caption}' on '{Bar}'; hiding it", caption, barName);
                try { ((dynamic)control).Visible = false; }
                catch (Exception hideEx) { Log.Debug(hideEx, "Could not hide '{Caption}' on '{Bar}'", caption, barName); }
            }
        }

        private static string ReadCaption(object control)
        {
            try { return (string)((dynamic)control).Caption; }
            catch { return null; }
        }

        private static string ReadTag(object control)
        {
            try { return (string)((dynamic)control).Tag; }
            catch { return null; }
        }

        private static bool ReadVisible(object control)
        {
            try { return (bool)((dynamic)control).Visible; }
            catch { return true; }
        }

        /// <summary>
        /// The AI ▸ submenu follows the same rule as the AI commands (<see cref="AiCommandVisibility"/>:
        /// AI enabled and privacy mode not "disabled"), asked through a stand-in command.
        /// </summary>
        private static bool IsAiMenuVisible()
        {
            var probe = new OleMenuCommand((_, _) => { }, new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiChatPanel));
            AiCommandVisibility.OnBeforeQueryStatus(probe, EventArgs.Empty);
            return probe.Visible;
        }

        /// <summary>Finds the AI ▸ popup inside a kept AKML SQL menu, so its visibility stays current.</summary>
        private static void TrackAiPopup(object akmlPopup)
        {
            var aiCaption = AkmlMenuTable.Entries.FirstOrDefault(e => e.AiOnly)?.Caption;
            if (aiCaption == null) return;
            try
            {
                foreach (dynamic child in ((dynamic)akmlPopup).CommandBar.Controls)
                {
                    object control = child;
                    if (!string.Equals(AkmlMenuTable.NormalizeCaption(ReadCaption(control)), aiCaption, StringComparison.OrdinalIgnoreCase)) continue;
                    _aiPopup = control;
                    _aiPopupVisible = ReadVisible(control);
                    SyncAiMenuVisibility();
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AKML SQL menu: could not find the AI submenu");
            }
        }

        /// <summary>
        /// Keeps AI ▸ in step with the AI setting while SSMS runs: Format Document — the menu's first
        /// item — is queried whenever the AKML SQL menu opens, so its status check updates AI ▸.
        /// </summary>
        private static void WatchAiMenuVisibility(OleMenuCommandService commandService)
        {
            if (_aiPopup == null || commandService == null) return;
            try
            {
                if (commandService.FindCommand(new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdFormatDocument)) is OleMenuCommand format)
                {
                    format.BeforeQueryStatus += (_, _) => SyncAiMenuVisibility();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AKML SQL menu: could not watch the AI setting");
            }
        }

        private static void SyncAiMenuVisibility()
        {
            var popup = _aiPopup;
            if (popup == null || _syncingAiMenu || !ThreadHelper.CheckAccess()) return;
            var visible = IsAiMenuVisible();
            if (visible == _aiPopupVisible) return;

            // Showing or hiding a control can query command status again (this handler included).
            _syncingAiMenu = true;
            _aiPopupVisible = visible;
            try
            {
                ((dynamic)popup).Visible = visible;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AKML SQL menu: could not update the AI submenu; no longer tracking it");
                _aiPopup = null;
            }
            finally
            {
                _syncingAiMenu = false;
            }
        }

        /// <summary>
        /// Recursively deletes AKML SQL controls from a CommandBar's Controls collection.
        /// For popup controls, recurses into the child CommandBar. Returns the total
        /// number of controls deleted from this bar and all descendants.
        /// </summary>
        private static int CleanupControls(dynamic bar, HashSet<string> knownLabels, string barName, int depth)
        {
            if (depth > 4) return 0; // safety against infinite recursion

            int deleted = 0;
            var toDelete = new List<object>();
            var toRecurseInto = new List<object>();

            try
            {
                foreach (dynamic ctrl in bar.Controls)
                {
                    string cap;
                    try { cap = ((string)ctrl.Caption ?? string.Empty).Replace("&", "").Trim(); }
                    catch { continue; }

                    if (knownLabels.Contains(cap))
                    {
                        toDelete.Add(ctrl);
                    }
                    else
                    {
                        // Popup control (msoControlPopup = 10) with a foreign caption —
                        // recurse into its child bar so we can find nested AKML SQL entries
                        // (e.g. if they ended up under Tools > External Tools > AKML SQL).
                        try
                        {
                            int ctrlType = (int)ctrl.Type;
                            if (ctrlType == 10) // msoControlPopup
                            {
                                toRecurseInto.Add(ctrl);
                            }
                        }
                        catch { /* control doesn't expose Type */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CleanupControls: enumeration failed on bar {Bar} (depth {Depth})", barName, depth);
                return 0;
            }

            foreach (dynamic ctrl in toDelete)
            {
                string cap = "";
                try { cap = (string)ctrl.Caption; } catch { }

                // Primary: try to delete the control outright.
                bool deletedOk = false;
                try
                {
                    ctrl.Delete();
                    deletedOk = true;
                    deleted++;
                    Log.Debug("Deleted control '{Caption}' from CommandBar '{Bar}'", cap, barName);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Delete failed for control '{Caption}' on bar {Bar}", cap, barName);
                }

                // Fallback: hide the control so the user doesn't see it even if the
                // persisted entry survived. This handles SSMS 22's quirk where
                // cmd.AddControl bindings persist at a layer beneath Delete().
                if (!deletedOk)
                {
                    try
                    {
                        ctrl.Visible = false;
                        Log.Debug("Hid control '{Caption}' on bar {Bar} (delete failed, visibility=false fallback)", cap, barName);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to hide control '{Caption}' on bar {Bar}", cap, barName);
                    }
                }
            }

            // Recurse into non-AKML popups
            foreach (dynamic popup in toRecurseInto)
            {
                try
                {
                    dynamic childBar = popup.CommandBar;
                    if (childBar != null)
                    {
                        string childName = barName + " > " + (string)popup.Caption;
                        deleted += CleanupControls(childBar, knownLabels, childName, depth + 1);
                    }
                }
                catch { /* not all popups expose a child CommandBar */ }
            }

            return deleted;
        }

        // AddHistoryButtonToStandardToolbar was removed — every call to
        // cmd.AddControl(standardBar, 4) persisted a new binding at a layer beneath
        // our Delete() calls, causing 4 → 6 → 8 → 9 SQL History duplicates to
        // accumulate across package reloads. Users can still open SQL History via:
        //   • The keyboard shortcut Ctrl+Alt+H
        //   • The AKML SQL top-level menu (injected by EnsureTopLevelMenu)
        //   • The editor margin toolbar on SQL files (WPF, no DTE involvement)

        /// <summary>
        /// Spec 040 (HIS-02): after the engine launch completes, sends
        /// <see cref="OpenStateReporter.BuildReconcileRequest"/> for the documents open now and keeps
        /// the reply's restorable entries in <see cref="HistoryRestoreState"/>. Best effort — History
        /// works without it.
        /// </summary>
        private async System.Threading.Tasks.Task ReconcileHistoryOpenStateAsync(System.Threading.Tasks.Task engineLaunch)
        {
            try
            {
                await engineLaunch;
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

                var documents = new System.Collections.Generic.List<(string FullName, string Key)>();
                try
                {
                    if (await GetServiceAsync(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
                    {
                        foreach (EnvDTE.Document doc in dte.Documents)
                        {
                            var name = doc.FullName;
                            DocumentSessionKeys.TryGet(name, out var key);
                            documents.Add((name, string.IsNullOrEmpty(key) ? null : key));
                        }
                    }
                }
                catch (Exception docEx)
                {
                    Log.Debug(docEx, "History reconcile: could not list open documents");
                }

                await System.Threading.Tasks.TaskScheduler.Default;
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                var request = OpenStateReporter.BuildReconcileRequest(
                    System.Diagnostics.Process.GetCurrentProcess().Id, documents);
                var response = await client.SendRequestAsync<
                    AkmlSql.Core.Ipc.Messages.HistoryActionResponse,
                    AkmlSql.Core.Ipc.Messages.HistoryActionRequest>(
                    AkmlSql.Core.Ipc.MessageTypes.HistoryAction, request, timeoutMs: 10000);

                HistoryRestoreState.RestorableEntryIds = response?.RestorableEntryIds ?? Array.Empty<long>();
                Log.Information("History reconcile: {Count} queries were open when SSMS last closed",
                    HistoryRestoreState.RestorableEntryIds.Length);

                // Spec 040 (T144, HIS-14): reopen them as the History settings say (always / ask / never).
                if (HistoryRestoreState.RestorableEntryIds.Length > 0)
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                    await new HistoryRestoreService().RestoreAsync(HistoryRestoreState.RestorableEntryIds);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "History reconcile failed (non-fatal)");
            }
        }

        /// <summary>
        /// Spec 040 (HIS-02): SSMS asks to close. Mark it first, so the tabs shutdown closes stay
        /// open in History and can be offered for restore on the next start — tentatively: the
        /// user can still cancel at a "save changes?" prompt (see <c>ShutdownState</c>).
        /// </summary>
        protected override int QueryClose(out bool canClose)
        {
            ExecutionCapture.CloseQueried();
            return base.QueryClose(out canClose);
        }

        protected override void Dispose(bool disposing)
        {
            ExecutionCapture.ShutdownBegun();
            if (disposing)
            {
                TransactionMonitor.Shutdown();
                LoggerFactory.Shutdown();
            }

            base.Dispose(disposing);
        }
    }
}
