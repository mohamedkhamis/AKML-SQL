using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Serilog;

namespace AkmlSql.Shell.Shared.Help
{
    /// <summary>
    /// Spec 014 / FR-104 — central registry that maps an AKML SQL UI surface
    /// (Options page, dialog, tool window) to a documentation URL, plus a single
    /// <see cref="Open(string)"/> entry point that any host-specific F1 handler
    /// can call to launch the matching documentation in the system browser.
    /// <para>
    /// Pages open on the product docs site (<see cref="F1HelpRegistrations.DocBase"/>).
    /// Unknown keys are a logged no-op.
    /// </para>
    /// <para>
    /// A surface either registers a context key in <see cref="F1HelpRegistrations"/>, or
    /// passes a docs topic straight to <see cref="Open(string)"/> (spec 040: the Options pages,
    /// the Format Styles window and SQL History do, through <see cref="HelpBinding"/>):
    /// <code>
    /// F1HelpListener.Default.Open("topics/sql-history");
    /// </code>
    /// </para>
    /// </summary>
    internal sealed class F1HelpListener
    {
        // ── Singleton ──────────────────────────────────────────────────────────

        /// <summary>The single shared listener instance for the running shell process.</summary>
        public static F1HelpListener Default { get; } = CreateDefault();

        // Auto-initialise Phase 10 (spec 019) registrations on first access so the
        // central registrations file does not depend on each host package
        // remembering to call into it. The local `instance` variable is passed
        // explicitly to RegisterAll — reading Default here would observe the
        // still-null backing field since this method runs inside Default's own
        // type initializer (see F1HelpRegistrations.RegisterAll docstring).
        private static F1HelpListener CreateDefault()
        {
            var instance = new F1HelpListener();
            try
            {
                F1HelpRegistrations.RegisterAll(instance);
            }
            catch (Exception ex)
            {
                // Registrations must not poison the singleton. Log and continue —
                // the registry stays empty for any surface whose registration
                // threw, but the rest of the API remains usable.
                Log.Warning(ex, "F1HelpListener: bulk registration via F1HelpRegistrations failed");
            }
            return instance;
        }

        private F1HelpListener() { }

        // ── Registry ───────────────────────────────────────────────────────────

        private readonly ConcurrentDictionary<string, string> _contextMap =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register (or update) the documentation URL for a help context key.
        /// Idempotent — calling twice for the same key replaces the existing URL.
        /// </summary>
        /// <param name="contextKey">
        /// Stable string identifying a UI surface, e.g. <c>"akmlsql.dialog.safety"</c>,
        /// <c>"akmlsql.window.invalid-objects"</c>, <c>"akmlsql.options.completion"</c>.
        /// </param>
        /// <param name="url">Absolute https URL to the matching documentation page.</param>
        public void Register(string contextKey, string url)
        {
            if (string.IsNullOrWhiteSpace(contextKey))
            {
                return;
            }
            _contextMap[contextKey] = url ?? string.Empty;
        }

        /// <summary>
        /// Returns the registered URL for the given context key, or <c>null</c>
        /// when no entry exists.
        /// </summary>
        public string? TryResolve(string contextKey)
        {
            if (string.IsNullOrWhiteSpace(contextKey))
            {
                return null;
            }
            return _contextMap.TryGetValue(contextKey, out var url) ? url : null;
        }

        /// <summary>The number of registered context keys (used by tests).</summary>
        public int Count => _contextMap.Count;

        // ── Open ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Spec 040 (T183) test hook: when set, <see cref="Open(string)"/> hands the key it was
        /// given to this delegate instead of launching the browser. Tests set it and clear it in a
        /// <c>finally</c>; production code never sets it.
        /// </summary>
        internal static Action<string>? OpenOverride { get; set; }

        /// <summary>
        /// Open the documentation page for the given key in the system browser. The key is
        /// either a registered context key (<c>"akmlsql.dialog.smart-rename"</c>) or a docs
        /// topic — a site slug with an optional anchor (<c>"topics/options#general"</c>), which
        /// resolves against <see cref="F1HelpRegistrations.DocBase"/>. No-ops when the key is
        /// unknown so that pressing F1 on a not-yet-registered surface fails closed instead of
        /// crashing.
        /// </summary>
        public bool Open(string contextKey)
        {
            var url = TryResolve(contextKey) ?? F1HelpRegistrations.TopicUrl(contextKey);
            if (string.IsNullOrEmpty(url))
            {
                Log.Debug("F1HelpListener: no help URL registered for context '{Context}'", contextKey);
                return false;
            }

            var hook = OpenOverride;
            if (hook != null)
            {
                hook(contextKey);
                return true;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "F1HelpListener: failed to open help URL for context '{Context}'", contextKey);
                return false;
            }
        }
    }
}
