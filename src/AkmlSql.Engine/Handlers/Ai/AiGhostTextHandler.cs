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

/// <summary>Spec 022 (M0 closure) -- P3 / US3. Concrete handler for <c>MessageTypes.AiGhostText</c>.
/// Low-latency inline next-token completion. Uses level-1 schema context with a 2000-token budget
/// to stay under ~500 ms response time. Replaces <c>AiRequestHandler.HandleGhostTextAsync</c>.</summary>
public sealed class AiGhostTextHandler : AiHandlerBase<AiGhostTextRequest, AiGhostTextResponse>
{
    public AiGhostTextHandler(AiPipelineServices svcs) : base(svcs) { }
    public override int RequestMessageType => MessageTypes.AiGhostText;
    public override int ResponseMessageType => MessageTypes.AiGhostTextResult;

    protected override AiGhostTextResponse BuildErrorResponse(string errorMessage, long elapsedMs) =>
        new() { Success = false, CursorOffset = 0, ErrorMessage = errorMessage, LatencyMs = (int)elapsedMs };

    protected override async Task<AiGhostTextResponse> InvokeAsync(
        AiGhostTextRequest request, RpcContext ctx, AiSettings settings, Stopwatch sw, CancellationToken ct)
    {
        if (!settings.Enabled || !settings.InlineCompletion)
            throw new InvalidOperationException("Inline completion is disabled");
        if (string.IsNullOrWhiteSpace(request.PrecedingText))
            throw new ArgumentException("No preceding text provided");

        Log.Debug("AiGhostText: session={Session}, cursor={Offset}, preceding length={Length}",
            request.SessionId, request.CursorOffset, request.PrecedingText.Length);

        (string? ConnectionString, string? DatabaseName) sessionLookup(string sid)
        {
            var s = ctx.Sessions.GetSession(sid);
            return s == null || !s.IsConnected ? (null, null) : (s.ConnectionString, s.DatabaseName);
        }

        var schemaContext = await Services.SchemaContext.BuildAsync(
            request.SessionId, sessionLookup, request.PrecedingText, compressionLevel: 1, maxObjects: 50);
        var schemaText = SchemaContextFormatter.Format(schemaContext);
        var estimatedTokens = TokenEstimator.EstimateTokensForModel(schemaText, settings.Provider);
        if (estimatedTokens > 2000)
        {
            schemaContext = await Services.SchemaContext.BuildAsync(
                request.SessionId, sessionLookup, request.PrecedingText, compressionLevel: 1, maxObjects: 30);
            schemaText = SchemaContextFormatter.Format(schemaContext);
        }

        var (transformedPreceding, transformedContext, transformation) =
            Services.Privacy.Transform(request.PrecedingText, schemaContext, settings.PrivacyMode);
        if (transformedContext != null && transformation.IdentifierMap.Count > 0)
            schemaText = SchemaContextFormatter.Format(transformedContext);

        var (systemPrompt, userPrompt) = GhostTextPrompt.Build(schemaText, transformedPreceding);
        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt),
        };
        var options = new ChatOptions { MaxOutputTokens = 150, Temperature = 0.2f };
        var (aiResponse, _) = await Services.ExecuteWithFallbackAsync(settings, chatMessages, options, ct);
        var predictedText = aiResponse.Text ?? string.Empty;
        if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            predictedText = PrivacyTransformer.DeTransform(predictedText, transformation);
        predictedText = AiPipelineServices.StripCodeFences(predictedText).TrimEnd();

        var tokensUsed = aiResponse.Usage != null
            ? (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0)) : 0;
        sw.Stop();
        Log.Debug("AiGhostText: success, tokens={Tokens}, latency={LatencyMs}ms, prediction length={Len}",
            tokensUsed, sw.ElapsedMilliseconds, predictedText.Length);

        return new AiGhostTextResponse
        {
            Success = true, PredictedText = predictedText, CursorOffset = request.CursorOffset,
            TokensUsed = tokensUsed, LatencyMs = (int)sw.ElapsedMilliseconds,
        };
    }
}
