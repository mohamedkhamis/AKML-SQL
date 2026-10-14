using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;
using AkmlSql.PostgreSql.Language;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (FR-081, contracts/ipc.md 223 to 224): the web edition asks the engine to remove the literals from PostgreSQL
/// text before it goes to a cloud AI provider. The browser has no PostgreSQL scanner (the library is engine-only), and the
/// T-SQL redactor cannot read this text.
///
/// <para>
/// Only PostgreSQL text is served: a request for any other dialect is refused, never passed through. Text the scanner cannot
/// read to the end is refused too (<c>Ok = false</c>, empty text), so the client can tell the user and must not send the
/// original. The reply carries the placeholder map to the client that sent the text and nowhere else; neither the text nor
/// any literal is written to the log.
/// </para>
/// </summary>
public sealed class RedactHandler : IRpcRequestHandler<RedactRequest, RedactResponse>
{
    /// <summary>The same 10 MB limit a session document has.</summary>
    public const int MaxTextChars = 10 * 1024 * 1024;

    public int RequestMessageType => MessageTypes.RedactRequest;
    public int ResponseMessageType => MessageTypes.RedactResponse;

    public Task<RedactResponse> HandleAsync(RedactRequest request, RpcContext ctx, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (!DatabaseKindNames.TryParse(request.Dialect, out var kind) || kind != DatabaseKind.PostgreSql)
            return Task.FromResult(Refuse("Only PostgreSQL text is redacted by the engine; SQL Server text is redacted in the client."));

        var text = request.Text ?? string.Empty;
        if (text.Length > MaxTextChars)
            return Task.FromResult(Refuse("The text is larger than the 10 MB limit, so its literals cannot be removed."));

        try
        {
            var result = PgLiteralRedactor.Redact(text);
            return Task.FromResult(result.Ok
                ? new RedactResponse { Ok = true, Text = result.Text, Placeholders = new Dictionary<string, string>(result.Placeholders) }
                : Refuse(result.Error ?? "The text could not be read to the end, so its literals cannot be found."));
        }
        catch (Exception ex)
        {
            // Never the text: only the exception, which holds none of it.
            Serilog.Log.Warning(ex, "PostgreSQL literal redaction failed");
            return Task.FromResult(Refuse("The text could not be redacted."));
        }
    }

    private static RedactResponse Refuse(string error) => new() { Ok = false, Text = string.Empty, Error = error };
}
