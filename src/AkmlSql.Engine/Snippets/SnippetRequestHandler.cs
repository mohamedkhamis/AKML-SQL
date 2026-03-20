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
        var cursorOffset = PlaceholderParser.FindCursorOffset(expandedText);
        expandedText = expandedText.Replace("$CURSOR$", "", StringComparison.OrdinalIgnoreCase);

        // Adjust cursor offset for removed $CURSOR$ marker
        if (cursorOffset < 0) cursorOffset = expandedText.Length;

        var placeholders = PlaceholderParser.Parse(expandedText, snippet.Variables);

        return new SnippetExpandResponse
        {
            Success = true,
            ExpandedText = expandedText,
            Placeholders = placeholders.ToArray(),
            CursorOffset = cursorOffset,
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
        // TODO: Implement in Phase 12 (US10)
        return new SnippetImportResponse { Success = false, FailedCount = 1, FailedDetails = ["Import not yet implemented"] };
    }
}
