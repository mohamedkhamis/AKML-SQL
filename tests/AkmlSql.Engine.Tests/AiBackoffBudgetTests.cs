using System.Net;
using AkmlSql.Engine.Ai;
using Xunit;

namespace AkmlSql.Engine.Tests;

/// <summary>
/// The 429 retry loop must be bounded in TIME, not only in attempts. A quota-exhausted Gemini
/// key made <see cref="AiPipelineServices.ExecuteWithBackoffAsync{T}"/> grind through
/// retries for 318 s (attempts themselves were slow) — far past both the provider timeout and
/// the shell's wait, so the user saw "A task was canceled" instead of the provider's real
/// "You exceeded your current quota" message.
/// </summary>
public sealed class AiBackoffBudgetTests
{
    private static HttpRequestException RateLimited() =>
        new("You exceeded your current quota, please check your plan and billing details.",
            inner: null, statusCode: HttpStatusCode.TooManyRequests);

    [Fact]
    public async Task Exhausted_budget_surfaces_the_provider_error_without_retrying()
    {
        var attempts = 0;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            AiPipelineServices.ExecuteWithBackoffAsync<string>(
                () => { attempts++; throw RateLimited(); },
                maxRetries: 10,
                CancellationToken.None,
                retryBudget: TimeSpan.Zero));

        Assert.Equal(1, attempts);                                  // no doomed retries
        Assert.Contains("exceeded your current quota", ex.Message); // the REAL provider error
    }

    [Fact]
    public async Task Within_budget_a_transient_429_still_retries_and_succeeds()
    {
        var attempts = 0;

        var result = await AiPipelineServices.ExecuteWithBackoffAsync(
            () =>
            {
                attempts++;
                if (attempts == 1) throw RateLimited();
                return Task.FromResult("ok");
            },
            maxRetries: 3,
            CancellationToken.None,
            retryBudget: TimeSpan.FromMinutes(1));

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task No_budget_keeps_the_attempt_bound()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            AiPipelineServices.ExecuteWithBackoffAsync<string>(
                () => { attempts++; throw RateLimited(); },
                maxRetries: 1,
                CancellationToken.None));

        Assert.Equal(2, attempts);   // initial + 1 retry, as before
    }
}
