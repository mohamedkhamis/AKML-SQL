using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
            // Write a user-level .casettings file that disables this rule
            // (T064/T066 will wire this into the full ConfigManager/SettingsDialog flow)
            try
            {
                var userDir  = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AKML SQL");
                System.IO.Directory.CreateDirectory(userDir);
                var settingsPath = System.IO.Path.Combine(userDir, ".casettings");

                // Load existing file or start fresh
                var json = System.IO.File.Exists(settingsPath)
                    ? System.IO.File.ReadAllText(settingsPath)
                    : "{}";
                var jobj = System.Text.Json.JsonDocument.Parse(json).RootElement;
                // Minimal write: merge disabled rule into rules section
                var merged = new Dictionary<string, object>();
                if (jobj.TryGetProperty("rules", out var rulesEl))
                {
                    foreach (var prop in rulesEl.EnumerateObject())
                        merged[prop.Name] = prop.Value;
                }

                // Preserve any existing severity override; only flip enabled to false
                string existingSeverity = null;
                if (rulesEl.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    rulesEl.TryGetProperty(ruleId, out var existing) &&
                    existing.TryGetProperty("severity", out var sev))
                {
                    existingSeverity = sev.GetString();
                }

                merged[ruleId] = existingSeverity != null
                    ? (object)new { enabled = false, severity = existingSeverity }
                    : new { enabled = false };

                var updated = System.Text.Json.JsonSerializer.Serialize(
                    new { rules = merged },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(settingsPath, updated);
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
