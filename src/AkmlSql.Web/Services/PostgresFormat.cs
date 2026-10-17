using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 042 (task T154, FR-140, FR-145) - formatting a PostgreSQL script. The T-SQL formatter runs inside the browser; the PostgreSQL one
/// lives in the engine (it needs the real PostgreSQL parser, which has native code), so a PostgreSQL script is sent there together with
/// the active style, and the answer is read back as an ordinary <see cref="FormatResult"/>.
/// </summary>
public interface IPostgresFormatEngine
{
    /// <summary>Formats a whole script with the style. A failure of any kind comes back as an unsuccessful result with the text unchanged.</summary>
    Task<FormatResult> FormatAsync(string sql, FormattingProfile profile, CancellationToken ct);

    /// <summary>Formats the Format Styles page's PostgreSQL sample with the style being edited.</summary>
    Task<FormatResult> PreviewAsync(string sample, FormattingProfile profile, CancellationToken ct);
}

internal sealed class EnginePostgresFormat : IPostgresFormatEngine
{
    /// <summary>Why nothing was formatted when the engine cannot be asked.</summary>
    public const string NoEngine = "No engine is connected, so a PostgreSQL script cannot be formatted.";
    public const string OldEngine = "The paired engine cannot format PostgreSQL. Update the engine.";

    private readonly IEngineBridge _bridge;
    private readonly ISqlConnectionService _connection;

    public EnginePostgresFormat(IEngineBridge bridge, ISqlConnectionService connection)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public async Task<FormatResult> FormatAsync(string sql, FormattingProfile profile, CancellationToken ct)
    {
        if (!Available(out var why)) return Failure(sql, why!);
        try
        {
            var response = await _bridge.SendAsync<FormatRequest, FormatResponse>(
                MessageTypes.FormatDocument,
                new FormatRequest { SessionId = _connection.SessionId, Text = sql, ProfileJson = ProfileSerializer.Serialize(profile) },
                ct).ConfigureAwait(false);
            if (response == null) return Failure(sql, "The engine did not answer.");

            return new FormatResult
            {
                Success = response.Success,
                FormattedText = response.FormattedText ?? sql,
                WasModified = response.WasModified,
                ValidationPassed = response.ValidationPassed,
                ElapsedMs = response.ElapsedMs,
                Diagnostics = (response.Diagnostics ?? Array.Empty<FormatDiagnosticInfo>())
                    .Select(d => new FormatDiagnostic { Severity = (DiagnosticSeverity)d.Severity, Message = d.Message, Offset = d.Offset, Line = d.Line })
                    .ToArray(),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure(sql, "The engine could not format the script: " + ex.Message);
        }
    }

    public async Task<FormatResult> PreviewAsync(string sample, FormattingProfile profile, CancellationToken ct)
    {
        if (!Available(out var why)) return Failure(sample, why!);
        try
        {
            var response = await _bridge.SendAsync<FormatPreviewRequest, FormatPreviewResponse>(
                MessageTypes.FormatPreview,
                new FormatPreviewRequest
                {
                    SessionId = _connection.SessionId,
                    SampleText = sample,
                    ProfileJson = ProfileSerializer.Serialize(profile),
                    Dialect = (int)AkmlSql.Core.DatabaseKind.PostgreSql,
                },
                ct).ConfigureAwait(false);
            if (response == null) return Failure(sample, "The engine did not answer.");

            var ok = string.IsNullOrEmpty(response.ValidationError);
            return new FormatResult
            {
                Success = ok,
                FormattedText = response.FormattedText ?? sample,
                ValidationPassed = ok,
                ElapsedMs = response.ElapsedMs,
                Diagnostics = ok ? [] : [new FormatDiagnostic { Severity = DiagnosticSeverity.Warning, Message = response.ValidationError! }],
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure(sample, "The engine could not format the sample: " + ex.Message);
        }
    }

    private bool Available(out string? why)
    {
        why = null;
        if (_bridge.State != BridgeState.Open) why = NoEngine;
        else if (!_bridge.Advertises(SqlConnectionService.CapabilityPostgreSql)) why = OldEngine;
        return why == null;
    }

    private static FormatResult Failure(string text, string message) => new()
    {
        Success = false,
        FormattedText = text,
        WasModified = false,
        ValidationPassed = false,
        Diagnostics = [new FormatDiagnostic { Severity = DiagnosticSeverity.Error, Message = message }],
    };
}
