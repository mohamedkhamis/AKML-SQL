using AkmlSql.Engine.Ai;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Spec 042 (task T112, FR-080, FR-090). The chat panel tells the model the dialect of the active connection at the moment of
/// sending. SQL Server keeps the text it always had; PostgreSQL names PostgreSQL and forbids T-SQL.
/// </summary>
public sealed class AiChatPanelDialectTests : BunitContext
{
    private readonly FakeAiClientFactory _client = new();
    private readonly Context _dialect = new();

    private sealed class Context : IAiDialectContext
    {
        public SqlDialect Value { get; set; } = SqlDialect.SqlServer;
        SqlDialect IAiDialectContext.Dialect => Value;
        public string? ServerVersion { get; set; }
    }

    public AiChatPanelDialectTests()
    {
        Services.AddSingleton<IAiClientFactory>(_client);
        Services.AddSingleton<IAiPreference>(new FakeAiPreference("anthropic"));
        Services.AddSingleton<IAiFeatureSettings>(new FakeAiFeatureSettings());
        Services.AddSingleton<IChatHistoryStore>(new FakeChatHistoryStore());
        Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
        Services.AddSingleton<IAiDialectContext>(_dialect);
    }

    private async Task<string> SystemPromptSentAsync()
    {
        var cut = Render<AiChatPanel>();
        cut.Find("textarea").Input("What does my query do?");
        await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Send").Click());
        cut.WaitForState(() => _client.Requests.Count > 0);
        return _client.Requests[0].SystemPrompt;
    }

    [Fact]
    public async Task A_SqlServer_connection_keeps_the_text_the_panel_always_had()
    {
        _dialect.Value = SqlDialect.SqlServer;

        var system = await SystemPromptSentAsync();

        Assert.Equal(
            "You are a SQL Server expert assisting an experienced developer through AKML SQL. Be concise. " +
            "When you give SQL, use canonical T-SQL formatting. When you're unsure of the schema, ask before guessing.",
            system);
    }

    [Fact]
    public async Task A_PostgreSql_connection_gets_a_PostgreSQL_text_with_the_version_and_without_SQL_Server()
    {
        _dialect.Value = SqlDialect.PostgreSql;
        _dialect.ServerVersion = "16.4";

        var system = await SystemPromptSentAsync();

        Assert.Contains("PostgreSQL expert", system);
        Assert.Contains("PostgreSQL 16.4", system);
        Assert.Contains("Do not use T-SQL", system);
        Assert.DoesNotContain("SQL Server", system);
    }

    [Fact]
    public void The_text_for_each_dialect_is_chosen_by_AiChatSystemText()
    {
        Assert.Equal(AiChatSystemText.SqlServer, AiChatSystemText.For(SqlDialect.SqlServer, "16.4"));
        Assert.DoesNotContain("The server is", AiChatSystemText.For(SqlDialect.PostgreSql, null));
        Assert.Contains("PostgreSQL 18.1", AiChatSystemText.For(SqlDialect.PostgreSql, "PostgreSQL 18.1"));
        Assert.DoesNotContain("PostgreSQL PostgreSQL", AiChatSystemText.For(SqlDialect.PostgreSql, "PostgreSQL 18.1"));
    }
}
