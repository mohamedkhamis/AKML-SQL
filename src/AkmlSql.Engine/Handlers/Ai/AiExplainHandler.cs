using System.Diagnostics;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Ai;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Ai.Context;
using AkmlSql.Engine.Ai.Privacy;
using AkmlSql.Engine.Ai.Prompts;
using Microsoft.Extensions.AI;
using Serilog;

namespace AkmlSql.Engine.Handlers.Ai;

/// <summary>Spec 022 (M0 closure) -- P3 / US3. Concrete handler for <c>MessageTypes.AiExplain</c>.
/// Explains a SQL statement in natural language with structured Purpose / StepByStep / KeyDetails / Suggestions
/// sections. Replaces <c>AiRequestHandler.HandleExplainAsync</c>.</summary>
public sealed class AiExplainHandler : AiHandlerBase<AiExplainRequest, AiExplainResponse>
{
    public AiExplainHandler(AiPipelineServices svcs) : base(svcs) { }
    public override int RequestMessageType => MessageTypes.AiExplain;
    public override int ResponseMessageType => MessageTypes.AiExplainResult;

    protected override AiExplainResponse BuildErrorResponse(string errorMessage, long elapsedMs) =>
        new() { Success = false, ErrorMessage = errorMessage, LatencyMs = (int)elapsedMs };

    protected override async Task<AiExplainResponse> InvokeAsync(
        AiExplainRequest request, RpcContext ctx, AiSettings settings, Stopwatch sw, CancellationToken ct)
    {
        if (!settings.Enabled) throw new InvalidOperationException("AI assistance is disabled");
        if (string.IsNullOrWhiteSpace(request.SelectedSql)) throw new ArgumentException("No SQL text provided");

        Log.Debug("AiExplain: session={Session}, sql length={Length}",
            request.SessionId, request.SelectedSql.Length);

        (string? ConnectionString, string? DatabaseName) sessionLookup(string sid)
        {
            var s = ctx.Sessions.GetSession(sid);
            return s == null || !s.IsConnected ? (null, null) : (s.ConnectionString, s.DatabaseName);
        }

        var schemaContext = await Services.SchemaContext.BuildAsync(
            request.SessionId, sessionLookup, request.SelectedSql, compressionLevel: 3,
            maxObjects: settings.SchemaContextMaxObjects);
        var schemaText = SchemaContextFormatter.Format(schemaContext);
        var (transformedSql, transformedContext, transformation) =
            Services.Privacy.Transform(request.SelectedSql, schemaContext, settings.PrivacyMode);
        if (transformedContext != null && transformation.IdentifierMap.Count > 0)
            schemaText = SchemaContextFormatter.Format(transformedContext);
        var (systemPrompt, userPrompt) = ExplainPrompt.Build(schemaText, transformedSql);

        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt),
        };
        var options = new ChatOptions
        {
            MaxOutputTokens = settings.MaxTokens,
            Temperature = (float)settings.Temperature,
        };
        var (aiResponse, usedFallback) = await Services.ExecuteWithFallbackAsync(settings, chatMessages, options, ct);
        var responseText = aiResponse.Text ?? string.Empty;
        if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            responseText = PrivacyTransformer.DeTransform(responseText, transformation);

        var (purpose, stepByStep, keyDetails, suggestions) = AiPipelineServices.ParseExplainSections(responseText);
        if (usedFallback)
            purpose = $"[Fallback model: {settings.OfflineProvider}/{settings.OfflineModel}] {purpose}";

        var tokensUsed = aiResponse.Usage != null
            ? (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0)) : 0;
        sw.Stop();
        Log.Information("AiExplain: success, tokens={Tokens}, latency={LatencyMs}ms, fallback={Fallback}",
            tokensUsed, sw.ElapsedMilliseconds, usedFallback);

        return new AiExplainResponse
        {
            Success = true, Purpose = purpose, StepByStep = stepByStep,
            KeyDetails = keyDetails, Suggestions = suggestions,
            TokensUsed = tokensUsed, LatencyMs = (int)sw.ElapsedMilliseconds,
        };
    }
}
