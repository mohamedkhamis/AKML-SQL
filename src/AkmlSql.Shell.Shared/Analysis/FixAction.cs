using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Applies a transform/insert/remove fix from a <see cref="FixActionInfo"/> to an <see cref="ITextBuffer"/>.
    /// </summary>
    internal sealed class FixAction : ISuggestedAction
    {
        private readonly ITextBuffer   _buffer;
        private readonly FixActionInfo _fix;
        private readonly string        _ruleId;

        public FixAction(ITextBuffer buffer, FixActionInfo fix, string ruleId)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _fix    = fix    ?? throw new ArgumentNullException(nameof(fix));
            _ruleId = ruleId ?? string.Empty;
        }

        public string  DisplayText      => _fix.Label;
        public bool    HasActionSets    => false;
        public bool    HasPreview       => false;
        public string  IconAutomationText => null;
        public ImageMoniker IconMoniker => default;
        public string  InputGestureText  => null;

        public void Dispose() { }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<SuggestedActionSet>>(null);

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
            => Task.FromResult<object>(null);

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
    internal sealed class SuppressLineFixAction : ISuggestedAction
    {
        private readonly ITextBuffer _buffer;
        private readonly int         _line;    // 1-based
        private readonly string      _ruleId;

        public SuppressLineFixAction(ITextBuffer buffer, int line, string ruleId)
        {
            _buffer = buffer;
            _line   = line;
            _ruleId = ruleId;
        }

        public string DisplayText         => $"Suppress {_ruleId} for this line";
        public bool   HasActionSets       => false;
        public bool   HasPreview          => false;
        public string IconAutomationText  => null;
        public ImageMoniker IconMoniker   => default;
        public string InputGestureText    => null;

        public void Dispose() { }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<SuggestedActionSet>>(null);

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
            => Task.FromResult<object>(null);

        public void Invoke(CancellationToken cancellationToken)
        {
            var snapshot = _buffer.CurrentSnapshot;
            // ScriptDom lines are 1-based; ITextSnapshot lines are 0-based
            int lineIndex = _line - 1;
            if (lineIndex < 0 || lineIndex >= snapshot.LineCount) return;

            var snapshotLine = snapshot.GetLineFromLineNumber(lineIndex);
            var lineEnd      = snapshotLine.End.Position;

            using (var edit = _buffer.CreateEdit())
            {
                edit.Insert(lineEnd, $" -- noqa: {_ruleId}");
                edit.Apply();
            }
        }

        public bool TryGetTelemetryId(out Guid telemetryId) { telemetryId = Guid.Empty; return false; }
    }

    /// <summary>
    /// Disables a rule globally by writing <c>enabled: false</c> to the user's global settings
    /// and notifying the engine to invalidate its settings cache.
    /// </summary>
    internal sealed class DisableRuleGloballyFixAction : ISuggestedAction
    {
        private readonly string _ruleId;

        public DisableRuleGloballyFixAction(string ruleId)
        {
            _ruleId = ruleId;
        }

        public string DisplayText         => $"Disable rule {_ruleId} globally";
        public bool   HasActionSets       => false;
        public bool   HasPreview          => false;
        public string IconAutomationText  => null;
        public ImageMoniker IconMoniker   => default;
        public string InputGestureText    => null;

        public void Dispose() { }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<SuggestedActionSet>>(null);

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
            => Task.FromResult<object>(null);

        public void Invoke(CancellationToken cancellationToken)
        {
            // Write a user-level .casettings file that disables this rule
            // (T064/T066 will wire this into the full ConfigManager/SettingsDialog flow)
            try
            {
                var userDir  = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "AKML SQL");
                System.IO.Directory.CreateDirectory(userDir);
                var settingsPath = System.IO.Path.Combine(userDir, ".casettings");

                // Load existing file or start fresh
                var json = System.IO.File.Exists(settingsPath)
                    ? System.IO.File.ReadAllText(settingsPath)
                    : "{}";
                var jobj = System.Text.Json.JsonDocument.Parse(json).RootElement;
                // Minimal write: merge disabled rule into rules section
                var merged = new System.Collections.Generic.Dictionary<string, object>();
                if (jobj.TryGetProperty("rules", out var rulesEl))
                {
                    foreach (var prop in rulesEl.EnumerateObject())
                        merged[prop.Name] = prop.Value;
                }

                // Preserve any existing severity override; only flip enabled to false
                string existingSeverity = null;
                if (rulesEl.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    rulesEl.TryGetProperty(_ruleId, out var existing) &&
                    existing.TryGetProperty("severity", out var sev))
                {
                    existingSeverity = sev.GetString();
                }

                merged[_ruleId] = existingSeverity != null
                    ? (object)new { enabled = false, severity = existingSeverity }
                    : new { enabled = false };

                var updated = System.Text.Json.JsonSerializer.Serialize(
                    new { rules = merged },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(settingsPath, updated);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "DisableRuleGloballyFixAction: failed to persist {Rule}", _ruleId);
            }

            // Notify engine to invalidate settings cache
            var client = EngineLifecycle.Manager?.Client;
            if (client != null && client.IsConnected)
            {
                _ = client.SendNotificationAsync(
                    AkmlSql.Core.Ipc.MessageTypes.AnalysisSettingsChanged,
                    new { },
                    cancellationToken);
            }
        }

        public bool TryGetTelemetryId(out Guid telemetryId) { telemetryId = Guid.Empty; return false; }
    }
}
