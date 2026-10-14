using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Ai;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 042 (task T112, FR-080 to FR-082, SC-011). Every AI feature is told the dialect of the active connection, and
/// PostgreSQL text that goes to a cloud provider has its literals removed first: through the engine, with the literals put
/// back into the answer, and with nothing sent when they could not be removed unless the user says so. SQL Server behaves as
/// it did, and a local provider never needs redaction because the text stays on the machine.
/// </summary>
public sealed class AiPromptDialectTests
{
    private const string Script = "SELECT * FROM customers WHERE country = 'UK' AND age > 30";
    private const string Redacted = "SELECT * FROM customers WHERE country = '__STR_0__' AND age > __NUM_0__";

    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["'__STR_0__'"] = "'UK'",
        ["__NUM_0__"] = "30",
    };

    private sealed class Rig
    {
        public RecordingClient Client { get; } = new();
        public FakeDialogService Dialogs { get; } = new();
        public FakeRedactor Redactor { get; } = new();
        public FakeDialect Dialect { get; } = new();
        public AiPromptService Service { get; private set; } = null!;

        public static Rig Build(
            SqlDialect dialect, string provider, string? version = "16.4", AiPrivacyMode mode = AiPrivacyMode.FullSchema, bool consent = true)
        {
            var rig = new Rig();
            rig.Dialect.Dialect = dialect;
            rig.Dialect.ServerVersion = version;
            rig.Dialogs.ConfirmAnswer = _ => consent;

            var store = new AiFeatureSettingsStore(new InMemoryIndexedDbAdapter());
            store.SetAsync(new AiFeatureSettings { GlobalDefaultMode = mode }).GetAwaiter().GetResult();
            rig.Service = new AiPromptService(rig.Client, new FixedPreference(provider), new EmptySchema(), store, rig.Dialect, rig.Redactor, rig.Dialogs);
            return rig;
        }
    }

    // ---- SQL Server is unchanged ------------------------------------------------------------------------------

    [Fact]
    public async Task A_SqlServer_connection_keeps_the_T_SQL_prompt_and_never_redacts_or_asks()
    {
        var rig = Rig.Build(SqlDialect.SqlServer, "anthropic");

        await rig.Service.ExplainAsync(Script, CancellationToken.None);

        var request = Assert.Single(rig.Client.Requests);
        Assert.Contains("SQL Server developer", request.SystemPrompt);
        Assert.DoesNotContain("PostgreSQL developer", request.SystemPrompt);
        Assert.Contains(Script, request.UserPrompt);
        Assert.Equal(0, rig.Redactor.Calls);
        Assert.Empty(rig.Dialogs.Confirms);
    }

    [Fact]
    public async Task Without_a_dialect_context_the_service_is_the_SqlServer_service_it_was()
    {
        var store = new AiFeatureSettingsStore(new InMemoryIndexedDbAdapter());
        var client = new RecordingClient();
        var service = new AiPromptService(client, new FixedPreference("anthropic"), new EmptySchema(), store);

        await service.ExplainAsync(Script, CancellationToken.None);

        Assert.Contains("SQL Server developer", Assert.Single(client.Requests).SystemPrompt);
    }

    // ---- PostgreSQL: the prompt -------------------------------------------------------------------------------

    [Theory]
    [InlineData("explain")]
    [InlineData("fix")]
    [InlineData("optimize")]
    [InlineData("texttosql")]
    [InlineData("indexanalysis")]
    public async Task Every_feature_names_PostgreSQL_and_the_server_version_on_a_PostgreSql_connection(string feature)
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "ollama", "16.4");

        _ = feature switch
        {
            "explain" => await rig.Service.ExplainAsync(Script, CancellationToken.None),
            "fix" => await rig.Service.FixAsync(Script, new AiErrorInfo("boom"), CancellationToken.None),
            "optimize" => await rig.Service.OptimizeAsync(Script, CancellationToken.None),
            "texttosql" => await rig.Service.TextToSqlAsync("orders per customer", CancellationToken.None),
            _ => await rig.Service.IndexAnalysisAsync(Script, CancellationToken.None),
        };

        var system = Assert.Single(rig.Client.Requests).SystemPrompt;
        Assert.Contains("PostgreSQL developer", system);
        Assert.Contains("PostgreSQL 16.4", system);
        Assert.Contains("Do not use T-SQL", system);
        Assert.DoesNotContain("SQL Server", system);
    }

    [Fact]
    public async Task The_streaming_methods_carry_the_dialect_too()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "ollama");
        rig.Client.Chunks = new[] { "a", "b" };

        var text = new StringBuilder();
        await foreach (var piece in rig.Service.OptimizeStreamAsync(Script, CancellationToken.None)) text.Append(piece);

        Assert.Equal("ab", text.ToString());
        Assert.Contains("PostgreSQL developer", Assert.Single(rig.Client.Requests).SystemPrompt);
    }

    // ---- PostgreSQL: a local provider -------------------------------------------------------------------------

    [Fact]
    public async Task A_local_provider_gets_the_script_as_it_is_because_it_never_leaves_the_machine()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "ollama");

        await rig.Service.ExplainAsync(Script, CancellationToken.None);

        Assert.Contains(Script, Assert.Single(rig.Client.Requests).UserPrompt);
        Assert.Equal(0, rig.Redactor.Calls);
        Assert.Empty(rig.Dialogs.Confirms);
    }

    // ---- PostgreSQL: a cloud provider -------------------------------------------------------------------------

    [Fact]
    public async Task A_cloud_provider_gets_the_redacted_script_and_never_the_literals()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic");
        rig.Redactor.Result = new PostgresRedaction(true, Redacted, Map, null);

        await rig.Service.ExplainAsync(Script, CancellationToken.None);

        var user = Assert.Single(rig.Client.Requests).UserPrompt;
        Assert.Equal(Script, rig.Redactor.LastText);
        Assert.Contains(Redacted, user);
        Assert.DoesNotContain("'UK'", user);
        Assert.DoesNotContain("30", user);
        Assert.Empty(rig.Dialogs.Confirms);
    }

    [Fact]
    public async Task The_literals_are_put_back_into_the_answer()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic");
        rig.Redactor.Result = new PostgresRedaction(true, Redacted, Map, null);
        rig.Client.Answer = "OPTIMIZED SQL:\nSELECT id FROM customers WHERE country = '__STR_0__' AND age > __NUM_0__";

        var answer = await rig.Service.OptimizeAsync(Script, CancellationToken.None);

        Assert.Equal("OPTIMIZED SQL:\nSELECT id FROM customers WHERE country = 'UK' AND age > 30", answer);
    }

    [Fact]
    public async Task A_placeholder_split_across_streamed_pieces_is_still_put_back()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic");
        rig.Redactor.Result = new PostgresRedaction(true, Redacted, Map, null);
        rig.Client.Chunks = new[] { "WHERE country = '__S", "TR_0__' AND ag", "e > __NU", "M_0__ done" };

        var text = new StringBuilder();
        await foreach (var piece in rig.Service.ExplainStreamAsync(Script, CancellationToken.None)) text.Append(piece);

        Assert.Equal("WHERE country = 'UK' AND age > 30 done", text.ToString());
    }

    [Fact]
    public async Task A_stream_that_ends_inside_a_possible_placeholder_still_delivers_everything()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic");
        rig.Redactor.Result = new PostgresRedaction(true, Redacted, Map, null);
        rig.Client.Chunks = new[] { "it ends with '__ST" };

        var text = new StringBuilder();
        await foreach (var piece in rig.Service.ExplainStreamAsync(Script, CancellationToken.None)) text.Append(piece);

        Assert.Equal("it ends with '__ST", text.ToString());
    }

    [Fact]
    public async Task Text_to_SQL_is_prose_so_it_is_not_sent_through_the_redactor()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic");

        await rig.Service.TextToSqlAsync("orders of customers in the UK over 30", CancellationToken.None);

        Assert.Equal(0, rig.Redactor.Calls);
        Assert.Contains("orders of customers in the UK over 30", Assert.Single(rig.Client.Requests).UserPrompt);
    }

    [Fact]
    public async Task Index_analysis_and_optimize_redact_too()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic");
        rig.Redactor.Result = new PostgresRedaction(true, Redacted, Map, null);

        await rig.Service.IndexAnalysisAsync(Script, CancellationToken.None);
        await rig.Service.OptimizeAsync(Script, CancellationToken.None);

        Assert.Equal(2, rig.Redactor.Calls);
        Assert.All(rig.Client.Requests, r => Assert.DoesNotContain("'UK'", r.UserPrompt));
    }

    // ---- PostgreSQL: a script that cannot be redacted ---------------------------------------------------------

    [Fact]
    public async Task A_script_that_cannot_be_redacted_asks_and_sends_nothing_when_the_user_declines()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic", consent: false);
        rig.Redactor.Result = PostgresRedaction.Failed("The text ends inside a string literal.");

        var ex = await Assert.ThrowsAsync<AiNotSentException>(() => rig.Service.ExplainAsync(Script, CancellationToken.None));

        var ask = Assert.Single(rig.Dialogs.Confirms);
        Assert.Contains("Literals could not be removed from this script", ask.Message);
        Assert.Equal("Send anyway", ask.ConfirmLabel);
        Assert.Equal("Cancel", ask.CancelLabel);
        Assert.Equal("ai-redaction-consent", ask.DialogId);
        Assert.Contains("Not sent", ex.Message);
        Assert.Empty(rig.Client.Requests);
    }

    [Fact]
    public async Task Sending_anyway_sends_the_original_script_and_the_answer_is_left_as_it_is()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic", consent: true);
        rig.Redactor.Result = PostgresRedaction.Failed("The text ends inside a string literal.");
        rig.Client.Answer = "works on '__STR_0__'";

        var answer = await rig.Service.ExplainAsync(Script, CancellationToken.None);

        Assert.Contains(Script, Assert.Single(rig.Client.Requests).UserPrompt);
        Assert.Equal("works on '__STR_0__'", answer);
    }

    [Fact]
    public async Task No_engine_to_redact_is_a_failed_redaction_and_asks_in_the_same_way()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic", consent: false);
        rig.Redactor.Result = PostgresRedaction.Failed("No engine is paired.");

        await Assert.ThrowsAsync<AiNotSentException>(() => rig.Service.OptimizeAsync(Script, CancellationToken.None));

        Assert.Single(rig.Dialogs.Confirms);
        Assert.Empty(rig.Client.Requests);
    }

    [Fact]
    public async Task The_question_does_not_quote_the_script_or_the_engine_text()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic", consent: false);
        rig.Redactor.Result = PostgresRedaction.Failed("The text ends inside a string literal.");

        await Assert.ThrowsAsync<AiNotSentException>(() => rig.Service.ExplainAsync(Script, CancellationToken.None));

        var ask = Assert.Single(rig.Dialogs.Confirms);
        var everything = ask.Title + ask.Message + string.Join(" ", ask.Details ?? Array.Empty<string>());
        Assert.DoesNotContain("'UK'", everything);
        Assert.DoesNotContain("customers", everything);
    }

    // ---- the fully-local guard comes first --------------------------------------------------------------------

    [Fact]
    public async Task Fully_local_with_a_cloud_provider_refuses_before_anything_is_redacted_or_asked()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic", mode: AiPrivacyMode.FullyLocal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Service.ExplainAsync(Script, CancellationToken.None));

        Assert.Equal(0, rig.Redactor.Calls);
        Assert.Empty(rig.Dialogs.Confirms);
        Assert.Empty(rig.Client.Requests);
    }

    // ---- fix: the error of the server -------------------------------------------------------------------------

    [Fact]
    public async Task Fix_on_PostgreSQL_carries_the_SQLSTATE_message_detail_and_hint()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "ollama");
        var error = new AiErrorInfo("relation \"custmers\" does not exist", 0, "42P01", "The table was dropped.", "Perhaps you meant customers.");

        await rig.Service.FixAsync("SELECT * FROM custmers", error, CancellationToken.None);

        var user = Assert.Single(rig.Client.Requests).UserPrompt;
        Assert.Contains("SQLSTATE: 42P01", user);
        Assert.Contains("relation \"custmers\" does not exist", user);
        Assert.Contains("Detail: The table was dropped.", user);
        Assert.Contains("Hint: Perhaps you meant customers.", user);
        Assert.DoesNotContain("Error Number", user);
    }

    [Fact]
    public async Task Fix_on_PostgreSQL_with_a_cloud_provider_redacts_the_script_but_sends_the_server_error()
    {
        var rig = Rig.Build(SqlDialect.PostgreSql, "anthropic");
        rig.Redactor.Result = new PostgresRedaction(true, Redacted, Map, null);
        var error = new AiErrorInfo("column \"agee\" does not exist", 0, "42703", null, "Perhaps you meant \"age\".");

        await rig.Service.FixAsync(Script, error, CancellationToken.None);

        var user = Assert.Single(rig.Client.Requests).UserPrompt;
        Assert.Contains(Redacted, user);
        Assert.DoesNotContain("'UK'", user);
        Assert.Contains("SQLSTATE: 42703", user);
        Assert.Contains("column \"agee\" does not exist", user);
    }

    [Fact]
    public async Task Fix_on_SQL_Server_still_sends_the_error_number_the_way_it_did()
    {
        var rig = Rig.Build(SqlDialect.SqlServer, "ollama");

        await rig.Service.FixAsync(Script, "Invalid object name 'x'.", 208, CancellationToken.None);

        var user = Assert.Single(rig.Client.Requests).UserPrompt;
        Assert.Contains("Error Number: 208", user);
        Assert.DoesNotContain("SQLSTATE", user);
    }

    // ---- fakes ------------------------------------------------------------------------------------------------

    private sealed class FakeDialect : IAiDialectContext
    {
        public SqlDialect Dialect { get; set; }
        public string? ServerVersion { get; set; }
    }

    private sealed class FakeRedactor : IPostgresRedactor
    {
        public int Calls { get; private set; }
        public string? LastText { get; private set; }
        public PostgresRedaction Result { get; set; } = new(true, string.Empty, new Dictionary<string, string>(), null);

        public Task<PostgresRedaction> RedactAsync(string text, CancellationToken ct)
        {
            Calls++;
            LastText = text;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingClient : IAiClientFactory
    {
        public List<AkmlSql.Web.Services.AiChatRequest> Requests { get; } = new();
        public string Answer { get; set; } = "ok";
        public string[]? Chunks { get; set; }

        public Task<string> SendAsync(string providerId, AkmlSql.Web.Services.AiChatRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Answer);
        }

#pragma warning disable CS1998 // an async iterator with no await is intentional for the fake
        public async IAsyncEnumerable<string> StreamAsync(
            string providerId, AkmlSql.Web.Services.AiChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Requests.Add(request);
            foreach (var chunk in Chunks ?? new[] { Answer }) yield return chunk;
        }
#pragma warning restore CS1998

        public bool IsOriginAllowed(string providerId, string origin) => true;
        public bool IsBrowserDirectCapable(string providerId) => true;
    }

    private sealed class FixedPreference : IAiPreference
    {
        private readonly string _id;
        public FixedPreference(string id) => _id = id;
        public Task<string> GetActiveAsync() => Task.FromResult(_id);
        public Task SetActiveAsync(string providerId) => Task.CompletedTask;
    }

    private sealed class EmptySchema : IAiSchemaContextProvider
    {
        public Task<string> GetSchemaTextAsync(string featureId, string? promptOrSql, CancellationToken ct) => Task.FromResult(string.Empty);
    }
}
