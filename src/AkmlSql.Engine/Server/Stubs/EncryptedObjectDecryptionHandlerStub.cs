using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
namespace AkmlSql.Engine.Server.Stubs
{
    /// <summary>
    /// Spec 019 / US14 stub for MessageType 92 (<see cref="MessageTypes.EncryptedObjectDecryption"/>).
    /// Returns a "not yet implemented" error envelope until the real handler
    /// lands in US11 (Phase 10 / US11 — Completion polish; specifically the
    /// DAC-permission-gated decrypted-body display in the Object Definition Box).
    /// </summary>
    internal sealed class EncryptedObjectDecryptionHandlerStub : IMessageHandler
    {
        public Task<RpcMessage?> HandleAsync(RpcMessage message, CancellationToken cancellationToken)
        {
            var resp = new EncryptedObjectDecryptionResponse
            {
                Status = 4, // Error
                ErrorMessage = "EncryptedObjectDecryption not yet implemented (spec 019 US11 — pending)",
            };
            return Task.FromResult<RpcMessage?>(
                RpcResponseFactory.CreateResponse(
                    MessageTypes.EncryptedObjectDecryptionResult,
                    message.RequestId,
                    resp));
        }
    }
}
