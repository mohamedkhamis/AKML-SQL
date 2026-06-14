using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Snippets.Models;
using Serilog;

namespace AkmlSql.Engine.Snippets;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Snippet models are simple POCOs preserved by DynamicDependency in Program.cs")]
public class SnippetRequestHandler
{
    private const int MaxSnippetJsonChars = 1024 * 1024; // 1 MB
    private readonly SnippetLoader _loader = new();
    private readonly SnippetIndex _index = new();
    private readonly BuiltInVariableResolver _variableResolver = new();
    private readonly List<SnippetSource> _sources = [];

    public SnippetRequestHandler(string personalFolder, string builtInFolder, string? teamFolder = null)
    {
        _sources.Add(new SnippetSource { Type = SnippetSourceType.Personal, Path = personalFolder, IsWriteable = true });
        if (!string.IsNullOrEmpty(teamFolder))
            _sources.Add(new SnippetSource { Type = SnippetSourceType.Team, Path = teamFolder, IsWriteable = false });
        _sources.Add(new SnippetSource { Type = SnippetSourceType.BuiltIn, Path = builtInFolder, IsWriteable = false });

        ReloadIndex();
    }

    public void ReloadIndex()
    {
        var allSnippets = _loader.LoadFromSources(_sources);
        _index.Rebuild(allSnippets);
        Log.Information("Snippet index rebuilt: {Count} snippets from {Sources} sources", _index.Count, _sources.Count);
    }

    public SnippetExpandResponse HandleExpand(SnippetExpandRequest request, string? databaseName = null, string? serverName = null)
    {
        var snippet = _index.GetByShortcode(request.Shortcode);
        if (snippet == null)
            return new SnippetExpandResponse { Success = false, ErrorMessage = $"No snippet found for shortcode: {request.Shortcode}" };

        var bodyText = string.Join("\n", snippet.Body);
        var context = new BuiltInVariableContext
        {
            DatabaseName = databaseName ?? string.Empty,
            ServerName = serverName ?? string.Empty,
            ClipboardText = request.ClipboardText,
            SelectedText = request.SelectedText
        };

        var expandedText = _variableResolver.Resolve(bodyText, context);

        // Locate the markers in the resolved text BEFORE stripping. $CURSOR$ positions the caret;
        // $SELECTIONSTART$/$SELECTIONEND$ delimit the post-expansion selection (T040/T047). All three
        // are stripped, so each surviving offset must be remapped against the FINAL text — a marker
        // shifts every offset that appears AFTER it by its own length.
        var rawCursor   = expandedText.IndexOf("$CURSOR$",         StringComparison.OrdinalIgnoreCase);
        var rawSelStart = expandedText.IndexOf("$SELECTIONSTART$", StringComparison.OrdinalIgnoreCase);
        var rawSelEnd   = expandedText.IndexOf("$SELECTIONEND$",   StringComparison.OrdinalIgnoreCase);

        expandedText = expandedText
            .Replace("$CURSOR$",         "", StringComparison.OrdinalIgnoreCase)
            .Replace("$SELECTIONSTART$", "", StringComparison.OrdinalIgnoreCase)
            .Replace("$SELECTIONEND$",   "", StringComparison.OrdinalIgnoreCase);

        // Strict '<' so a marker never subtracts its own length; -1 (absent) maps to -1.
        int MapOffset(int raw)
        {
            if (raw < 0) return -1;
            var shift = 0;
            if (rawCursor   >= 0 && rawCursor   < raw) shift += "$CURSOR$".Length;
            if (rawSelStart >= 0 && rawSelStart < raw) shift += "$SELECTIONSTART$".Length;
            if (rawSelEnd   >= 0 && rawSelEnd   < raw) shift += "$SELECTIONEND$".Length;
            return raw - shift;
        }

        var cursorOffset     = MapOffset(rawCursor);
        var selectionStart   = MapOffset(rawSelStart);
        var selectionEnd     = MapOffset(rawSelEnd);

        // Adjust cursor offset for removed $CURSOR$ marker (caret falls at end-of-text when absent).
        // Selection offsets stay -1 when absent — they are not clamped.
        if (cursorOffset < 0) cursorOffset = expandedText.Length;

        var placeholders = PlaceholderParser.Parse(expandedText, snippet.Variables);

        return new SnippetExpandResponse
        {
            Success = true,
            ExpandedText = expandedText,
            Placeholders = placeholders.ToArray(),
            CursorOffset = cursorOffset,
            SelectionStartOffset = selectionStart,
            SelectionEndOffset = selectionEnd,
            WasFormatted = false // TODO: integrate format-on-expand
        };
    }

