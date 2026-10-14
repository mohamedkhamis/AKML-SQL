using System.Diagnostics;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.PostgreSql.Formatting;
using Serilog;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T152, FR-140 to FR-147) - formatting for a PostgreSQL session. The request handlers hand the text and the style here
/// instead of to the T-SQL pipeline: the style is read as a SQL Prompt style, the text is laid out by the PostgreSQL formatter, and the
/// schema-aware Format SQL actions (expand wildcards, qualify names) use PostgreSQL quoting. A script that does not parse, or a layout that
/// does not keep the meaning, comes back unchanged with the reason.
/// </summary>
public sealed class PgFormatService
{
    public const string NotAvailableForAction = "Not available for PostgreSQL";

    /// <summary>The style as the PostgreSQL layout reads it: a SQL Prompt document, whether the style was written in that model or not.</summary>
    public static SqlPromptStyle StyleOf(FormattingProfile profile) => new(SqlPromptStyles.ToDocument(profile));

    // ── document ─────────────────────────────────────────────────────────────────────────────────────

    public FormatResponse Format(string text, FormattingProfile profile, FormatSqlActionsDto? actions, DatabaseCache? cache, string? fallbackWarning)
    {
        var watch = Stopwatch.StartNew();
        var style = StyleOf(profile);
        var options = OptionsOf(profile, actions);
        var result = PgFormatter.Format(text, style, options);
        var diagnostics = result.Diagnostics.Select(ToInfo).ToList();
        var formatted = result.Text;

        if (result.Success && actions is { } a && (a.ExpandWildcards || a.QualifyObjectNames))
            formatted = ApplySchemaActions(formatted, a, style, options, cache, diagnostics);

        return new FormatResponse
        {
            Success = result.Success,
            FormattedText = formatted,
            WasModified = !string.Equals(formatted, text, StringComparison.Ordinal),
            ValidationPassed = result.ValidationPassed,
            ElapsedMs = watch.ElapsedMilliseconds,
            ProfileFallbackWarning = fallbackWarning,
            Diagnostics = diagnostics.ToArray(),
        };
    }

    // ── selection ────────────────────────────────────────────────────────────────────────────────────

    public FormatSelectionResponse FormatSelection(FormatSelectionRequest request, FormattingProfile profile, DatabaseCache? cache, string? fallbackWarning)
    {
        var watch = Stopwatch.StartNew();
        var style = StyleOf(profile);
        var options = OptionsOf(profile, request.Actions);
        var result = PgFormatter.FormatSelection(request.Text, request.SelectionStart, request.SelectionEnd, style, options);

        var formatted = result.Text;
        var modified = result.WasModified;
        if (result.Success && request.Actions is { } a && (a.ExpandWildcards || a.QualifyObjectNames))
        {
            var expanded = ApplySchemaActions(formatted, a, style, options, cache, diagnostics: null);
            if (!string.Equals(expanded, formatted, StringComparison.Ordinal))
            {
                formatted = expanded;
                modified = true;
            }
        }

        return new FormatSelectionResponse
        {
            Success = result.Success,
            FormattedText = formatted,
            OriginalStart = result.OriginalStart,
            OriginalEnd = result.OriginalEnd,
            WasModified = modified,
            ValidationPassed = result.Success,
            ElapsedMs = watch.ElapsedMilliseconds,
            ProfileFallbackWarning = fallbackWarning,
        };
    }

    // ── preview (the Format Styles page) ─────────────────────────────────────────────────────────────

    public FormatPreviewResponse Preview(string sample, FormattingProfile profile)
    {
        var watch = Stopwatch.StartNew();
        var result = PgFormatter.Format(sample, StyleOf(profile), new PgFormatOptions());
        return new FormatPreviewResponse
        {
            FormattedText = result.Text,
            ElapsedMs = watch.ElapsedMilliseconds,
            ValidationError = result.Success ? null : result.Diagnostics.FirstOrDefault()?.Message ?? "The preview could not be formatted with this style.",
        };
    }

    // ── single actions ───────────────────────────────────────────────────────────────────────────────

