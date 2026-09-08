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

/// <summary>Spec 022 (M0 closure) -- P3 / US3. Concrete handler for <c>MessageTypes.AiChat</c>.
/// Multi-turn SQL-aware chat with conversation history. Returns the response plus copy-to-clipboard
/// code actions extracted from ```sql blocks. Replaces <c>AiRequestHandler.HandleChatAsync</c>.</summary>
public sealed class AiChatHandler : AiHandlerBase<AiChatRequest, AiChatResponse>
{
    public AiChatHandler(AiPipelineServices svcs) : base(svcs) { }
    public override int RequestMessageType => MessageTypes.AiChat;
    public override int ResponseMessageType => MessageTypes.AiChatResult;
    public override AiFeature Feature => AiFeature.Chat;

    protected override AiChatResponse BuildErrorResponse(string errorMessage, long elapsedMs) =>
        new() { Success = false, ErrorMessage = errorMessage, LatencyMs = (int)elapsedMs };

    protected override async Task<AiChatResponse> InvokeAsync(
        AiChatRequest request, RpcContext ctx, AiSettings settings, AiAgent? resolvedAgent, Stopwatch sw, CancellationToken ct)
    {
        if (!settings.Enabled) throw new InvalidOperationException("AI assistance is disabled");
        if (string.IsNullOrWhiteSpace(request.Message)) throw new ArgumentException("Message cannot be empty");

        Log.Debug("AiChat: session={Session}, message length={Length}, history={HistoryCount}",
            request.SessionId, request.Message.Length, request.History.Count);

        (string? ConnectionString, string? DatabaseName) sessionLookup(string sid)
        {
            var s = ctx.Sessions.GetSession(sid);
            return s == null || !s.IsConnected ? (null, null) : (s.ConnectionString, s.DatabaseName);
        }

        var schemaContext = await Services.SchemaContext.BuildAsync(
            request.SessionId, sessionLookup, request.Message, compressionLevel: 3,
            maxObjects: settings.SchemaContextMaxObjects);
        var schemaText = SchemaContextFormatter.Format(schemaContext);
        var (transformedMessage, transformedContext, transformation) =
            Services.Privacy.Transform(request.Message, schemaContext, settings.PrivacyMode);
        if (transformedContext != null && transformation.IdentifierMap.Count > 0)
            schemaText = SchemaContextFormatter.Format(transformedContext);

        var systemPrompt = ChatSystemPrompt.Build(schemaText);
        var chatMessages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };

        if (request.History.Count > 0)
        {
            foreach (var turn in request.History)
            {
                var role = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                    ? ChatRole.Assistant : ChatRole.User;
                var content = turn.Content;
                var needsTransform = role == ChatRole.User &&
                    (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0);
                if (needsTransform)
                {
                    var (transformedTurn, _, _) =
                        Services.Privacy.Transform(content, schemaContext, settings.PrivacyMode);
                    content = transformedTurn;
                }
                chatMessages.Add(new ChatMessage(role, content));
            }
        }
        chatMessages.Add(new ChatMessage(ChatRole.User, transformedMessage));

        var options = new ChatOptions { MaxOutputTokens = settings.MaxTokens, Temperature = (float)settings.Temperature };
        var (aiResponse, usedFallback, agentName) = await Services.ExecuteWithFallbackAsync(
            settings, resolvedAgent, chatMessages, options, ct);
        var responseText = aiResponse.Text ?? string.Empty;
        if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            responseText = PrivacyTransformer.DeTransform(responseText, transformation);

        var codeActions = AiPipelineServices.ExtractCodeActions(responseText);

        var tokensUsed = aiResponse.Usage != null
            ? (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0)) : 0;
        sw.Stop();
        Log.Information("AiChat: success, tokens={Tokens}, latency={LatencyMs}ms, codeActions={Actions}, fallback={Fallback}",
            tokensUsed, sw.ElapsedMilliseconds, codeActions.Count, usedFallback);

        return new AiChatResponse
        {
            Success = true, Response = responseText, CodeActions = codeActions,
            TokensUsed = tokensUsed, LatencyMs = (int)sw.ElapsedMilliseconds,
            // Spec 037 (US3/US4, FR-042/FR-052): who ACTUALLY answered — the resolved chat
            // agent, a fallback-chain agent, or the offline provider's display name (T073).
            // The shell states a fallback plainly from this name and its own selection.
            AgentName = agentName,
        };
    }
}