    public SnippetListResponse HandleList(SnippetListRequest request)
    {
        IEnumerable<(Snippet Snippet, SnippetSourceType Source)> results;

        if (!string.IsNullOrEmpty(request.Query))
            results = _index.Search(request.Query);
        else if (!string.IsNullOrEmpty(request.Context) || request.HasSelection)
            results = _index.GetByContext(request.Context, request.HasSelection);
        else
            results = _index.GetAll();

        if (request.SourceFilter > 0)
            results = results.Where(r => (int)r.Source == request.SourceFilter);
        if (!string.IsNullOrEmpty(request.CategoryFilter))
            results = results.Where(r => r.Snippet.Metadata.Category.Equals(request.CategoryFilter, StringComparison.OrdinalIgnoreCase));

        var infos = results.Select(r => new SnippetInfo
        {
            Id = r.Snippet.Metadata.Id,
            Shortcode = r.Snippet.Metadata.Shortcode,
            Name = r.Snippet.Metadata.Name,
            Description = r.Snippet.Metadata.Description,
            Category = r.Snippet.Metadata.Category,
            Source = (int)r.Source,
            SurroundsWith = r.Snippet.Metadata.SurroundsWith,
            UsageCount = 0, // TODO: integrate usage tracker
            Tags = r.Snippet.Metadata.Tags
        }).ToArray();

        return new SnippetListResponse { Snippets = infos };
    }

    public SnippetSaveResponse HandleSave(SnippetSaveRequest request)
    {
        try
        {
            if (request.SnippetJson?.Length > MaxSnippetJsonChars)
                return new SnippetSaveResponse { Success = false, ErrorMessage = "Snippet JSON exceeds maximum allowed size (1 MB)." };

            var snippet = JsonSerializer.Deserialize<Snippet>(request.SnippetJson);
            if (snippet == null)
                return new SnippetSaveResponse { Success = false, ErrorMessage = "Invalid snippet JSON" };

            var personalSource = _sources.FirstOrDefault(s => s.Type == SnippetSourceType.Personal);
            if (personalSource == null)
                return new SnippetSaveResponse { Success = false, ErrorMessage = "No personal snippet folder configured" };

            snippet.Metadata.Modified = DateTime.UtcNow;
            if (request.IsNew)
                snippet.Metadata.Created = DateTime.UtcNow;

            _loader.SaveSnippet(snippet, personalSource.Path);
            ReloadIndex();
            return new SnippetSaveResponse { Success = true };
        }
        catch (Exception ex)
        {
            return new SnippetSaveResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public SnippetDeleteResponse HandleDelete(SnippetDeleteRequest request)
    {
        var entry = _index.GetById(request.SnippetId);
        if (entry == null)
            return new SnippetDeleteResponse { Success = false, ErrorMessage = "Snippet not found" };
        if (entry.Value.Source == SnippetSourceType.BuiltIn)
            return new SnippetDeleteResponse { Success = false, ErrorMessage = "Cannot delete built-in snippet" };

        var filePath = _index.GetFilePath(request.SnippetId);
        if (filePath == null)
            return new SnippetDeleteResponse { Success = false, ErrorMessage = "Snippet file path not tracked" };

        var deleted = _loader.DeleteSnippet(filePath);
        if (deleted) ReloadIndex();
        return new SnippetDeleteResponse { Success = deleted, ErrorMessage = deleted ? null : "Snippet file not found" };
    }

    public SnippetImportResponse HandleImport(SnippetImportRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.FileContent))
                return new SnippetImportResponse { Success = false, FailedCount = 1, FailedDetails = ["File content is empty"] };

            if (request.FileContent.Length > MaxSnippetJsonChars)
                return new SnippetImportResponse { Success = false, FailedCount = 1, FailedDetails = ["Import content exceeds maximum allowed size (1 MB)"] };

            var personalSource = _sources.FirstOrDefault(s => s.Type == SnippetSourceType.Personal);
            if (personalSource == null)
                return new SnippetImportResponse { Success = false, FailedCount = 1, FailedDetails = ["No personal snippet folder configured"] };

            var snippets = ParseImportContent(request.FileContent, request.SourceFormat);
            if (snippets == null)
                return new SnippetImportResponse { Success = false, FailedCount = 1, FailedDetails = [$"Failed to parse content as source format {request.SourceFormat}"] };

            var importedCount = 0;
            var failedDetails = new List<string>();
            var importedIds = new List<string>();

            foreach (var snippet in snippets)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(snippet.Metadata.Shortcode))
                    {
                        var name = string.IsNullOrWhiteSpace(snippet.Metadata.Name) ? "(unnamed)" : snippet.Metadata.Name;
                        failedDetails.Add($"Snippet '{name}' has no shortcode");
                        continue;
                    }

