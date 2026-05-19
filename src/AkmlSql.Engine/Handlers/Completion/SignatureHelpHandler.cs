using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Transports;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Handlers.Completion
{
    /// <summary>
    /// Spec 021 (web edition) -- M0.4 wave 2 task T020. Wraps the existing
    /// <see cref="SignatureProvider"/> + a private token-walking helper (previously on
    /// <c>PipeRpcServer</c>) as a typed handler. The helper was folded into this class as
    /// part of the T020 finishing step that shrunk PipeRpcServer.
    /// </summary>
    public sealed class SignatureHelpHandler : IRpcRequestHandler<SignatureRequest, SignatureResponse>
    {
        private readonly TsqlParserService _parserService;
        private readonly SignatureProvider _signatureProvider;

        public SignatureHelpHandler(TsqlParserService parserService, SignatureProvider signatureProvider)
        {
            _parserService = parserService ?? throw new ArgumentNullException(nameof(parserService));
            _signatureProvider = signatureProvider ?? throw new ArgumentNullException(nameof(signatureProvider));
        }

        public int RequestMessageType => MessageTypes.RequestSignatureHelp;
        public int ResponseMessageType => MessageTypes.SignatureHelpResult;

        public Task<SignatureResponse> HandleAsync(SignatureRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var session = ctx.Sessions.GetSession(request.SessionId);
            var documentText = session?.DocumentText ?? string.Empty;
            var cache = session != null
                ? ctx.SchemaCache.GetCache(request.SessionId, session.DatabaseName)
                : null;

            var tokens = _parserService.GetTokenStream(documentText);
            var (funcName, parenOffset) = FindFunctionAtCursor(tokens, request.CursorOffset);
            var paramIdx = parenOffset >= 0
                ? SignatureProvider.CountCommasBeforeCursor(tokens, request.CursorOffset, parenOffset)
                : 0;

            var response = _signatureProvider.GetSignature(funcName, paramIdx, cache);
            return Task.FromResult(response);
        }

        /// <summary>
        /// Walk back from <paramref name="cursorOffset"/> to find the nearest open paren at
        /// the current nesting level, then walk back from that paren to find the function
        /// identifier (or keyword-function like CONVERT / CAST). Returns the function name
        /// and the open-paren offset, or <c>("", -1)</c> if no function call is found.
        /// </summary>
        internal static (string FunctionName, int ParenOffset) FindFunctionAtCursor(
            IList<TSqlParserToken> tokens, int cursorOffset)
        {
            int depth = 0;
            int parenTokenIndex = -1;
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                var t = tokens[i];
                if (t.Offset >= cursorOffset) continue;

                if (t.TokenType == TSqlTokenType.RightParenthesis)
                {
                    depth++;
                }
                else if (t.TokenType == TSqlTokenType.LeftParenthesis)
                {
                    if (depth == 0)
                    {
                        parenTokenIndex = i;
                        break;
                    }
                    depth--;
                }
            }

            if (parenTokenIndex <= 0) return (string.Empty, -1);

            // Walk back from paren to find the identifier (function name).
            for (int i = parenTokenIndex - 1; i >= 0; i--)
            {
                var t = tokens[i];
                if (t.TokenType == TSqlTokenType.WhiteSpace) continue;

                if (t.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
                {
                    return (t.Text.Trim('[', ']', '"'), tokens[parenTokenIndex].Offset);
                }
                // Keyword-function like CONVERT / CAST.
                return (t.Text, tokens[parenTokenIndex].Offset);
            }

            return (string.Empty, -1);
        }
    }
}
