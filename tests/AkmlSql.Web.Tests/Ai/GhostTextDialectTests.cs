using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Ai;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 042 (task T112, FR-080, FR-081). Ghost text follows the dialect of the connection and, on PostgreSQL with a cloud
/// provider, is written from text whose literals were removed. It fails silently by design, so a script whose literals cannot
/// be removed is not sent and nobody is asked.
/// </summary>
public sealed class GhostTextDialectTests
{
    private const string Before = "SELECT * FROM orders WHERE customer = 'ACME' AND total > ";
    private const string Redacted = "SELECT * FROM orders WHERE customer = '__STR_0__' AND total > ";

    private sealed class Dialect : IAiDialectContext
    {
        public SqlDialect Dialect_ { get; set; }
        SqlDialect IAiDialectContext.Dialect => Dialect_;
        public string? ServerVersion { get; set; } = "16.4";
    }

    private sealed class Redactor : IPostgresRedactor
    {
        public int Calls { get; private set; }
        public PostgresRedaction Result { get; set; } = new(true, Redacted, new Dictionary<string, string> { ["'__STR_0__'"] = "'ACME'" }, null);

        public Task<PostgresRedaction> RedactAsync(string text, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private static (AiGhostTextService Service, Client Client, Redactor Redactor, Dialect Dialect) Build(SqlDialect dialect, string provider)
    {
        var store = new AiFeatureSettingsStore(new InMemoryIndexedDbAdapter());
        store.SetAsync(new AiFeatureSettings { GhostTextEnabled = true, GhostTextMaxRequestsPer3s = 5, GlobalDefaultMode = AiPrivacyMode.FullSchema })
            .GetAwaiter().GetResult();
        var client = new Client();
        var redactor = new Redactor();
        var context = new Dialect { Dialect_ = dialect };
        var service = new AiGhostTextService(client, new Preference(provider), new Schema(), store, () => DateTimeOffset.UnixEpoch, context, redactor);
        return (service, client, redactor, context);
    }

    [Fact]
    public async Task On_SQL_Server_the_prompt_is_the_T_SQL_one_and_the_text_is_untouched()
    {
        var (service, client, redactor, _) = Build(SqlDialect.SqlServer, "anthropic");

        await service.CompleteAsync(Before, CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Contains("SQL Server developer", request.SystemPrompt);
        Assert.Equal(Before, request.UserPrompt);
        Assert.Equal(0, redactor.Calls);
    }

    [Fact]
    public async Task On_PostgreSQL_with_a_local_provider_the_text_is_untouched_and_the_prompt_is_PostgreSQL()
    {
        var (service, client, redactor, _) = Build(SqlDialect.PostgreSql, "ollama");

        await service.CompleteAsync(Before, CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Contains("PostgreSQL developer", request.SystemPrompt);
        Assert.Equal(Before, request.UserPrompt);
        Assert.Equal(0, redactor.Calls);
    }

    [Fact]
    public async Task On_PostgreSQL_with_a_cloud_provider_the_text_before_the_caret_is_redacted_and_the_suggestion_restored()
    {
        var (service, client, redactor, _) = Build(SqlDialect.PostgreSql, "anthropic");
        client.Answer = "'__STR_0__'::text";

        var suggestion = await service.CompleteAsync(Before, CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Equal(Redacted, request.UserPrompt);
        Assert.DoesNotContain("ACME", request.UserPrompt);
        Assert.Equal(1, redactor.Calls);
        Assert.Equal("'ACME'::text", suggestion);
    }

    [Fact]
    public async Task When_the_literals_cannot_be_removed_nothing_is_sent_nothing_is_counted_and_nobody_is_asked()
    {
        var (service, client, redactor, _) = Build(SqlDialect.PostgreSql, "anthropic");
        redactor.Result = PostgresRedaction.Failed("The text ends inside a string literal.");

        var suggestion = await service.CompleteAsync(Before, CancellationToken.None);

        Assert.Null(suggestion);
        Assert.Empty(client.Requests);
        Assert.Equal(0, service.SessionRequestCount);
    }

    [Fact]
    public async Task A_suggestion_made_for_one_dialect_is_not_served_to_the_other()
    {
        var (service, client, _, context) = Build(SqlDialect.SqlServer, "ollama");
        client.Answer = "dbo.Orders o";
        await service.CompleteAsync("SELECT * FROM ", CancellationToken.None);

        context.Dialect_ = SqlDialect.PostgreSql;
        client.Answer = "public.orders o";
        var second = await service.CompleteAsync("SELECT * FROM ", CancellationToken.None);

        Assert.Equal("public.orders o", second);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task A_redactor_that_throws_is_a_silent_null_and_sends_nothing()
    {
        var store = new AiFeatureSettingsStore(new InMemoryIndexedDbAdapter());
        store.SetAsync(new AiFeatureSettings { GhostTextEnabled = true, GlobalDefaultMode = AiPrivacyMode.FullSchema }).GetAwaiter().GetResult();
        var client = new Client();
        var service = new AiGhostTextService(
            client, new Preference("anthropic"), new Schema(), store, () => DateTimeOffset.UnixEpoch,
            new Dialect { Dialect_ = SqlDialect.PostgreSql }, new ThrowingRedactor());

        Assert.Null(await service.CompleteAsync(Before, CancellationToken.None));
        Assert.Empty(client.Requests);
    }

    private sealed class ThrowingRedactor : IPostgresRedactor
    {
        public Task<PostgresRedaction> RedactAsync(string text, CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    private sealed class Client : IAiClientFactory
    {
        public List<AkmlSql.Web.Services.AiChatRequest> Requests { get; } = new();
        public string Answer { get; set; } = "Orders o";

        public Task<string> SendAsync(string providerId, AkmlSql.Web.Services.AiChatRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Answer);
        }

#pragma warning disable CS1998
        public async IAsyncEnumerable<string> StreamAsync(
            string providerId, AkmlSql.Web.Services.AiChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return Answer;
        }
#pragma warning restore CS1998

        public bool IsOriginAllowed(string providerId, string origin) => true;
        public bool IsBrowserDirectCapable(string providerId) => true;
    }

    private sealed class Preference : IAiPreference
    {
        private readonly string _id;
        public Preference(string id) => _id = id;
        public Task<string> GetActiveAsync() => Task.FromResult(_id);
        public Task SetActiveAsync(string providerId) => Task.CompletedTask;
    }

    private sealed class Schema : IAiSchemaContextProvider
    {
        public Task<string> GetSchemaTextAsync(string featureId, string? promptOrSql, CancellationToken ct) => Task.FromResult(string.Empty);
    }
}