                    snippet.Metadata.Modified = DateTime.UtcNow;
                    if (string.IsNullOrWhiteSpace(snippet.Metadata.Id))
                        snippet.Metadata.Id = Guid.NewGuid().ToString();

                    _loader.SaveSnippet(snippet, personalSource.Path);
                    importedIds.Add(snippet.Metadata.Id);
                    importedCount++;
                }
                catch (Exception ex)
                {
                    failedDetails.Add($"Failed to save snippet '{snippet.Metadata.Shortcode}': {ex.Message}");
                }
            }

            if (importedCount > 0)
                ReloadIndex();

            return new SnippetImportResponse
            {
                Success = importedCount > 0,
                ImportedCount = importedCount,
                FailedCount = failedDetails.Count,
                FailedDetails = failedDetails.ToArray(),
                SnippetIds = importedIds.ToArray()
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during snippet import");
            return new SnippetImportResponse { Success = false, FailedCount = 1, FailedDetails = [ex.Message] };
        }
    }

    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static List<Snippet>? ParseImportContent(string content, int sourceFormat)
    {
        // SourceFormat: 0=Auto, 1=SqlPromptXml, 4=AkmlSnippet
        if (sourceFormat == 4 || sourceFormat == 0)
        {
            var result = TryParseAkmlSnippet(content);
            if (result != null)
                return result;
        }

        // .sqlpromptsnippet (SQL Prompt XML) — T042/T043, FR-032. In Auto mode this runs AFTER the
        // AkmlSnippet (JSON) probe, which throws JsonException on XML and returns null, so we reach here.
        if (sourceFormat == 1 || sourceFormat == 0)
        {
            var result = SqlPromptSnippetParser.ParseXml(content);
            if (result.Count > 0)
                return result;
        }

        if (sourceFormat == 0)
        {
            Log.Warning("Auto-detect: no supported format matched the import content");
        }
        else if (sourceFormat != 4 && sourceFormat != 1)
        {
            Log.Warning("Source format {Format} is not yet supported for import", sourceFormat);
        }

        return null;
    }

    private static List<Snippet>? TryParseAkmlSnippet(string content)
    {
        var trimmed = content.TrimStart();

        try
        {
            if (trimmed.StartsWith("["))
            {
                var array = JsonSerializer.Deserialize<Snippet[]>(content, ImportJsonOptions);
                return array?.ToList();
            }

            var single = JsonSerializer.Deserialize<Snippet>(content, ImportJsonOptions);
            return single != null ? [single] : null;
        }
        catch (JsonException ex)
        {
            Log.Debug(ex, "Failed to parse content as AkmlSnippet format");
            return null;
        }
    }
}
