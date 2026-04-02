#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Text;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.DocumentOutline
{
    /// <summary>
    /// ViewModel for the Document Outline tool window.
    /// Listens for text buffer changes (debounced 300ms), sends DocumentOutlineRequest
    /// to the engine, and populates the RootNodes collection.
    /// </summary>
    public sealed class DocumentOutlineViewModel : INotifyPropertyChanged, IDisposable
    {
        private ITextBuffer? _buffer;
        private CancellationTokenSource? _debounce;
        private volatile bool _disposed;
        private int _version;
        private OutlineNodeDto? _selectedNode;
        private string _sessionId = string.Empty;

        /// <summary>Top-level outline nodes displayed in the TreeView.</summary>
        public ObservableCollection<OutlineNodeDto> RootNodes { get; } = new();

        /// <summary>Currently selected node in the tree.</summary>
        public OutlineNodeDto? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode != value)
                {
                    _selectedNode = value;
                    OnPropertyChanged();
                    NodeSelected?.Invoke(this, value);
                }
            }
        }

        /// <summary>
        /// Raised when a node is selected. The subscriber (tool window control)
        /// should scroll the editor to the node's StartLine.
        /// </summary>
        public event EventHandler<OutlineNodeDto?>? NodeSelected;
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Attaches this ViewModel to the given text buffer and begins listening for changes.
        /// </summary>
        public void AttachToBuffer(ITextBuffer buffer, string sessionId)
        {
            DetachFromBuffer();
            _buffer = buffer;
            _sessionId = sessionId;
            _buffer.Changed += OnBufferChanged;

            // Trigger initial outline
            RequestOutlineUpdate();
        }

        /// <summary>
        /// Detaches from the current text buffer and stops listening for changes.
        /// </summary>
        public void DetachFromBuffer()
        {
            if (_buffer != null)
            {
                _buffer.Changed -= OnBufferChanged;
                _buffer = null;
            }
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (_disposed) return;
            RequestOutlineUpdate();
        }

        private void RequestOutlineUpdate()
        {
            Interlocked.Increment(ref _version);
            var prev = _debounce;
            _debounce = new CancellationTokenSource();
            try { prev?.Cancel(); prev?.Dispose(); } catch (ObjectDisposedException) { }

            var ct = _debounce.Token;
            Task.Delay(300, ct).ContinueWith(
                _ => RunOutlineAsync(ct),
                ct,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        private async Task RunOutlineAsync(CancellationToken ct)
        {
            if (_disposed || _buffer == null) return;

            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                var text = _buffer.CurrentSnapshot.GetText();
                if (string.IsNullOrEmpty(text))
                {
                    await UpdateNodesOnUiThreadAsync(Array.Empty<OutlineNodeDto>());
                    return;
                }

                var request = new DocumentOutlineRequest
                {
                    SessionId = _sessionId,
                    SqlText = text
                };

                var response = await client.SendRequestAsync<DocumentOutlineResponse, DocumentOutlineRequest>(
                    MessageTypes.DocumentOutline, request, timeoutMs: 5000, ct: ct);

                if (ct.IsCancellationRequested) return;

                if (response.Success)
                {
                    await UpdateNodesOnUiThreadAsync(response.RootNodes);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "DocumentOutlineViewModel: outline update failed");
            }
        }

        private async Task UpdateNodesOnUiThreadAsync(OutlineNodeDto[] nodes)
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            RootNodes.Clear();
            foreach (var node in nodes)
            {
                RootNodes.Add(node);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DetachFromBuffer();
            try { _debounce?.Cancel(); _debounce?.Dispose(); } catch (ObjectDisposedException) { }
        }
    }
}
