using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace AkmlSql.Shell.Shared.Analysis
{
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
    /// Inserts <c>-- noqa: RULEID</c> at the end of the violation line.
    /// </summary>
    internal sealed class SuppressLineFixAction(ITextBuffer buffer, int line, string ruleId) : ISuggestedAction
    {
        // 1-based

        public string DisplayText         => $"Suppress {ruleId} for this line";
        public bool   HasActionSets       => false;
        public bool   HasPreview          => false;
        public string IconAutomationText  => null;
        // Advisory action (no code transform) → neutral/blue info lightbulb (FR-027).
        public ImageMoniker IconMoniker   => KnownMonikers.StatusInformation;
        public string InputGestureText    => null;

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
            var snapshot = buffer.CurrentSnapshot;
            // ScriptDom lines are 1-based; ITextSnapshot lines are 0-based
            int lineIndex = line - 1;
            if (lineIndex < 0 || lineIndex >= snapshot.LineCount) return;

            var snapshotLine = snapshot.GetLineFromLineNumber(lineIndex);
            var lineEnd      = snapshotLine.End.Position;

            using (var edit = buffer.CreateEdit())
            {
                edit.Insert(lineEnd, $" -- noqa: {ruleId}");
                edit.Apply();
            }
        }

        public bool TryGetTelemetryId(out Guid telemetryId) { telemetryId = Guid.Empty; return false; }
    }

    /// <summary>
    /// Disables a rule globally by writing <c>enabled: false</c> to the user's global settings
    /// and notifying the engine to invalidate its settings cache.
    /// </summary>
    internal sealed class DisableRuleGloballyFixAction(string ruleId) : ISuggestedAction
    {
        public string DisplayText         => $"Disable rule {ruleId} globally";
        public bool   HasActionSets       => false;
        public bool   HasPreview          => false;
        public string IconAutomationText  => null;
        // Advisory action (no code transform) → neutral/blue info lightbulb (FR-027).
        public ImageMoniker IconMoniker   => KnownMonikers.StatusInformation;
        public string InputGestureText    => null;

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
            }

            // Notify engine to invalidate settings cache
            var client = EngineLifecycle.Manager?.Client;
            if (client != null && client.IsConnected)
            {
                _ = client.SendNotificationAsync(
                    Core.Ipc.MessageTypes.AnalysisSettingsChanged,
                    new { },
                    cancellationToken);
            }
        }

        public bool TryGetTelemetryId(out Guid telemetryId) { telemetryId = Guid.Empty; return false; }
    }
}
