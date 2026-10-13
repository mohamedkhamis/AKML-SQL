using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Ai.Providers;
using Microsoft.Extensions.AI;
using Xunit;

namespace AkmlSql.AI.Tests;

/// <summary>
/// Mscc.GenerativeAI wraps every non-HTTP failure in a GeminiApiException with no response —
/// the caller's cancellation included. Left wrapped, the provider test reported a cancelled test
/// as "unexpected error (GeminiApiException)" and the fallback chain (whose contract is that
/// cancellation never falls back) walked on to the next agent. The adapter must surface the
/// caller's cancellation as an <see cref="OperationCanceledException"/>. A pre-cancelled token
/// fails inside the SDK before any request leaves the machine, so no network is needed.
/// </summary>
public sealed class GeminiAdapterCancellationTests
{
    private static readonly string DummyKey = "AIza" + new string('x', 35);

    [Fact]
    public async Task The_callers_cancellation_surfaces_as_OperationCanceledException()
    {
        using var adapter = new GeminiChatClientAdapter(DummyKey, "gemini-flash-latest", timeoutSeconds: 30);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.GetResponseAsync("hello", cancellationToken: cts.Token));

        Assert.Equal(cts.Token, ex.CancellationToken);
    }
}
