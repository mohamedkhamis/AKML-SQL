using System.Text.RegularExpressions;
using Xunit;

namespace AkmlSql.Site.Tests;

/// <summary>
/// Guards against CSS mistakes that are legal syntax and fail silently.
/// <para>
/// The one that prompted this: <c>--s-section</c> and <c>--s-block</c> were each defined as a
/// var() reference to themselves. CSS resolves a self-referential custom property to the
/// guaranteed-invalid value, so every rule using them computed to NO margin — including
/// <c>.page-body &gt; section + section</c>, the vertical rhythm between every section on every
/// page. The whole site read as cramped and it looked like a dozen unrelated spacing bugs.
/// Nothing flagged it: it is valid syntax, the build is happy, and the browser says nothing.
/// </para>
/// </summary>
public sealed class SiteCssSanityTests
{
    private static string CssPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "AkmlSql.Site", "wwwroot", "css", "site.css");
    }

    /// <summary>
    /// The stylesheet with comments removed.
    /// <para>
    /// Stripping is not incidental. The comment explaining the self-reference bug necessarily
    /// quotes the broken declaration, and the first version of this test flagged that prose as the
    /// bug — so a scan of the raw file would make it impossible to document the trap without
    /// failing the build for describing it.
    /// </para>
    /// </summary>
    private static string Css() =>
        Regex.Replace(File.ReadAllText(CssPath()), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    [Fact]
    public void NoCustomPropertyIsDefinedAsItself()
    {
        var offenders = new List<string>();

        // --name: var(--name)  -- with or without a fallback.
        foreach (Match m in Regex.Matches(Css(), @"(--[\w-]+)\s*:\s*var\(\s*\1\s*[,)]"))
        {
            offenders.Add(m.Groups[1].Value);
        }

        Assert.True(
            offenders.Count == 0,
            "Self-referential custom properties resolve to the guaranteed-invalid value and silently " +
            "produce no style at all: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheSpacingScaleResolvesToRealLengths()
    {
        var css = Css();

        // Every --s-* the site's rhythm depends on must be a length, not a reference that could
        // dangle. Checked by value, so a future var() indirection is caught too.
        string[] names = ["--s-1", "--s-2", "--s-3", "--s-4", "--s-5", "--s-6", "--s-7", "--s-8", "--s-block", "--s-section"];

        foreach (var name in names)
        {
            var match = Regex.Match(css, Regex.Escape(name) + @"\s*:\s*([^;]+);");
            Assert.True(match.Success, $"{name} is not defined in site.css.");

            var value = match.Groups[1].Value.Trim();
            Assert.True(
                Regex.IsMatch(value, @"^(clamp\(|calc\()?\s*[\d.]"),
                $"{name} resolves to '{value}', which is not a length.");
        }
    }

    [Fact]
    public void EveryVarReferenceHasADefinitionOrAFallback()
    {
        var css = Css();

        // Properties site.css defines itself. Theme tokens (--akml-*) live in the generated theme
        // files and are excluded; this checks only the ones this stylesheet owns.
        var defined = Regex.Matches(css, @"(--[\w-]+)\s*:")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(css, @"var\(\s*(--[\w-]+)\s*([,)])"))
        {
            var name = m.Groups[1].Value;
            var hasFallback = m.Groups[2].Value == ",";

            if (name.StartsWith("--akml-", StringComparison.Ordinal) || hasFallback || defined.Contains(name))
            {
                continue;
            }

            missing.Add(name);
        }

        Assert.True(missing.Count == 0, "Undefined custom properties referenced: " + string.Join(", ", missing));
    }
}
