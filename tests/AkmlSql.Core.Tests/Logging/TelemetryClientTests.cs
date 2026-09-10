using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Logging;
using Serilog;
using Serilog.Events;
using Xunit;

namespace AkmlSql.Core.Tests.Logging
{
    public class TelemetryClientTests
    {
        /// <summary>Captures request URIs + bodies and responds with a fixed status.</summary>
        private sealed class CaptureHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly TaskCompletionSource<string> _firstBody =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public readonly List<(Uri? Uri, string Body)> Requests = new();
            public Task<string> FirstBody => _firstBody.Task;

            public CaptureHandler(HttpStatusCode status)
            {
                _status = status;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync();
                lock (Requests)
                {
                    Requests.Add((request.RequestUri, body));
                }

                _firstBody.TrySetResult(body);
                return new HttpResponseMessage(_status);
            }
        }

        /// <summary>Throws on the first send (offline), succeeds afterwards.</summary>
        private sealed class FailOnceHandler : HttpMessageHandler
        {
            private readonly TaskCompletionSource<string> _recovered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _firstAttempt =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _calls;

            public Task<string> Recovered => _recovered.Task;
            public Task FirstAttempt => _firstAttempt.Task;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync();
                if (Interlocked.Increment(ref _calls) == 1)
                {
                    _firstAttempt.TrySetResult(true);
                    throw new HttpRequestException("offline");
                }

                _recovered.TrySetResult(body);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        private static TelemetryEvent Error(string message) => new()
        {
            Utc = DateTimeOffset.UtcNow,
            Level = "Error",
            Message = message
        };

        [Fact]
        public async Task Enqueue_PostsBatchToTelemetryEndpoint()
        {
            var handler = new CaptureHandler(HttpStatusCode.NoContent);
            using var client = new TelemetryClient("testinstall", handler, TimeSpan.FromMilliseconds(50));

            client.Enqueue(Error("boom"));

            var body = await handler.FirstBody.WaitAsync(TimeSpan.FromSeconds(10));

            Uri? uri;
            lock (handler.Requests)
            {
                uri = handler.Requests[0].Uri;
            }

            Assert.Equal(Constants.TelemetryUrl, uri?.ToString());

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            Assert.Equal("testinstall", root.GetProperty("installId").GetString());
            Assert.Equal(Constants.RuntimeVersion, root.GetProperty("productVersion").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("host").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("os").GetString()));

            var first = root.GetProperty("events")[0];
            Assert.Equal("Error", first.GetProperty("level").GetString());
            Assert.Equal("boom", first.GetProperty("message").GetString());
        }

        [Fact]
        public async Task Payload_IsAnonymous_NoUserOrMachineName()
        {
            var handler = new CaptureHandler(HttpStatusCode.NoContent);
            using var client = new TelemetryClient("anoninstall", handler, TimeSpan.FromMilliseconds(50));

            client.Enqueue(Error("something failed"));

            var body = await handler.FirstBody.WaitAsync(TimeSpan.FromSeconds(10));

            if (!string.IsNullOrEmpty(Environment.UserName))
            {
                Assert.DoesNotContain(Environment.UserName, body);
            }

            if (!string.IsNullOrEmpty(Environment.MachineName))
            {
                Assert.DoesNotContain(Environment.MachineName, body);
            }
        }

        [Fact]
        public async Task LevelFiltering_BelowMinimumLevelIsNotSent()
        {
            var handler = new CaptureHandler(HttpStatusCode.NoContent);
            using var client = new TelemetryClient("inst", handler, TimeSpan.FromMilliseconds(50));
            using var log = new LoggerConfiguration()
                .WriteTo.Sink(new TelemetrySink(client), restrictedToMinimumLevel: LogEventLevel.Error)
                .CreateLogger();

            log.Information("quiet detail that must stay local");
            log.Error("loud failure");

            var body = await handler.FirstBody.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("loud failure", body);
            Assert.DoesNotContain("quiet detail", body);
        }

        [Fact]
        public async Task SendFailure_DropsBatchAndRecovers_WithoutThrowing()
        {
            var handler = new FailOnceHandler();
            using var client = new TelemetryClient("inst", handler, TimeSpan.FromMilliseconds(50));

            // The first send fails and its batch is dropped by design — a retried batch would
            // duplicate events. Only events enqueued AFTER the failure are recoverable.
            client.Enqueue(Error("first batch dies"));
            await handler.FirstAttempt.WaitAsync(TimeSpan.FromSeconds(10));

            client.Enqueue(Error("second batch lands"));
            var body = await handler.Recovered.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("second batch lands", body);
        }

        [Fact]
        public void Dispose_FlushesQueuedEvents()
        {
            var handler = new CaptureHandler(HttpStatusCode.NoContent);
            var client = new TelemetryClient("inst", handler, TimeSpan.FromMilliseconds(50));

            client.Enqueue(Error("flush me on the way out"));
            client.Dispose();

            lock (handler.Requests)
            {
                Assert.Single(handler.Requests);
                Assert.Contains("flush me on the way out", handler.Requests[0].Body);
            }
        }

        [Fact]
        public async Task Message_IsTruncatedToTheServerCap()
        {
            var handler = new CaptureHandler(HttpStatusCode.NoContent);
            using var client = new TelemetryClient("inst", handler, TimeSpan.FromMilliseconds(50));

            client.Enqueue(Error(new string('x', TelemetryEvent.MaxMessageLength + 1000)));

            var body = await handler.FirstBody.WaitAsync(TimeSpan.FromSeconds(10));

            using var doc = JsonDocument.Parse(body);
            var message = doc.RootElement.GetProperty("events")[0].GetProperty("message").GetString();
            Assert.Equal(TelemetryEvent.MaxMessageLength, message!.Length);
        }
    }
}
