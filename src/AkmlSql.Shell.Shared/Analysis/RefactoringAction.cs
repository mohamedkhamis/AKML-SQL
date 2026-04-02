#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Serilog;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// A suggested action that dispatches a FormatAction IPC request to the engine.
    /// Used by <see cref="LightbulbSource"/> to offer refactoring operations
    /// (Expand Wildcards, Qualify Names, Surround with, etc.) alongside code analysis fixes.
    /// </summary>
    internal sealed class RefactoringAction : ISuggestedAction
    {
        private readonly ITextBuffer _buffer;
        private readonly string _displayText;
        private readonly FormatActionType _actionType;
        private readonly string _sessionId;

        public RefactoringAction(ITextBuffer buffer, string displayText, FormatActionType actionType)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _displayText = displayText;
            _actionType = actionType;

            _sessionId = buffer.Properties.TryGetProperty("AkmlSqlSessionId", out string id)
                ? id : string.Empty;
        }

        public string DisplayText => _displayText;
        public bool HasActionSets => false;
        public bool HasPreview => false;
        public string IconAutomationText => null;
        public ImageMoniker IconMoniker => default;
        public string InputGestureText => null;

        public void Dispose() { }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken ct)
            => Task.FromResult<IEnumerable<SuggestedActionSet>>(null);

        public Task<object> GetPreviewAsync(CancellationToken ct)
            => Task.FromResult<object>(null);

        public void Invoke(CancellationToken cancellationToken)
        {
            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    Log.Warning("RefactoringAction: engine not available for {Action}", _actionType);
                    return;
                }

                var snapshot = _buffer.CurrentSnapshot;
                var request = new FormatActionRequest
                {
                    SessionId = _sessionId,
                    Text = snapshot.GetText(),
                    ActionType = (int)_actionType
                };

                Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    var response = await client.SendRequestAsync<FormatActionResponse, FormatActionRequest>(
                        MessageTypes.FormatAction, request, timeoutMs: 10_000);

                    if (response != null && !string.IsNullOrEmpty(response.FormattedText))
                    {
                        await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        // Apply via ITextBuffer.CreateEdit (not FormatActionHelper which needs IVsTextLines)
                        var currentSnapshot = _buffer.CurrentSnapshot;
                        using (var edit = _buffer.CreateEdit())
                        {
                            edit.Replace(0, currentSnapshot.Length, response.FormattedText);
                            edit.Apply();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "RefactoringAction: failed to apply {Action}", _actionType);
            }
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }
    }

    /// <summary>
    /// A suggested action that toggles line comments on the current selection.
    /// Does not require engine IPC — operates directly on the text buffer.
    /// </summary>
    internal sealed class CommentToggleAction : ISuggestedAction
    {
        private readonly ITextBuffer _buffer;

        public CommentToggleAction(ITextBuffer buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        public string DisplayText => "Comment / Uncomment Selection";
        public bool HasActionSets => false;
        public bool HasPreview => false;
        public string IconAutomationText => null;
        public ImageMoniker IconMoniker => default;
        public string InputGestureText => "Ctrl+K, Ctrl+C";

        public void Dispose() { }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken ct)
            => Task.FromResult<IEnumerable<SuggestedActionSet>>(null);

        public Task<object> GetPreviewAsync(CancellationToken ct)
            => Task.FromResult<object>(null);

        public void Invoke(CancellationToken cancellationToken)
        {
            try
            {
                // Toggle comment via DTE (works across all SSMS/VS versions)
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                dte?.ExecuteCommand("Edit.ToggleLineComment");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CommentToggleAction: failed");
            }
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }
    }
}