    public FormatActionResponse RunAction(FormatActionRequest request, FormattingProfile profile, DatabaseCache? cache)
    {
        var watch = Stopwatch.StartNew();
        var style = StyleOf(profile);
        var type = (FormatActionType)request.ActionType;

        // The selection, or the whole text when nothing is selected.
        var hasSelection = request.SelectionLength > 0;
        var start = hasSelection ? Math.Clamp(request.SelectionStart, 0, request.Text.Length) : 0;
        var end = hasSelection ? Math.Clamp(request.SelectionStart + request.SelectionLength, start, request.Text.Length) : request.Text.Length;
        var part = request.Text.Substring(start, end - start);

        string changed;
        var warnings = new List<string>();
        switch (type)
        {
            case FormatActionType.CasingOnly:
                changed = Run(PgFormatter.Format(part, style, new PgFormatOptions(ApplyLayout: false)), part, warnings);
                break;
            case FormatActionType.InsertSemicolons:
                changed = Run(PgFormatter.Format(part, style, new PgFormatOptions(ApplyLayout: false, ApplyCasing: false, Semicolons: 1)), part, warnings);
                break;
            case FormatActionType.RemoveSemicolons:
                changed = Run(PgFormatter.Format(part, style, new PgFormatOptions(ApplyLayout: false, ApplyCasing: false, Semicolons: 2)), part, warnings);
                break;
            case FormatActionType.ExpandWildcards:
            case FormatActionType.QualifyObjectNames:
            {
                if (PgParserOk(part, warnings))
                {
                    var action = type == FormatActionType.ExpandWildcards
                        ? PgFormatActions.ExpandWildcards(part, cache)
                        : PgFormatActions.QualifyNames(part, cache);
                    warnings.AddRange(action.Warnings);
                    changed = action.Changes > 0 && PgParserOk(action.Text, warnings, "The result would not parse, so the text was not changed.") ? action.Text : part;
                }
                else
                {
                    changed = part;
                }
                break;
            }
            default:
                return new FormatActionResponse
                {
                    Success = false,
                    FormattedText = request.Text,
                    ErrorMessage = NotAvailableForAction,
                    ElapsedMs = watch.ElapsedMilliseconds,
                };
        }

        var full = request.Text.Substring(0, start) + changed + request.Text.Substring(end);
        return new FormatActionResponse
        {
            Success = true,
            FormattedText = full,
            WasModified = !string.Equals(full, request.Text, StringComparison.Ordinal),
            ElapsedMs = watch.ElapsedMilliseconds,
            Warnings = warnings.Count == 0 ? null : warnings.ToArray(),
        };
    }

    private static string Run(PgFormatResult result, string original, List<string> warnings)
    {
        if (!result.Success) warnings.AddRange(result.Diagnostics.Select(d => d.Message));
        return result.Success ? result.Text : original;
    }

    private static bool PgParserOk(string text, List<string> warnings, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parsed = AkmlSql.PostgreSql.Language.PgParser.Parse(text);
        if (!parsed.Success) warnings.Add(message ?? $"Not changed: {parsed.Error!.Message}");
        return parsed.Success;
    }

    // ── shared ───────────────────────────────────────────────────────────────────────────────────────

    private static PgFormatOptions OptionsOf(FormattingProfile profile, FormatSqlActionsDto? actions) =>
        actions == null
            ? new PgFormatOptions(CheckIdempotency: profile.Metadata.EnableIdempotencyCheck)
            : new PgFormatOptions(
                ApplyCasing: actions.ApplyCasing,
                CheckIdempotency: profile.Metadata.EnableIdempotencyCheck,
                ApplyLayout: actions.ApplyLayout,
                Semicolons: actions.Semicolons switch
                {
                    FormatSqlActionsDto.Insert => 1,
                    FormatSqlActionsDto.Remove => 2,
                    _ => 0,
                });

    private static FormatDiagnosticInfo ToInfo(PgFormatDiagnostic d) =>
        new() { Severity = d.Severity, Message = d.Message, Offset = d.Offset, Line = d.Line };

    /// <summary>Expand wildcards / Qualify names on already formatted text; laid out once more so inserted names look like the rest.</summary>
    private static string ApplySchemaActions(string text, FormatSqlActionsDto actions, SqlPromptStyle style, PgFormatOptions options,
        DatabaseCache? cache, List<FormatDiagnosticInfo>? diagnostics)
    {
        var current = text;
        var warnings = new List<string>();

        if (actions.ExpandWildcards)
        {
            var expanded = PgFormatActions.ExpandWildcards(current, cache);
            warnings.AddRange(expanded.Warnings);
            current = expanded.Text;
        }
        if (actions.QualifyObjectNames)
        {
            var qualified = PgFormatActions.QualifyNames(current, cache);
            warnings.AddRange(qualified.Warnings);
            current = qualified.Text;
        }
        foreach (var warning in warnings.Distinct())
            diagnostics?.Add(new FormatDiagnosticInfo { Severity = 1, Message = warning });

        if (string.Equals(current, text, StringComparison.Ordinal)) return text;

        var parsed = AkmlSql.PostgreSql.Language.PgParser.Parse(current);
        if (!parsed.Success)
        {
            Log.Warning("PostgreSQL Format SQL actions produced a script that does not parse ({Error}); the formatted text is kept without them", parsed.Error!.Message);
            diagnostics?.Add(new FormatDiagnosticInfo { Severity = 1, Message = "Expand wildcards / Qualify names were not applied: the result would not parse." });
            return text;
        }

        if (options.ApplyLayout)
        {
            var again = PgFormatter.Format(current, style, options with { Semicolons = 0 });
            if (again.Success) current = again.Text;
        }
        return current;
    }
}
