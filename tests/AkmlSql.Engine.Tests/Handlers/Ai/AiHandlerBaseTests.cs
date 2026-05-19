using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
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

// Spec 022 (M0 closure) -- P3 / US3.
// Covers the AiHandlerBase template-method contract per
// specs/022-m0-engine-closure/contracts/ai-handler-base.md.
public class AiHandlerBaseTests
{
    private sealed record TestRequest(string Text);
    private sealed record TestResponse
    {
        public string Echo { get; init; } = "";
    }

    private sealed class EchoHandler : AiHandlerBase<TestRequest, TestResponse>
    {
        public EchoHandler(AiPipelineServices svcs) : base(svcs) { }
        public override int RequestMessageType => 9999;
        public override int ResponseMessageType => 10099;
        protected override Task<TestResponse> InvokeAsync(
            TestRequest req, RpcContext ctx, AiSettings settings, Stopwatch sw, CancellationToken ct)
            => Task.FromResult(new TestResponse { Echo = req.Text });
        protected override TestResponse BuildErrorResponse(string message, long elapsedMs)
            => new() { Echo = $"err:{message}" };
    }

    private static RpcContext NewContext()
    {
        return new RpcContext
        {
            Sessions = new SessionManager(),
            SchemaCache = new SchemaCacheManager(),
            Logger = Log.Logger,
            ParserService = new TsqlParserService(),
            SettingsLoader = () => new AppSettings(),
        };
    }

    private static AiPipelineServices ServicesFor(AiSettings settings)
        => AiPipelineServices.Build(
            new SchemaCacheManager(),
            new TsqlParserService(),
            () => settings);

    [Fact]
    public async Task Local_provider_skips_consent_check_and_returns_response()
    {
        var settings = new AiSettings { Provider = "ollama", PrivacyConsentRequired = true };
        var handler = new EchoHandler(ServicesFor(settings));

        var resp = await handler.HandleAsync(new TestRequest("hi"), NewContext(), CancellationToken.None);

        Assert.Equal("hi", resp.Echo);
    }

    [Fact]
    public async Task Cloud_provider_with_consent_required_returns_error_response_via_BuildErrorResponse()
    {
        // Base catches PrivacyConsentRequiredException and routes through BuildErrorResponse;
        // the typed response carries the consent-required message in Echo (via EchoHandler's
        // error-shape).
        var settings = new AiSettings { Provider = "anthropic", PrivacyConsentRequired = true };
        var handler = new EchoHandler(ServicesFor(settings));

        var resp = await handler.HandleAsync(new TestRequest("hi"), NewContext(), CancellationToken.None);

        Assert.StartsWith("err:CONSENT_REQUIRED:", resp.Echo);
    }

    [Fact]
    public async Task Cloud_provider_with_consent_given_returns_response()
    {
        var settings = new AiSettings { Provider = "anthropic", PrivacyConsentRequired = false };
        var handler = new EchoHandler(ServicesFor(settings));

        var resp = await handler.HandleAsync(new TestRequest("ok"), NewContext(), CancellationToken.None);

        Assert.Equal("ok", resp.Echo);
    }
}
