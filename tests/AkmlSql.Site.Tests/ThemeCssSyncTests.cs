using Xunit;

namespace AkmlSql.Site.Tests;

/// <summary>
/// UI-002 guard: <c>scripts/generate-theme-css.ps1</c> writes to
/// <c>src/AkmlSql.Web/wwwroot/css/themes</c> by default, and the site's copies are produced by a
/// SECOND invocation with <c>-OutputFolder</c>. Running only the default one leaves the site on a
/// stale palette, and nothing reports it — the light theme's collapsed elevation survived exactly
/// that way. The two sets are generated from the same tokens, so they must be identical.
/// </summary>
public sealed class ThemeCssSyncTests
{
    [Theory]
    [InlineData("light.css")]
    [InlineData("dark.css")]
    [InlineData("high-contrast.css")]
    public void SiteAndWebThemeFilesAreIdentical(string fileName)
    {
        var root = RepositoryRoot();
        var site = Path.Combine(root, "src", "AkmlSql.Site", "wwwroot", "css", "themes", fileName);
        var web = Path.Combine(root, "src", "AkmlSql.Web", "wwwroot", "css", "themes", fileName);

        Assert.True(File.Exists(site), $"Missing site theme file: {site}");
        Assert.True(File.Exists(web), $"Missing web theme file: {web}");

        // Compared as normalised text, not bytes: the two are written by separate runs of the
        // generator, so a line-ending or BOM difference is noise, a token difference is not.
        var siteText = Normalize(File.ReadAllText(site));
        var webText = Normalize(File.ReadAllText(web));

        Assert.True(
            siteText == webText,
            $"{fileName} differs between AkmlSql.Site and AkmlSql.Web — regenerate BOTH:\n"
            + "  scripts/generate-theme-css.ps1 -RepoRoot .\n"
            + "  scripts/generate-theme-css.ps1 -RepoRoot . -OutputFolder src/AkmlSql.Site/wwwroot/css/themes\n"
            + FirstDifference(siteText, webText));
    }

    private static string Normalize(string text) =>
        text.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    /// <summary>Names the first differing line so a failure points at the token, not the file.</summary>
    private static string FirstDifference(string a, string b)
    {
        var left = a.Split('\n');
        var right = b.Split('\n');

        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return $"  first difference at line {i + 1}:\n    site: {left[i].Trim()}\n    web:  {right[i].Trim()}";
            }
        }

        return $"  files differ in length ({left.Length} vs {right.Length} lines)";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "AkmlSql.Site", "AkmlSql.Site.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
