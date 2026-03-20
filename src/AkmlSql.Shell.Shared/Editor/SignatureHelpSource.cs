using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// T063: MEF-exported provider that creates SignatureHelpSource instances for T-SQL buffers.
    /// </summary>
    [Export(typeof(ISignatureHelpSourceProvider))]
    [ContentType("T-SQL")]
    [Name("AkmlSqlSignatureHelpSource")]
    [Order(Before = "default")]
    internal class SignatureHelpSourceProvider : ISignatureHelpSourceProvider
    {
        public ISignatureHelpSource TryCreateSignatureHelpSource(ITextBuffer textBuffer)
        {
            return new SignatureHelpSource(textBuffer);
        }
    }

    /// <summary>
    /// T063: Bridges signature help requests to PipeRpcClient for SignatureRequest/SignatureResponse.
    /// Currently a skeleton with TODOs for actual IPC calls.
    /// </summary>
    internal class SignatureHelpSource : ISignatureHelpSource
    {
        private readonly ITextBuffer _buffer;
        private bool _disposed;

        public SignatureHelpSource(ITextBuffer buffer)
        {
            _buffer = buffer;
        }

        public void AugmentSignatureHelpSession(ISignatureHelpSession session, IList<ISignature> signatures)
        {
            if (_disposed) return;

            try
            {
                var point = session.GetTriggerPoint(_buffer.CurrentSnapshot);
                if (point == null) return;

                var position = point.Value.Position;

                // TODO: Send SignatureRequest via PipeRpcClient
                // var request = new SignatureRequest { SessionId = ..., CursorOffset = position };
                // var response = await _rpcClient.SendRequestAsync<SignatureResponse>(MessageTypes.Signature, request);
                // Convert response overloads to ISignature instances and add to signatures list

                Log.Debug("SignatureHelp requested at offset {Offset}", position);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error augmenting signature help session");
            }
        }

        public ISignature GetBestMatch(ISignatureHelpSession session)
        {
            // TODO: Implement best match selection based on active parameter
            return null;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
