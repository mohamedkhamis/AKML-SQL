#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Analysis;
using AkmlSql.Shell.Shared.Ipc;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    [CollectionDefinition("AnalysisController seams", DisableParallelization = true)]
    public sealed class AnalysisControllerSeamsCollection { }

    /// <summary>
    /// Spec 047 (SSMS-002, contracts/analysis-display.md §5) — a document opened before the engine connected was never
    /// analysed until the user typed: the controller's first pass found no connected client and gave up. It now waits
    /// for the engine (one wait per controller, however many passes run meanwhile) and analyses once it connects,
    /// without any edit.
    /// </summary>
    [Collection("AnalysisController seams")]
    public sealed class AnalysisDeferredUntilEngineTests : IDisposable
    {
        private readonly Func<IRpcClientAccessor> _savedAccessor = AnalysisController.RpcAccessor;
        private readonly Func<Func<Task>, Task<bool>> _savedWait = AnalysisController.WhenEngineReady;

        private readonly FakeRpcClientAccessor _rpc = new FakeRpcClientAccessor { IsConnected = false };
        private readonly List<Func<Task>> _deferrals = new List<Func<Task>>();

        public AnalysisDeferredUntilEngineTests()
        {
            _rpc.Respond(MessageTypes.RequestAnalyze, new CodeAnalysisResponse
            {
                Issues = new[] { new CodeIssueInfo { RuleId = "ST001", Message = "Missing semicolon", Severity = 1 } },
            });
            AnalysisController.RpcAccessor = () => _rpc;
            AnalysisController.WhenEngineReady = send =>
            {
                lock (_deferrals) _deferrals.Add(send);
                return new TaskCompletionSource<bool>().Task;   // the engine has not connected yet
            };
        }

        public void Dispose()
        {
            AnalysisController.RpcAccessor = _savedAccessor;
            AnalysisController.WhenEngineReady = _savedWait;
        }

        private int Deferrals { get { lock (_deferrals) return _deferrals.Count; } }

        private static async Task WaitUntil(Func<bool> condition, string what)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.ElapsedMilliseconds > 5000) throw new TimeoutException("Timed out waiting for " + what);
                await Task.Delay(20);
            }
        }

        [Fact]
        public async Task A_document_opened_before_the_engine_connects_is_analysed_once_it_does()
        {
            var updates = 0;
            CodeIssueInfo[]? issues = null;
            using var controller = new AnalysisController(new FakeTextBuffer("select 1"), "session-1");
            controller.DiagnosticsUpdated += (_, e) => { issues = e.Issues; Interlocked.Increment(ref updates); };

            // The opening pass finds no engine: it sends nothing and waits for one.
            await WaitUntil(() => Deferrals == 1, "the deferral");
            Assert.Empty(_rpc.Requests);
            Assert.Equal(0, updates);

            // The engine connects: the waiting pass runs, with no edit to the buffer.
            _rpc.IsConnected = true;
            Func<Task> deferred;
            lock (_deferrals) deferred = _deferrals[0];
            await deferred();

            await WaitUntil(() => Volatile.Read(ref updates) == 1, "DiagnosticsUpdated");
            Assert.Single(_rpc.Requests, r => r.MessageType == MessageTypes.RequestAnalyze);
            Assert.Equal("ST001", Assert.Single(issues!).RuleId);
        }

        [Fact]
        public async Task Several_passes_while_disconnected_wait_once()
        {
            using var controller = new AnalysisController(new FakeTextBuffer("select 1"), "session-2");
            await WaitUntil(() => Deferrals == 1, "the first deferral");

            controller.TriggerReanalysis();
            await Task.Delay(500);   // past the 300 ms debounce: the second pass has run and found no engine either
            controller.TriggerReanalysis();
            await Task.Delay(500);

            Assert.Equal(1, Deferrals);
            Assert.Empty(_rpc.Requests);
        }
    }
}
