using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
namespace AkmlSql.Engine.Server.Stubs
{
    /// <summary>
    /// Spec 019 / US14 stub for MessageType 91 (<see cref="MessageTypes.FindUnusedVariables"/>).
    /// Returns a "not yet implemented" error envelope until the real handler
    /// lands in US7 (Phase 10 / US7 — Script navigation chords; specifically
    /// `Ctrl+B, Ctrl+F` Find Unused Variables and Parameters).
    /// </summary>
    internal sealed class FindUnusedVariablesHandlerStub : IMessageHandler
    {
        public Task<RpcMessage?> HandleAsync(RpcMessage message, CancellationToken cancellationToken)
        {
            var resp = new FindUnusedVariablesResponse
            {
                Status = 1, // ParseError used as a stand-in for "not implemented"
                ErrorMessage = "FindUnusedVariables not yet implemented (spec 019 US7 — pending)",
            };
            return Task.FromResult<RpcMessage?>(
                PipeRpcServer.CreateResponse(
                    MessageTypes.FindUnusedVariablesResult,
                    message.RequestId,
                    resp));
        }
    }
}
