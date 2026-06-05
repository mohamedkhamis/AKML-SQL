using System;
using System.Collections.Concurrent;
using System.Reflection;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Extracts the active SSMS connection info from the DTE window caption.
    /// SSMS shows "ServerName.DatabaseName - filename" in query window titles.
    /// This avoids SSMS-internal COM interop (ScriptFactory) which requires
    /// version-specific assemblies and breaks across SSMS 20/21/22.
    /// </summary>
    internal static class SsmsConnectionDetector
    {
        // Dedupe "cannot silently reuse this auth" warnings so that the 10×500ms
        // retry loop in ConnectionWiringHelper doesn't emit the same Warning ten
        // times on a single session bind. Keyed by server|database|authMode; the
        // content of the warning is identical across retries, so seeing it once
        // is enough for the user.
        private static readonly ConcurrentDictionary<string, byte> _warnedUnusableAuth
            = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Authentication mode derived from the SSMS document's <c>AuthenticationType</c>
        /// DTE property. Drives how the engine's connection string is built.
        /// </summary>
        internal enum AuthMode
        {
            /// <summary>Auth type could not be read — fall back to Windows integrated.</summary>
            Unknown,
            /// <summary>Classic Windows integrated auth (Kerberos/NTLM).</summary>
            Windows,
            /// <summary>Azure AD integrated — engine inherits the user's AAD token.</summary>
            AzureAdIntegrated,
            /// <summary>SQL Server authentication (username + password) — engine connects once the user supplies a credential (spec 029).</summary>
            SqlPassword,
            /// <summary>AAD Interactive/MFA/Password (and similar) — engine cannot silently reuse these credentials.</summary>
            Unsupported
        }
        /// <summary>
        /// Attempts to detect the SQL Server connection for the CURRENTLY ACTIVE
        /// document (whatever SSMS has focused). Kept for call sites that only
        /// care about the active window (e.g. execution-time safety checks).
        /// For per-text-view detection use the overload that takes an <see cref="IWpfTextView"/>.
        /// Must be called on the UI thread.
        /// </summary>
        public static ConnectionResult TryDetectConnection(IServiceProvider serviceProvider)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var dte = serviceProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null)
                {
                    Log.Debug("TryDetectConnection(provider): DTE service unavailable → null");
                    return null;
                }
                if (dte.ActiveDocument == null)
                {
                    Log.Debug("TryDetectConnection(provider): no active document → null");
                    return null;
                }
                if (dte.ActiveDocument.ActiveWindow == null)
                {
                    Log.Debug("TryDetectConnection(provider): active document '{Name}' has no active window → null", dte.ActiveDocument.Name);
                    return null;
                }

                var caption = dte.ActiveDocument.ActiveWindow.Caption;
                Log.Debug("TryDetectConnection(provider): window caption = '{Caption}'", caption);

                if (string.IsNullOrEmpty(caption))
                {
                    Log.Debug("TryDetectConnection(provider): empty caption → null");
                    return null;
                }

                var (authMode, rawAuth) = ReadAuthModeFromDocument(dte.ActiveDocument);
                return ParseCaption(caption, authMode, rawAuth);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TryDetectConnection(provider): unexpected exception");
                return null;
            }
        }

        /// <summary>
        /// Attempts to detect the SQL Server connection for a SPECIFIC text view
        /// by resolving the view's file path to the matching DTE Document and
        /// reading ITS window caption. This prevents the cross-window leak where
        /// a new Server-B query window was being assigned Server-A's connection
        /// because DTE.ActiveDocument still pointed to the previously focused
        /// Server-A window at the moment the new view was created.
        ///
        /// CRITICAL: this overload does NOT fall back to
        /// <see cref="TryDetectConnection(IServiceProvider)"/>. If the text view
        /// has no resolvable file path yet (brand-new unsaved buffer), we return
        /// <c>null</c> so the caller's retry loop can try again after SSMS finishes
        /// wiring up the document. Falling back to ActiveDocument would pick up the
        /// previously focused window and leak the wrong connection info.
        /// Must be called on the UI thread.
        /// </summary>
        public static ConnectionResult TryDetectConnection(IServiceProvider serviceProvider, IWpfTextView textView)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (textView == null)
                {
                    return TryDetectConnection(serviceProvider);
                }

                var dte = serviceProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null)
                {
                    return null;
                }

                // Resolve the text view's file path via ITextDocument.
                string filePath = null;
                try
                {
                    if (textView.TextBuffer.Properties.TryGetProperty<ITextDocument>(typeof(ITextDocument), out var textDoc))
                    {
                        filePath = textDoc?.FilePath;
                    }
                }
                catch { /* not fatal */ }

                if (string.IsNullOrEmpty(filePath))
                {
                    // Brand-new unsaved buffer — the document isn't registered with
                    // DTE yet. Return null so the retry loop waits 500ms and tries
                    // again; by then the file path and caption will be wired up.
                    Log.Debug("SsmsConnectionDetector: no file path for text view, returning null (retry expected)");
                    return null;
                }

                // Find the DTE Document with matching FullName (case-insensitive).
                //
                // IMPORTANT: use an indexed loop instead of `foreach` so that a
                // single malformed COM object (throwing from its enumerator's
                // MoveNext or from Item(i)) only skips THAT document. A foreach
                // here would swallow the exception at the outer try/catch and
                // abandon the rest of the collection, silently returning null
                // for a valid text view whose match sits further down the list.
                EnvDTE.Documents docs = null;
                int count = 0;
                try
                {
                    docs = dte.Documents;
                    count = docs?.Count ?? 0;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "SsmsConnectionDetector: unable to read DTE.Documents.Count");
                    return null;
                }

                for (int i = 1; i <= count; i++)
                {
                    string docFullName = null;
                    EnvDTE.Window win = null;
                    try
                    {
                        var doc = docs.Item(i);
                        if (doc == null) continue;
                        docFullName = doc.FullName;
                        if (!string.Equals(docFullName, filePath, StringComparison.OrdinalIgnoreCase))
                            continue;
                        win = doc.ActiveWindow;
                    }
                    catch
                    {
                        // This specific document is broken — skip it and keep
                        // looking for a match in the remaining documents.
                        continue;
                    }

                    if (win != null && !string.IsNullOrEmpty(win.Caption))
                    {
                        Log.Debug("SsmsConnectionDetector: matched text view to document '{Caption}'", win.Caption);

                        EnvDTE.Document matchedDoc = null;
                        try { matchedDoc = docs.Item(i); } catch { /* ignore */ }
                        var (authMode, rawAuth) = ReadAuthModeFromDocument(matchedDoc);
                        return ParseCaption(win.Caption, authMode, rawAuth);
                    }
                }

                // No matching document found — return null so the retry loop
                // waits and tries again. NO fallback to ActiveDocument.
                Log.Debug("SsmsConnectionDetector: no DTE document matched '{Path}' yet, returning null", filePath);
                return null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SsmsConnectionDetector: per-textview detection failed");
                return null;
            }
        }

        /// <summary>
        /// Parses an SSMS window caption like "ServerName.DatabaseName - QueryFile.sql"
        /// to extract server and database names. Overload kept for tests and legacy callers
        /// that don't have auth info; defaults to Windows auth (legacy behavior).
        /// </summary>
        internal static ConnectionResult ParseCaption(string caption)
            => ParseCaption(caption, AuthMode.Unknown, null);

        /// <summary>
        /// Parses the caption AND builds the engine connection string using the supplied
        /// <paramref name="authMode"/>. When the auth mode can't be silently reused by
        /// the out-of-process engine (SQL auth, AAD Interactive/Password), the returned
        /// <see cref="ConnectionResult"/> has <see cref="ConnectionResult.IsEngineUsable"/>
        /// set to <c>false</c> and <see cref="ConnectionResult.ConnectionString"/> is <c>null</c>.
        /// Caller must check and skip engine-side schema loading in that case.
        /// </summary>
        internal static ConnectionResult ParseCaption(string caption, AuthMode authMode, string rawAuthType)
        {
            // SSMS 22 caption format: "filename.sql - ServerName.DatabaseName (Username (SPID))"
            // SSMS 20 caption format: "ServerName.DatabaseName - filename.sql"
            // We need to handle both formats.

            var dashIndex = caption.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIndex <= 0)
                return null;

            // Try SSMS 22 format first: connection info is AFTER the dash
            var afterDash = caption.Substring(dashIndex + 3).Trim();

            // Before stripping, also try to extract the username from the
            // "(Username (SPID))" trailing pattern. The username is what lets
            // us distinguish Windows auth (DOMAIN\user), AAD (user@tenant),
            // and SQL auth (bare name like 'sa') when DTE's AuthenticationType
            // property is unavailable — which is the common case in SSMS today.
            string captionUserName = null;
            var parenIdx = afterDash.IndexOf(" (", StringComparison.Ordinal);
            if (parenIdx > 0)
            {
                var trailing = afterDash.Substring(parenIdx + 1).Trim(); // "(sa (69))"
                afterDash = afterDash.Substring(0, parenIdx).Trim();

                if (trailing.Length >= 2 && trailing[0] == '(' && trailing[trailing.Length - 1] == ')')
                {
                    var inner = trailing.Substring(1, trailing.Length - 2).Trim(); // "sa (69)"
                    // Username is everything before the FINAL " (SPID)" segment.
                    var spidIdx = inner.LastIndexOf(" (", StringComparison.Ordinal);
                    var user = spidIdx > 0 ? inner.Substring(0, spidIdx).Trim() : inner.Trim();
                    if (!string.IsNullOrWhiteSpace(user))
                        captionUserName = user;
                }
            }

            string server;
            string database;

            // Try parsing "server.database" from afterDash.
            // Split at the LAST dot, not the first: the database is the final token, but the SERVER
            // can itself contain dots — a remote FQDN ("srv.corp.contoso.com") or an IP
            // ("10.0.0.5"). Splitting at the first dot truncated the server to "srv"/"10" and leaked
            // the rest into the database, so the engine connected to a bogus host and loaded no
            // schema — while dotless local names ((local) / localhost / MACHINE\INSTANCE) worked.
            // That was the "any remote server cannot load schema, just local only" bug.
            var dotIndex = afterDash.LastIndexOf('.');
            if (dotIndex > 0 && !afterDash.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                // SSMS 22 format: afterDash = "(local).StockProduction" or "srv.corp.contoso.com.MyDb"
                server = afterDash.Substring(0, dotIndex).Trim();
                database = afterDash.Substring(dotIndex + 1).Trim();
            }
            else
            {
                // Try SSMS 20 format: connection info is BEFORE the dash. Same last-dot rule.
                var beforeDash = caption.Substring(0, dashIndex).Trim();
                dotIndex = beforeDash.LastIndexOf('.');
                if (dotIndex > 0 && !beforeDash.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                {
                    server = beforeDash.Substring(0, dotIndex).Trim();
                    database = beforeDash.Substring(dotIndex + 1).Trim();
                }
                else
                {
                    return null; // Can't parse connection
                }
            }

            if (string.IsNullOrEmpty(server))
                return null;

            // Heuristic: if DTE couldn't tell us the auth type but the caption
            // shows a bare username (no DOMAIN\ prefix, no user@tenant UPN),
            // it's almost certainly a SQL Server login. Classify as SqlPassword
            // (spec 029): the engine still can't silently reuse SQL auth (no
            // password across the process boundary — see R-017 in spec 014), but
            // instead of giving up we let the user supply the password once via
            // the click-to-enter affordance, after which the engine connects with
            // a SQL-auth connection string. Until then ConnectionString stays null
            // (IsEngineUsable=false), so we never build an Integrated Security
            // string that would fail with a noisy 4060 on every session bind.
            if (authMode == AuthMode.Unknown && !string.IsNullOrEmpty(captionUserName))
            {
                bool hasDomainPrefix = captionUserName.IndexOf('\\') >= 0;
                bool looksLikeUpn = captionUserName.IndexOf('@') >= 0;

                if (!hasDomainPrefix && !looksLikeUpn)
                {
                    Log.Debug(
                        "ParseCaption: caption-user='{User}' has no domain/UPN markers → inferring SQL auth (SqlPassword) for '{Caption}'",
                        captionUserName, caption);
                    authMode = AuthMode.SqlPassword;
                    if (string.IsNullOrEmpty(rawAuthType))
                        rawAuthType = $"inferred-SqlAuth(user='{captionUserName}')";
                }
            }

            var connStr = BuildEngineConnectionString(server, database, authMode);
            var usable = connStr != null;

            if (usable)
            {
                // Debug level: ParseCaption runs on every F5 (ExecutionCommandFilter
                // calls us from its DTE Query.Execute hook). The "real" session-bind
                // log is ConnectionWiringHelper.SendConnectionChangedAsync's
                // "Sent ConnectionChanged: …" Info line, which fires once per
                // session-to-server binding, not once per keypress.
                Log.Debug(
                    "SsmsConnectionDetector: parsed '{Caption}' → server='{Server}' database='{Database}' auth={AuthMode} (raw='{RawAuth}')",
                    caption, server, database, authMode, rawAuthType ?? "(null)");
            }
            else
            {
                if (authMode == AuthMode.SqlPassword)
                {
                    // SQL auth is handled by the click-to-enter affordance (spec 029) — no scary
                    // "IntelliSense disabled" warning. The wiring/margin take over from here.
                    Log.Debug(
                        "SsmsConnectionDetector: SQL auth for {Server}.{Database} (login='{Login}') — schema loads once a credential is stored/entered",
                        server, database, captionUserName);
                }
                else
                {
                    // One Warning per (server, database, authMode) — retries and repeated
                    // detect calls stay quiet after the first occurrence.
                    var dedupeKey = $"{server}|{database}|{authMode}";
                    if (_warnedUnusableAuth.TryAdd(dedupeKey, 0))
                    {
                        // IMPORTANT: each unique placeholder name binds to ONE argument in
                        // Serilog — repeated names overwrite each other in the property bag.
                        // Keep placeholder names unique and match the argument count exactly.
                        Log.Warning(
                            "AKML SQL IntelliSense disabled for {Server}.{Database}: " +
                            "this window is using {AuthMode} (raw='{RawAuth}'), which the out-of-process " +
                            "engine cannot silently reuse. To enable IntelliSense, either (a) reconnect " +
                            "this window using Windows authentication, or (b) grant your Windows user " +
                            "access to {TargetDatabase} in SQL Server. Caption='{Caption}'.",
                            server, database, authMode, rawAuthType ?? "(null)", database, caption);
                    }
                    else
                    {
                        Log.Debug(
                            "SsmsConnectionDetector: repeat unusable-auth detection for {Server}.{Database} ({AuthMode}) — warning already emitted",
                            server, database, authMode);
                    }
                }
            }

            return new ConnectionResult
            {
                Server = server,
                Database = database,
                Login = captionUserName,
                ConnectionString = connStr,
                AuthMode = authMode,
                IsEngineUsable = usable
            };
        }

        /// <summary>
        /// Builds the SQL-authentication connection string the engine uses once the user's password
        /// is available (spec 029). Uses <see cref="System.Data.SqlClient.SqlConnectionStringBuilder"/>
        /// so the password is escaped correctly (semicolons, quotes, equals) — never hand-concatenated.
        /// The keyword set is parse-compatible with the engine's Microsoft.Data.SqlClient.
        /// <para>
        /// Transport: <c>Encrypt=true</c> — unlike the engine's Windows/AAD shadow connections, a
        /// SQL-auth connection carries a reusable password, so the wire (login + data) is encrypted to
        /// defeat passive network interception. <c>TrustServerCertificate=true</c> is kept because
        /// internal SQL Servers almost always present a self-signed certificate that would not chain-
        /// validate; this mirrors SSMS 22's own default (encrypt + trust). The residual exposure is an
        /// active MITM that substitutes a certificate; full validation (<c>TrustServerCertificate=false</c>)
        /// is a future opt-in, not a safe default for internal self-signed-cert servers.
        /// </para>
        /// </summary>
        internal static string BuildSqlAuthConnectionString(string server, string database, string login, string password)
        {
            var b = new System.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                UserID = login,
                Password = password,
                IntegratedSecurity = false,
                ApplicationName = "AKML SQL Engine",
                TrustServerCertificate = true,
                Encrypt = true,   // encrypt the password-bearing connection on the wire (spec 029 security review)
                ConnectTimeout = 5
            };
            return b.ConnectionString;
        }

        // Resolved once (lazily) and cached — the SSMS process loads hundreds of assemblies, so we
        // don't want to scan them on every detect/poll. null-sentinel via _scriptFactoryResolved.
        private static Type _scriptFactoryType;
        private static bool _scriptFactoryResolved;

        /// <summary>
        /// Spec 029 follow-up: read the SQL-auth password SSMS already holds for the active query
        /// window, so the user never re-types a password SSMS has. Reads
        /// <c>ScriptFactory.Instance.CurrentlyActiveWndConnectionInfo.UIConnectionInfo.Password</c>
        /// entirely by reflection — there is NO compile-time dependency on SSMS assemblies, so this is
        /// a silent no-op on VS 2026 / older SSMS where the types or the active connection are absent
        /// (the caller then falls back to the stored credential, then the prompt). The password is
        /// returned ONLY when the active connection's server + login match the window being wired, so
        /// one window's credential is never handed to another window's engine session.
        /// <para>MUST be called on the UI thread (the SSMS ScriptFactory singleton is UI-affine).</para>
        /// </summary>
        internal static bool TryGetActiveSqlAuthPassword(string expectedServer, string expectedLogin, out string password)
        {
            password = null;
            if (string.IsNullOrEmpty(expectedServer) || string.IsNullOrEmpty(expectedLogin))
                return false;

            try
            {
                var sfType = ResolveScriptFactoryType();
                if (sfType == null) return false;

                var sf = sfType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (sf == null) return false;

                var caw = sf.GetType().GetProperty("CurrentlyActiveWndConnectionInfo")?.GetValue(sf);
                var uci = caw?.GetType().GetProperty("UIConnectionInfo")?.GetValue(caw);
                if (uci == null) return false;

                var uciType = uci.GetType();
                var srv = uciType.GetProperty("ServerName")?.GetValue(uci) as string;
                var usr = uciType.GetProperty("UserName")?.GetValue(uci) as string;

                // Prefer the plaintext Password, but SSMS 22 commonly keeps the real secret only in
                // InMemoryPassword (a SecureString) and leaves the plaintext property empty unless
                // persist-security-info is set — so marshal the SecureString when Password is blank.
                var pwd = uciType.GetProperty("Password")?.GetValue(uci) as string;
                if (string.IsNullOrEmpty(pwd))
                {
                    var secure = uciType.GetProperty("InMemoryPassword")?.GetValue(uci) as System.Security.SecureString;
                    pwd = SecureStringToString(secure);
                }

                if (string.IsNullOrEmpty(pwd))
                    return false; // Windows/AAD auth, or SSMS didn't retain a password — nothing to inherit

                // Only inherit when the active connection IS the window we're wiring (server + login).
                if (!ServerHostMatches(srv, expectedServer)) return false;
                if (!string.Equals((usr ?? string.Empty).Trim(), expectedLogin.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;

                password = pwd;
                Log.Debug("TryGetActiveSqlAuthPassword: inherited SQL password from SSMS for {Server}/{Login}",
                    expectedServer, expectedLogin);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TryGetActiveSqlAuthPassword: reflection into the SSMS connection failed (non-fatal — falling back)");
                return false;
            }
        }

        private static Type ResolveScriptFactoryType()
        {
            if (_scriptFactoryResolved) return _scriptFactoryType;
            const string fullName = "Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory";
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType(fullName, throwOnError: false);
                        if (t != null) { _scriptFactoryType = t; break; }
                    }
                    catch { /* skip assemblies that can't be inspected */ }
                }
            }
            catch { /* ignore */ }
            _scriptFactoryResolved = true;
            return _scriptFactoryType;
        }

        /// <summary>Lenient server match: SSMS may format the name differently than the caption
        /// (case, default instance, a trailing port/instance). Compare the host token before any
        /// <c>\</c> or <c>,</c>.</summary>
        private static bool ServerHostMatches(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            a = a.Trim(); b = b.Trim();
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            string Host(string s)
            {
                int i = s.IndexOfAny(new[] { '\\', ',' });
                return (i > 0 ? s.Substring(0, i) : s).Trim();
            }
            return string.Equals(Host(a), Host(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Marshals a <see cref="System.Security.SecureString"/> to a managed string,
        /// zero-freeing the unmanaged buffer. Returns null for a null/empty input.</summary>
        private static string SecureStringToString(System.Security.SecureString secure)
        {
            if (secure == null || secure.Length == 0) return null;
            IntPtr bstr = IntPtr.Zero;
            try
            {
                bstr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(secure);
                return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(bstr);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SecureStringToString: marshal failed");
                return null;
            }
            finally
            {
                if (bstr != IntPtr.Zero)
                    System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(bstr);
            }
        }

        /// <summary>
        /// Builds the connection string the engine process will use. Returns <c>null</c>
        /// when the auth mode cannot be silently reused across the process boundary
        /// (e.g. SQL auth where we'd need the password, or AAD Interactive which would
        /// pop a second browser prompt in the engine).
        /// </summary>
        private static string BuildEngineConnectionString(string server, string database, AuthMode authMode)
        {
            // Common suffix — keep the short connect-timeout so schema probes don't hang
            // the IntelliSense path when the server is unreachable.
            const string commonSuffix = "TrustServerCertificate=true;Encrypt=false;" +
                                        "Connect Timeout=5;Application Name=AKML SQL Engine";

            switch (authMode)
            {
                case AuthMode.AzureAdIntegrated:
                    // "Authentication=Active Directory Integrated" uses the signed-in
                    // Windows/AAD identity without prompting — the engine process inherits
                    // the same user token and negotiates AAD auth directly.
                    return $"Data Source={server};Initial Catalog={database};" +
                           "Authentication=Active Directory Integrated;" + commonSuffix;

                case AuthMode.Windows:
                case AuthMode.Unknown:
                    // Unknown defaults to Integrated Security (legacy behavior) — for
                    // non-AAD Windows domains this is correct. If this fires against an
                    // AAD-only server, Phase A will log a single login-failed warning and
                    // stop (see SchemaMetadataService).
                    return $"Data Source={server};Initial Catalog={database};" +
                           "Integrated Security=true;" + commonSuffix;

                case AuthMode.Unsupported:
                default:
                    return null;
            }
        }

        /// <summary>
        /// Reads the <c>AuthenticationType</c> (and related) properties that SSMS
        /// exposes on a DTE Document, and classifies them into one of the
        /// <see cref="AuthMode"/> values.
        ///
        /// SSMS's DTE property bag is not formally documented and values differ
        /// across SSMS 20/21/22 (sometimes a string like "Windows Authentication",
        /// sometimes a numeric enum), so we match heuristically.
        /// </summary>
        internal static (AuthMode Mode, string Raw) ReadAuthModeFromDocument(EnvDTE.Document doc)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (doc == null)
            {
                Log.Debug("ReadAuthModeFromDocument: null document → AuthMode.Unknown");
                return (AuthMode.Unknown, null);
            }

            string raw = null;
            try
            {
                // ProjectItem is where SSMS attaches SQL-document properties (Server,
                // Database, AuthenticationType, UserName). For plain .sql files opened
                // without an SSMS connection, ProjectItem is null — that's expected,
                // not a bug; we silently fall back to Unknown.
                var projItem = doc.ProjectItem;
                if (projItem == null)
                {
                    Log.Debug("ReadAuthModeFromDocument: doc '{Name}' has no ProjectItem (likely a non-SSMS document) → AuthMode.Unknown", doc.Name);
                    return (AuthMode.Unknown, null);
                }

                var props = projItem.Properties;
                if (props == null)
                {
                    Log.Debug("ReadAuthModeFromDocument: doc '{Name}' ProjectItem has no Properties → AuthMode.Unknown", doc.Name);
                    return (AuthMode.Unknown, null);
                }

                raw = TryReadProperty(props, "AuthenticationType");
                if (string.IsNullOrEmpty(raw))
                {
                    // Property missing — SSMS build may not expose it, or the window
                    // is disconnected. Log the raw so the next bug report can tell us
                    // which SSMS version / window state reproduces this.
                    Log.Debug("ReadAuthModeFromDocument: doc '{Name}' did not expose 'AuthenticationType' property → AuthMode.Unknown", doc.Name);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ReadAuthModeFromDocument: unable to read AuthenticationType property on doc '{Name}'", doc.Name);
            }

            var mode = ClassifyAuth(raw);
            Log.Debug("ReadAuthModeFromDocument: doc='{Name}' rawAuth='{Raw}' → {Mode}", doc.Name, raw ?? "(null)", mode);
            return (mode, raw);
        }

        private static string TryReadProperty(EnvDTE.Properties properties, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var prop = properties.Item(name);
                var value = prop?.Value?.ToString();
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Classifies a raw <c>AuthenticationType</c> string (or numeric enum) into
        /// <see cref="AuthMode"/>. Defensive substring matching — see class comment
        /// for why this isn't a clean string→enum lookup.
        ///
        /// Logs the classification decision at Debug so that the NEXT time SSMS
        /// ships a new auth-type string and the mapping is wrong, the log shows
        /// exactly which raw value fell through to which branch.
        /// </summary>
        internal static AuthMode ClassifyAuth(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                Log.Debug("ClassifyAuth: raw value is null/empty → AuthMode.Unknown");
                return AuthMode.Unknown;
            }

            // Numeric SqlAuthenticationMethod enum values that some SSMS builds return.
            // Mapping per Microsoft.Data.SqlClient.SqlAuthenticationMethod:
            //   0 NotSpecified, 1 SqlPassword, 2 ActiveDirectoryPassword,
            //   3 ActiveDirectoryIntegrated, 4 ActiveDirectoryInteractive,
            //   5 ActiveDirectoryServicePrincipal, 6 ActiveDirectoryDeviceCodeFlow,
            //   7 ActiveDirectoryManagedIdentity, 8 ActiveDirectoryMSI,
            //   9 ActiveDirectoryDefault, 10 ActiveDirectoryWorkloadIdentity.
            if (int.TryParse(raw, out var numeric))
            {
                var mapped = numeric switch
                {
                    0 => AuthMode.Windows,   // NotSpecified → SSMS treats as Windows
                    1 => AuthMode.SqlPassword, // SQL login — engine connects with a stored/entered credential
                    2 => AuthMode.Unsupported, // AAD Password
                    3 => AuthMode.AzureAdIntegrated,
                    4 => AuthMode.Unsupported, // AAD Interactive
                    5 => AuthMode.Unsupported, // AAD ServicePrincipal (needs client secret)
                    6 => AuthMode.Unsupported, // AAD DeviceCodeFlow
                    7 => AuthMode.AzureAdIntegrated, // ManagedIdentity (engine runs as same user)
                    8 => AuthMode.AzureAdIntegrated, // MSI alias
                    9 => AuthMode.AzureAdIntegrated, // Default — tries integrated first
                    10 => AuthMode.AzureAdIntegrated,
                    _ => AuthMode.Unknown
                };
                Log.Debug("ClassifyAuth: numeric raw='{Raw}' → {Mode}", raw, mapped);
                return mapped;
            }

            // String form. Match on stable substrings; strip case/whitespace.
            var lower = raw.Trim();

            if (lower.IndexOf("Active Directory", StringComparison.OrdinalIgnoreCase) >= 0
                || lower.IndexOf("Azure", StringComparison.OrdinalIgnoreCase) >= 0
                || lower.IndexOf("AAD", StringComparison.OrdinalIgnoreCase) >= 0
                || lower.IndexOf("Entra", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // AAD family — Integrated/Default are silent-reusable; the rest prompt.
                if (lower.IndexOf("Integrated", StringComparison.OrdinalIgnoreCase) >= 0
                    || lower.IndexOf("Default", StringComparison.OrdinalIgnoreCase) >= 0
                    || lower.IndexOf("Managed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Log.Debug("ClassifyAuth: AAD-family raw='{Raw}' matched Integrated/Default/Managed → AzureAdIntegrated", raw);
                    return AuthMode.AzureAdIntegrated;
                }
                Log.Debug("ClassifyAuth: AAD-family raw='{Raw}' did not match silent-reusable subset → Unsupported (engine would prompt)", raw);
                return AuthMode.Unsupported;
            }

            if (lower.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0
                || lower.IndexOf("Integrated", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Log.Debug("ClassifyAuth: string raw='{Raw}' → Windows", raw);
                return AuthMode.Windows;
            }

            if (lower.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // SQL Server auth — engine connects with a stored/entered credential (spec 029).
                Log.Debug("ClassifyAuth: string raw='{Raw}' matched 'SQL' → SqlPassword", raw);
                return AuthMode.SqlPassword;
            }

            Log.Debug("ClassifyAuth: string raw='{Raw}' matched no known pattern → Unknown (please add this value to ClassifyAuth if it recurs)", raw);
            return AuthMode.Unknown;
        }

        internal class ConnectionResult
        {
            public string Server { get; set; }
            public string Database { get; set; }

            /// <summary>The login parsed from the SSMS caption "(Login (SPID))", used to key the
            /// SQL credential store. Empty when not available. Spec 029.</summary>
            public string Login { get; set; }

            /// <summary>
            /// Connection string suitable for the engine process. <c>null</c> when the
            /// detected auth mode can't be silently reused (SQL auth, AAD Interactive, …).
            /// </summary>
            public string ConnectionString { get; set; }

            public AuthMode AuthMode { get; set; } = AuthMode.Unknown;

            /// <summary>
            /// <c>true</c> when <see cref="ConnectionString"/> is non-null AND is expected
            /// to succeed without user interaction. Callers should skip engine-side
            /// schema auto-load when this is <c>false</c>.
            /// </summary>
            public bool IsEngineUsable { get; set; } = true;
        }
    }
}
