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
    /// This is the <b>Phase 2 foundational skeleton</b>: it has the registry
    /// API and a no-op fallback when a context key is unknown. The actual
    /// VS-host F1 wiring (subscribing to <c>IVsHelpSystem</c> and inspecting
    /// the focused element's <c>HelpContextValues</c>) lands in the per-host
    /// user-story phases that introduce each new dialog / tool window.
    /// </para>
    /// <para>
    /// Each user-story phase that adds a new UI surface MUST register its
    /// help context key with this listener at construction time, e.g.
    /// <code>
    /// F1HelpListener.Default.Register("akmlsql.dialog.smartrename",
    ///     "https://github.com/mohamedkhamis/AKML-SQL/blob/master/doc/smart-rename.md");
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
        /// Open the documentation page for the given context key in the system
        /// browser. No-ops when the key is unknown so that pressing F1 on a
        /// not-yet-registered surface fails closed instead of crashing.
        /// </summary>
        public bool Open(string contextKey)
        {
            var url = TryResolve(contextKey);
            if (string.IsNullOrEmpty(url))
            {
                Log.Debug("F1HelpListener: no help URL registered for context '{Context}'", contextKey);
                return false;
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
