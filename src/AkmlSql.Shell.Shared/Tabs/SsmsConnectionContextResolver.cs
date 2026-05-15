using System;
using AkmlSql.Shell.Shared.Editor;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Phase 10 (spec 019) / US14 FR-076 — shared connection-context resolver
    /// consolidating two BUG-A8 / BUG-A10 TODOs flagged by the 2026-05-05
    /// codebase audit. Both <c>TabTooltipProvider</c> and
    /// <c>TabColoringManager</c> previously held independent placeholder
    /// comments for "SSMS-specific connection context retrieval" — that work
    /// now lives here as a thin wrapper over the existing
    /// <see cref="SsmsConnectionDetector"/> that emits a stable
    /// <see cref="ConnectionContext"/> shape so the two callers don't need to
    /// reach into <c>SsmsConnectionDetector</c>'s internal
    /// <c>ConnectionResult</c>.
    /// <para>
    /// Strategy: delegate to <see cref="SsmsConnectionDetector.TryDetectConnection(IServiceProvider, IWpfTextView)"/>.
    /// The detector itself handles caption parsing, per-text-view file-path
    /// resolution, and auth-mode classification — we just project its result
    /// into the public-shape class exposed here.
    /// </para>
    /// <para>
    /// Returns <see cref="ConnectionContext.Unknown"/> when the detector cannot
    /// resolve. Callers treat that as "no tooltip enrichment, no environment
    /// color override". The resolver never throws.
    /// </para>
    /// </summary>
    internal static class SsmsConnectionContextResolver
    {
        /// <summary>
        /// Resolve the connection context for a given text view in an SSMS or
        /// VS host. Both parameters can be null — null inputs return
        /// <see cref="ConnectionContext.Unknown"/> rather than throwing.
        /// <para>
        /// MUST be called on the UI thread. The underlying
        /// <c>SsmsConnectionDetector.TryDetectConnection</c> asserts
        /// <c>ThreadHelper.ThrowIfNotOnUIThread()</c> — off-thread callers will
        /// observe the throw caught by this method's try/catch and silently
        /// receive <see cref="ConnectionContext.Unknown"/> with no warning
        /// surfaced beyond a Debug log line. Both
        /// <c>TabTooltipProvider</c> and <c>TabColoringManager</c> already run
        /// on the UI thread; new callers must do the same.
        /// </para>
        /// </summary>
        public static ConnectionContext Resolve(IServiceProvider serviceProvider, IWpfTextView textView)
        {
            if (serviceProvider == null)
            {
                return ConnectionContext.Unknown;
            }

            try
            {
                var result = textView != null
                    ? SsmsConnectionDetector.TryDetectConnection(serviceProvider, textView)
                    : SsmsConnectionDetector.TryDetectConnection(serviceProvider);

                if (result == null || string.IsNullOrEmpty(result.Server))
                {
                    return ConnectionContext.Unknown;
                }

                return new ConnectionContext
                {
                    Server = result.Server ?? string.Empty,
                    Database = result.Database ?? string.Empty,
                    AuthMode = result.AuthMode.ToString(),
                    IsEngineUsable = result.IsEngineUsable,
                };
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SsmsConnectionContextResolver: detector threw; returning Unknown");
                return ConnectionContext.Unknown;
            }
        }
    }

    /// <summary>
    /// Public-shape connection context returned by
    /// <see cref="SsmsConnectionContextResolver.Resolve(IServiceProvider, IWpfTextView)"/>.
    /// Strings are non-null but may be empty when the field could not be
    /// extracted. <see cref="AuthMode"/> is the string form of
    /// <c>SsmsConnectionDetector.AuthMode</c> (e.g., "Windows",
    /// "AzureAdIntegrated", "Unsupported", "Unknown").
    /// </summary>
    internal sealed class ConnectionContext : IEquatable<ConnectionContext>
    {
        public string Server { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string AuthMode { get; set; } = string.Empty;
        public bool IsEngineUsable { get; set; }

        /// <summary>The shared empty instance returned when the detector could not resolve.</summary>
        public static ConnectionContext Unknown { get; } = new ConnectionContext
        {
            Server = string.Empty,
            Database = string.Empty,
            AuthMode = "Unknown",
            IsEngineUsable = false,
        };

        public bool Equals(ConnectionContext other)
        {
            if (other == null) return false;
            return string.Equals(Server, other.Server, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Database, other.Database, StringComparison.OrdinalIgnoreCase)
                && string.Equals(AuthMode, other.AuthMode, StringComparison.OrdinalIgnoreCase)
                && IsEngineUsable == other.IsEngineUsable;
        }

        public override bool Equals(object obj) => Equals(obj as ConnectionContext);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (Server == null ? 0 : Server.ToLowerInvariant().GetHashCode());
                hash = hash * 31 + (Database == null ? 0 : Database.ToLowerInvariant().GetHashCode());
                hash = hash * 31 + (AuthMode == null ? 0 : AuthMode.ToLowerInvariant().GetHashCode());
                hash = hash * 31 + IsEngineUsable.GetHashCode();
                return hash;
            }
        }
    }
}
