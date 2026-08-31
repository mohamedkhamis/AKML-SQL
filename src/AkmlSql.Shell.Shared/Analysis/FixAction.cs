using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Shared helpers for the "stop reporting this rule" actions. The four scopes are offered
    /// together, narrowest first, on both surfaces that expose them — the lightbulb
    /// (<see cref="LightbulbSource"/>) and the warning-glyph menu (<see cref="WarningGlyphMenu"/>) —
    /// which build them from these same classes so the two can never drift.
    /// </summary>
    internal static class SuppressionActions
    {
        /// <summary>The inline directive appended to a single line.</summary>
        internal static string LineDirective(string ruleId) => $" -- akml-disable-line {ruleId}";

        /// <summary>The inline directive placed at the top of a script (no matching enable = whole file).</summary>
        internal static string ScriptDirective(string ruleId) => $"-- akml-disable {ruleId}";

        /// <summary>
        /// Builds every suppression action for one issue, narrowest scope first.
        /// </summary>
        internal static List<ISuggestedAction> ForIssue(ITextBuffer buffer, CodeIssueInfo issue)
        {
            return new List<ISuggestedAction>
            {
                new SuppressLineFixAction(buffer, issue.Line, issue.RuleId),
                new SuppressScriptFixAction(buffer, issue.RuleId),
                new DisableRuleForSessionFixAction(buffer, issue.RuleId),
                new DisableRuleGloballyFixAction(buffer, issue.RuleId),
            };
        }

        /// <summary>
        /// Re-runs analysis for the buffer. Disabling a rule for the session or globally edits no
        /// text, so nothing else would re-trigger a pass and the squiggles would sit there looking
        /// as though the command had done nothing until the next keystroke.
        /// </summary>
        internal static void Reanalyze(ITextBuffer buffer)
        {
            if (buffer == null) return;
            if (buffer.Properties.TryGetProperty(typeof(AnalysisController), out AnalysisController controller))
                controller.TriggerReanalysis();
        }
    }

    /// <summary>
    /// Applies a transform/insert/remove fix from a <see cref="FixActionInfo"/> to an <see cref="ITextBuffer"/>.
    /// </summary>
    /// <remarks>
    /// Spec 030 T054 (FR-027): <paramref name="autoFixable"/> comes from the issue's
    /// <see cref="CodeIssueInfo.AutoFixable"/> flag (engine RuleMetadataCatalog). It only drives the
    /// lightbulb icon colour — an auto-fixable rule shows the orange quick-fix lightbulb
    /// (<see cref="KnownMonikers.IntellisenseLightBulb"/>); an advisory rule shows the neutral/blue
    /// info icon (<see cref="KnownMonikers.StatusInformation"/>).
    /// </remarks>
    internal sealed class FixAction(ITextBuffer buffer, FixActionInfo fix, string ruleId, bool autoFixable = false) : ISuggestedAction
    {
        private readonly ITextBuffer   _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        private readonly FixActionInfo _fix = fix    ?? throw new ArgumentNullException(nameof(fix));
        private readonly string        _ruleId = ruleId ?? string.Empty;
        private readonly bool          _autoFixable = autoFixable;

        public string  DisplayText      => _fix.Label;
        public bool    HasActionSets    => false;
        public bool    HasPreview       => false;
        public string  IconAutomationText => null;
        public ImageMoniker IconMoniker =>
            _autoFixable ? KnownMonikers.IntellisenseLightBulb : KnownMonikers.StatusInformation;
        public string  InputGestureText  => null;

        public void Dispose() { }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<SuggestedActionSet>>(null);
        }

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<object>(null);
        }

        public void Invoke(CancellationToken cancellationToken)
        {
            var snapshot = _buffer.CurrentSnapshot;
            var start    = Math.Min(_fix.ReplacementStart, snapshot.Length);
            var end      = Math.Min(_fix.ReplacementEnd,   snapshot.Length);

            using (var edit = _buffer.CreateEdit())
            {
                edit.Replace(start, end - start, _fix.ReplacementText ?? string.Empty);
                edit.Apply();
            }
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }
    }

    /// <summary>
    /// Common chrome for the suppression actions: no preview, no nested action sets, and the
    /// neutral/blue info icon (FR-027) because none of them transforms code.
    /// </summary>
    internal abstract class SuppressionActionBase : ISuggestedAction
    {
        public abstract string DisplayText { get; }
        public bool   HasActionSets      => false;
        public bool   HasPreview         => false;
        public string IconAutomationText => null;
        public ImageMoniker IconMoniker  => KnownMonikers.StatusInformation;
        public string InputGestureText   => null;

        public void Dispose() { }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<SuggestedActionSet>>(null);

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
            => Task.FromResult<object>(null);

        public abstract void Invoke(CancellationToken cancellationToken);

        public bool TryGetTelemetryId(out Guid telemetryId) { telemetryId = Guid.Empty; return false; }
    }

    /// <summary>
    /// Narrowest scope: appends <c>-- akml-disable-line RULE</c> to the offending line.
    /// </summary>
    internal sealed class SuppressLineFixAction(ITextBuffer buffer, int line, string ruleId) : SuppressionActionBase
    {
        // line is 1-based (ScriptDom convention).

        public override string DisplayText => $"Suppress {ruleId} on this line";

        public override void Invoke(CancellationToken cancellationToken)
        {
            var snapshot = buffer.CurrentSnapshot;
            // ScriptDom lines are 1-based; ITextSnapshot lines are 0-based
            int lineIndex = line - 1;
            if (lineIndex < 0 || lineIndex >= snapshot.LineCount) return;

            var snapshotLine = snapshot.GetLineFromLineNumber(lineIndex);
            var lineEnd      = snapshotLine.End.Position;

            using (var edit = buffer.CreateEdit())
            {
                edit.Insert(lineEnd, SuppressionActions.LineDirective(ruleId));
                edit.Apply();
            }
        }
    }

    /// <summary>
    /// Whole-script scope: inserts <c>-- akml-disable RULE</c> as the first line of the document.
    /// With no matching <c>-- akml-enable</c> the directive runs to end of file, so one comment
    /// covers the script — and, because it lives in the file, it travels with it to source control
    /// and to whoever opens it next.
    /// </summary>
    internal sealed class SuppressScriptFixAction(ITextBuffer buffer, string ruleId) : SuppressionActionBase
    {
        public override string DisplayText => $"Disable {ruleId} in this script";

        public override void Invoke(CancellationToken cancellationToken)
        {
            var snapshot = buffer.CurrentSnapshot;
            var directive = SuppressionActions.ScriptDirective(ruleId);

            // Already disabled at the top of the script — inserting a second identical directive
            // would be harmless but untidy, so do nothing. Only the opening lines are scanned: that
            // is where this action puts it, and reading the whole snapshot to be thorough would
            // allocate a copy of a document that may be megabytes, on the UI thread, to answer a
            // question that cannot even arise while the rule is still reporting.
            var probeLines = Math.Min(snapshot.LineCount, 20);
            for (var i = 0; i < probeLines; i++)
            {
                if (snapshot.GetLineFromLineNumber(i).GetText()
                        .IndexOf(directive, StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
            }

            // Match the document's own newline so the inserted line does not introduce mixed
            // endings in a file that uses bare LF.
            var newLine = snapshot.LineCount > 0
                ? snapshot.GetLineFromLineNumber(0).GetLineBreakText()
                : null;
            if (string.IsNullOrEmpty(newLine)) newLine = Environment.NewLine;

            using (var edit = buffer.CreateEdit())
            {
                edit.Insert(0, directive + newLine);
                edit.Apply();
            }
        }
    }

    /// <summary>
    /// Session scope: asks the engine to stop reporting the rule until the IDE is closed. Writes
    /// nothing — not to the script, not to config.json — which is what makes it the right choice
    /// for "not now" as opposed to "not ever".
    /// </summary>
    /// <remarks>
    /// Lifted from the Manage Code Analysis Rules dialog, which lists the session-disabled rules
    /// and can clear them; the suppression also ends on its own when the engine process does.
    /// </remarks>
    internal sealed class DisableRuleForSessionFixAction(ITextBuffer buffer, string ruleId) : SuppressionActionBase
    {
        public override string DisplayText => $"Disable {ruleId} for this session";

        public override void Invoke(CancellationToken cancellationToken)
        {
            var client = EngineLifecycle.Manager?.Client;
            if (client == null || !client.IsConnected)
            {
                Serilog.Log.Warning(
                    "DisableRuleForSessionFixAction: engine not connected — {Rule} not suppressed", ruleId);
                return;
            }

            try
            {
                // Not awaited: blocking the UI thread on the RPC would risk exactly the deadlock the
                // async handlers exist to avoid. Ordering still holds — the pipe is FIFO and the
                // reanalysis below is debounced 300 ms, so the engine has the suppression before it
                // sees the analyze request. The task is observed only to log a failure; without a
                // continuation a faulted RPC would vanish silently.
                var pending = client.SendRequestAsync<SessionSuppressionResponse, SessionSuppressionRequest>(
                    MessageTypes.SessionSuppression,
                    new SessionSuppressionRequest
                    {
                        RuleId = ruleId,
                        Action = SessionSuppressionActions.Add,
                    },
                    timeoutMs: 5_000,
                    ct: cancellationToken);

                _ = pending.ContinueWith(
                    t =>
                    {
                        if (t.IsFaulted)
                            Serilog.Log.Warning(t.Exception,
                                "DisableRuleForSessionFixAction: engine rejected suppressing {Rule}", ruleId);
                        else if (t.Status == TaskStatus.RanToCompletion && t.Result?.Success != true)
                            Serilog.Log.Warning(
                                "DisableRuleForSessionFixAction: engine reported failure suppressing {Rule}: {Error}",
                                ruleId, t.Result?.Error);
                        else
                            Serilog.Log.Information(
                                "DisableRuleForSessionFixAction: {Rule} disabled for this session", ruleId);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "DisableRuleForSessionFixAction: failed to suppress {Rule}", ruleId);
                return;
            }

            SuppressionActions.Reanalyze(buffer);
        }
    }

    /// <summary>
    /// Widest scope: disables a rule for every file, in every session, by writing
    /// <c>enabled: false</c> to the user's global settings and notifying the engine to invalidate
    /// its settings cache. Reversible from the Manage Code Analysis Rules dialog.
    /// </summary>
    internal sealed class DisableRuleGloballyFixAction(ITextBuffer buffer, string ruleId) : SuppressionActionBase
    {
        public override string DisplayText => $"Disable {ruleId} everywhere";

        public override void Invoke(CancellationToken cancellationToken)
        {
            // Persist through config.json ruleOverrides — the mechanism the engine actually
            // reads and ManageRulesCommand already uses. The previous implementation wrote a
            // user-level %AppData%\AKML SQL\.casettings, which CaSettingsLoader never loads
            // (it only searches upward from the DOCUMENT directory), so "Disable rule"
            // silently did nothing across restarts.
            try
            {
                var settings = ConfigManager.Load();
                var overrides = settings.CodeAnalysis.RuleOverrides;

                // Preserve any existing severity override; only flip the enable flag.
                if (overrides.TryGetValue(ruleId, out var existing))
                {
                    existing.Enabled = false;
                }
                else
                {
                    overrides[ruleId] = new Core.Config.RuleOverride { Enabled = false };
                }

                ConfigManager.Save(settings);
                Serilog.Log.Information("DisableRuleGloballyFixAction: disabled {Rule} via config.json ruleOverrides", ruleId);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "DisableRuleGloballyFixAction: failed to persist {Rule}", ruleId);
                return;
            }

            // Notify engine to invalidate settings cache
            var client = EngineLifecycle.Manager?.Client;
            if (client != null && client.IsConnected)
            {
                _ = client.SendNotificationAsync(
                    MessageTypes.AnalysisSettingsChanged,
                    new { },
                    cancellationToken);
            }

            SuppressionActions.Reanalyze(buffer);
        }
    }
}
