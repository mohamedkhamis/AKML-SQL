using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
namespace AkmlSql.Engine.Server.Stubs
{
    /// <summary>
    /// Spec 019 / US14 stub for MessageType 90 (<see cref="MessageTypes.FindInvalidObjects"/>).
    /// Returns a "not yet implemented" error envelope until the real handler
    /// lands in US8 (Phase 10 / US8 — Find Invalid Objects across the database).
    /// <para>
    /// Registered into <see cref="PipeRpcServer"/>'s pluggable handler dictionary
    /// during construction (T008). Migrating the inline stub-case off the
    /// 53-case switch demonstrates the hybrid dispatch pattern works without
    /// touching any of the other 50 cases.
    /// </para>
    /// </summary>
    internal sealed class FindInvalidObjectsHandlerStub : IMessageHandler
    {
        public Task<RpcMessage?> HandleAsync(RpcMessage message, CancellationToken cancellationToken)
        {
            var resp = new FindInvalidObjectsResponse
            {
                Status = 2, // Error
                ErrorMessage = "FindInvalidObjects not yet implemented (spec 019 US8 — pending)",
                IsFinalChunk = true,
            };
            return Task.FromResult<RpcMessage?>(
                PipeRpcServer.CreateResponse(
                    MessageTypes.FindInvalidObjectsResult,
                    message.RequestId,
                    resp));
        }
    }
}
