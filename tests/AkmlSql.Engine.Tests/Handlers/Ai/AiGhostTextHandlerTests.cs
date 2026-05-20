using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Ai;
using AkmlSql.Engine;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Handlers.Ai;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers.Ai;

// Spec 022 (M0 closure) -- P3 / US3 follow-up. Smoke tests for AiGhostTextHandler.
// Mirrors AiTextToSqlHandlerTests: live AI provider calls are out of scope; these cover the
// wire-shape contract + the BuildErrorResponse routing for the guard paths so a regression
// in the AiHandlerBase template + subclass wiring fails fast. GhostText has an extra guard
// (InlineCompletion) over the other handlers, so it carries one extra test.
public class AiGhostTextHandlerTests
{
    private static RpcContext NewContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        ParserService = new TsqlParserService(),
        SettingsLoader = () => new AppSettings(),
    };

    private static AiGhostTextHandler NewHandler(AiSettings settings) =>
        new(AiPipelineServices.Build(
            new SchemaCacheManager(),
            new TsqlParserService(),
            () => settings));

    [Fact]
    public void Message_type_ids_match_the_wire_contract()
    {
        var handler = NewHandler(new AiSettings
        {
            Provider = "ollama", Enabled = true, InlineCompletion = true,
        });
        Assert.Equal(MessageTypes.AiGhostText, handler.RequestMessageType);
        Assert.Equal(MessageTypes.AiGhostTextResult, handler.ResponseMessageType);
    }

    [Fact]
    public async Task Returns_error_response_when_AI_assistance_disabled()
    {
        var handler = NewHandler(new AiSettings
        {
            Provider = "ollama", Enabled = false, InlineCompletion = true,
        });
        var resp = await handler.HandleAsync(
            new AiGhostTextRequest { SessionId = "test", PrecedingText = "SELECT * FROM " },
            NewContext(),
            CancellationToken.None);

        Assert.False(resp.Success);
        Assert.Contains("disabled", resp.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_error_response_when_inline_completion_disabled()
    {
        var handler = NewHandler(new AiSettings
        {
            Provider = "ollama", Enabled = true, InlineCompletion = false,
        });
        var resp = await handler.HandleAsync(
            new AiGhostTextRequest { SessionId = "test", PrecedingText = "SELECT * FROM " },
            NewContext(),
            CancellationToken.None);

        Assert.False(resp.Success);
        Assert.Contains("disabled", resp.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_error_response_when_preceding_text_is_empty()
    {
        var handler = NewHandler(new AiSettings
        {
            Provider = "ollama", Enabled = true, InlineCompletion = true,
        });
        var resp = await handler.HandleAsync(
            new AiGhostTextRequest { SessionId = "test", PrecedingText = "   " },
            NewContext(),
            CancellationToken.None);

        Assert.False(resp.Success);
        Assert.Contains("preceding", resp.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_error_response_when_consent_required_for_cloud_provider()
    {
        var handler = NewHandler(new AiSettings
        {
            Provider = "anthropic",
            Enabled = true,
            InlineCompletion = true,
            PrivacyConsentRequired = true,
        });
        var resp = await handler.HandleAsync(
            new AiGhostTextRequest { SessionId = "test", PrecedingText = "SELECT * FROM " },
            NewContext(),
            CancellationToken.None);

        Assert.False(resp.Success);
        Assert.StartsWith("CONSENT_REQUIRED:", resp.ErrorMessage);
    }
}
