using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>
/// Moves styles between SQL Prompt's model (<see cref="SqlPromptStyleDocument"/>) and the stored
/// <c>.akmlstyle</c> (<see cref="FormattingProfile"/>). One place for the rules, shared by the
/// engine (SSMS / Visual Studio) and the web edition, so a style saved by one reads the same in
/// the other.
/// </summary>
public static class SqlPromptStyles
{
    /// <summary>True when the style is written in SQL Prompt's model and formats with its layout.</summary>
    public static bool IsSqlPromptStyle(FormattingProfile profile) => profile.SqlPrompt is not null;

    /// <summary>
    /// The stored style for a SQL Prompt document. The document is kept whole (it is the style);
    /// the AKML option groups are filled with the closest projection so a build that predates SQL
    /// Prompt styles still formats sensibly. Name and id come from the document;
    /// <paramref name="previous"/> (the style being overwritten, if any) keeps its description,
    /// author, creation date and format-on-save actions.
    /// </summary>
    public static FormattingProfile ToProfile(SqlPromptStyleDocument document, FormattingProfile? previous = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var projected = RedgateJsonStyleImporter.Import(document.ToJson(), document.Name).Profile;
        projected.SqlPrompt = (System.Text.Json.Nodes.JsonObject)document.Root.DeepClone();

        var metadata = projected.Metadata;
        metadata.Name = string.IsNullOrWhiteSpace(document.Name) ? metadata.Name : document.Name;
        if (!string.IsNullOrWhiteSpace(document.Id)) metadata.Id = document.Id;
        metadata.IsBuiltIn = false;
        metadata.Modified = DateTime.UtcNow;
        if (previous is not null)
        {
            metadata.Description = previous.Metadata.Description;
            metadata.Author = previous.Metadata.Author;
            metadata.Created = previous.Metadata.Created;
            metadata.BasedOn = previous.Metadata.BasedOn;
            projected.FormatActions = previous.FormatActions;
            // Keys a newer build wrote at the root of the .akmlstyle stay with the style.
            projected.ExtensionData = previous.ExtensionData;
        }
        else
        {
            metadata.Description = "SQL Prompt style";
            metadata.BasedOn = "SQL Prompt";
            metadata.Created = DateTime.UtcNow;
        }
        return projected;
    }

    /// <summary>
    /// The SQL Prompt document for any stored style: its own document when it has one; otherwise
    /// the closest SQL Prompt equivalent of an AKML-model style (what the style editors show before
    /// such a style is first saved as a SQL Prompt style). <paramref name="importedSource"/> is the
    /// verbatim SQL Prompt file a style was imported from, when kept — exact, so preferred.
    /// </summary>
    public static SqlPromptStyleDocument ToDocument(FormattingProfile profile, string? importedSource = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        SqlPromptStyleDocument document;
        if (profile.SqlPrompt is not null)
            document = SqlPromptStyleDocument.FromNode(profile.SqlPrompt);
        else if (importedSource is not null && SqlPromptStyleDocument.TryParse(importedSource, out var source, out _))
            document = source!;
        else
            document = SqlPromptProjection.Project(profile);

        document.Name = profile.Metadata.Name;
        if (string.IsNullOrWhiteSpace(document.Id)) document.Id = profile.Metadata.Id;
        return document;
    }

    /// <summary>What an import of <paramref name="document"/> did with each key it contained.</summary>
    public static IReadOnlyList<RedgateOptionReport> ImportReport(SqlPromptStyleDocument document)
    {
        var reports = new List<RedgateOptionReport>();
        foreach (var option in SqlPromptOptionCatalog.Options)
        {
            if (!document.IsSet(option.Path)) continue;
            reports.Add(new RedgateOptionReport(option.Path, document.Get(option.Path), RedgateOptionStatus.Mapped, null));
        }
        foreach (var key in document.UnknownKeys())
        {
            reports.Add(new RedgateOptionReport(key, "", RedgateOptionStatus.Unknown,
                "Not a SQL Prompt option this version knows; kept in the style unchanged."));
        }
        return reports;
    }
}
