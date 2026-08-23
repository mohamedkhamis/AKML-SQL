using System;
using AkmlSql.Engine.Ai.Providers;
using Xunit;

namespace AkmlSql.AI.Tests;

/// <summary>
/// The Mscc.GenerativeAI SDK retries internally (honouring Google's RetryInfo hints — 58 s
/// waits on a quota-exhausted key) with a ~2-minute default deadline, so ONE engine attempt
/// outlived both the provider timeout and the shell's IPC wait: the user saw a timeout instead
/// of Google's actual "You exceeded your current quota" message. The adapter must bound the
/// SDK's request deadline to the configured provider timeout and keep retry ownership in
/// AiPipelineServices.
/// </summary>
public sealed class GeminiAdapterTimeoutTests
{
    private static readonly string DummyKey = "AIza" + new string('x', 35);

    [Fact]
    public void Adapter_bounds_the_sdk_request_deadline_to_the_provider_timeout()
    {
        using var adapter = new GeminiChatClientAdapter(DummyKey, "gemini-flash-latest", timeoutSeconds: 45);

        Assert.Equal(TimeSpan.FromSeconds(45), adapter.RequestTimeout);
    }

    [Fact]
    public void Adapter_defaults_to_the_90s_provider_timeout()
    {
        using var adapter = new GeminiChatClientAdapter(DummyKey, "gemini-flash-latest");

        Assert.Equal(TimeSpan.FromSeconds(90), adapter.RequestTimeout);
    }

    [Fact]
    public void Nonsense_timeouts_fall_back_to_the_default()
    {
        using var adapter = new GeminiChatClientAdapter(DummyKey, "gemini-flash-latest", timeoutSeconds: 0);

        Assert.Equal(TimeSpan.FromSeconds(90), adapter.RequestTimeout);
    }
}
