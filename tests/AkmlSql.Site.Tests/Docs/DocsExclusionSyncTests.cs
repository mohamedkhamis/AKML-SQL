using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace AkmlSql.Site.Tests.Docs;

/// <summary>
/// DOC-001 guard: the docs exclusion list exists in two places that must agree —
/// <c>appsettings.json</c> <c>Docs:Exclusions</c> (read at runtime by <c>DocsCatalog</c>, and also
/// by <c>scripts/generate-docs-metadata.ps1</c>, so that script is not a third copy) and the
/// <c>DocsSource Remove</c> items in the csproj (which decide what is copied into the build at
/// all). Drift between them is silent and asymmetric: a file removed from only the csproj vanishes
/// with no exclusion recorded, while one excluded only in appsettings is still shipped into the
/// output folder. These tests make that drift a build failure instead.
/// </summary>
public sealed class DocsExclusionSyncTests
{
    private static readonly string ProjectDirectory = LocateProjectDirectory();

    [Fact]
    public void EveryAppsettingsExclusionHasAMatchingCsprojRemove()
    {
        var missing = AppsettingsExclusions()
            .Where(exclusion => !CsprojRemoves().Contains(exclusion))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "In appsettings.json Docs:Exclusions but not removed by the csproj DocsSource globs — "
            + "these files are still copied into the build output: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryCsprojRemoveHasAMatchingAppsettingsExclusion()
    {
        var missing = CsprojRemoves()
            .Where(remove => !AppsettingsExclusions().Contains(remove))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Removed by a csproj DocsSource glob but absent from appsettings.json Docs:Exclusions — "
            + "the runtime catalog and the metadata generator do not know these are excluded: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void ContributorDocsAreExcludedFromThePublicSite()
    {
        // DOC-001: named explicitly so re-including one is a deliberate act, not an accident.
        string[] contributorDocs =
            ["architecture.md", "ipc-api.md", "deployment.md", "m3-security.md", "use-cases.md"];

        var exclusions = AppsettingsExclusions();
        var published = contributorDocs.Where(d => !exclusions.Contains(d)).ToList();

        Assert.True(
            published.Count == 0,
            "Contributor documentation would be published as public product docs: " + string.Join(", ", published));
    }

    /// <summary>Exclusion entries from <c>appsettings.json</c>, normalised for comparison.</summary>
    private static HashSet<string> AppsettingsExclusions()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ProjectDirectory, "appsettings.json")));

        var entries = document.RootElement
            .GetProperty("Docs")
            .GetProperty("Exclusions")
            .EnumerateArray()
            .Select(e => e.GetString() ?? "");

        return new HashSet<string>(entries.Select(Normalize), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>DocsSource Remove</c> patterns from the csproj, reduced to the same shape as an
    /// appsettings entry: the leading <c>../../doc/</c> and any <c>**/</c> recursion marker are
    /// dropped, so <c>../../doc/**/bugs.md</c> and <c>../../doc/WEB/**</c> compare as
    /// <c>bugs.md</c> and <c>WEB/</c>.
    /// </summary>
    private static HashSet<string> CsprojRemoves()
    {
        var csproj = File.ReadAllText(Path.Combine(ProjectDirectory, "AkmlSql.Site.csproj"));

        // Only DocsSource removes — DocsAsset has its own (image) list with different semantics.
        var patterns = Regex.Matches(csproj, @"<DocsSource\s+Remove=""(?<pattern>[^""]+)""")
            .Select(m => m.Groups["pattern"].Value)
            .Select(p => p.Replace("../../doc/", "", StringComparison.Ordinal))
            .Select(p => p.Replace("**/", "", StringComparison.Ordinal))
            .Select(p => p.EndsWith("/**", StringComparison.Ordinal) ? p[..^2] : p);

        return new HashSet<string>(patterns.Select(Normalize), StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) => value.Trim().Replace('\\', '/');

    /// <summary>Walks up from the test binaries to the site project directory.</summary>
    private static string LocateProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "AkmlSql.Site");
            if (File.Exists(Path.Combine(candidate, "AkmlSql.Site.csproj")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate src/AkmlSql.Site from " + AppContext.BaseDirectory);
    }
}
