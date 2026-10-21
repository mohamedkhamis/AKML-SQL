using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkmlSql.PostgreSql.Reference;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Reference;

/// <summary>
/// Spec 042 (task T092, SC-006) - how much of the manual the reference carries, measured against the counts the generator
/// wrote beside it, and the invariants every entry must keep.
/// </summary>
[Trait("Category", "Reference")]
public class ReferenceCoverageTests
{
    private static JsonElement Coverage()
    {
        var assembly = typeof(PgReference).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("coverage.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonDocument.Parse(stream).RootElement.Clone();
    }

    private static (int Documented, int Included) Count(string kind)
    {
        var c = Coverage().GetProperty(kind);
        return (c.GetProperty("documented").GetInt32(), c.GetProperty("included").GetInt32());
    }

    [Theory]
    [InlineData("commands")]
    [InlineData("types")]
    [InlineData("keywords")]
    [InlineData("sqlState")]
    public void Every_command_type_keyword_and_error_code_in_the_manual_is_in_the_reference(string kind)
    {
        var (documented, included) = Count(kind);

        Assert.True(documented > 0, kind + ": the generator found nothing to measure against");
        Assert.Equal(documented, included);
    }

    [Theory]
    [InlineData("functions")]
    [InlineData("operators")]
    public void At_least_95_percent_of_the_functions_and_operators_in_the_manual_are_in_the_reference(string kind)
    {
        var (documented, included) = Count(kind);

        Assert.True(documented > 50, kind + ": the generator found suspiciously little in the manual (" + documented + ")");
        Assert.True(included >= documented * 0.95, $"{kind}: {included} of {documented}");
    }

    [Fact]
    public void The_counts_match_the_entries_actually_shipped()
    {
        var entries = PgReference.Instance.Entries;

        Assert.Equal(Count("commands").Included, entries.Count(e => e.Kind == PgEntryKind.Command));
        Assert.Equal(Count("keywords").Included, entries.Count(e => e.Kind == PgEntryKind.Keyword));
        Assert.Equal(Count("types").Included, entries.Count(e => e.Kind == PgEntryKind.Type));
        Assert.Equal(Count("operators").Included, entries.Count(e => e.Kind == PgEntryKind.Operator));
        Assert.Equal(Count("sqlState").Included, entries.Count(e => e.Kind == PgEntryKind.SqlState));
        Assert.Equal(Count("functions").Included, entries.Count(e => e.IsCallable));
    }

    [Fact]
    public void Every_entry_has_a_summary_a_section_and_a_link_to_the_manual()
    {
        Assert.All(PgReference.Instance.Entries, e =>
        {
            var label = e.Kind + " " + e.Name;
            Assert.False(string.IsNullOrWhiteSpace(e.Summary), label + ": summary");
            Assert.False(string.IsNullOrWhiteSpace(e.SectionId), label + ": section");
            Assert.Matches(new Regex(@"^https://www\.postgresql\.org/docs/18/[a-z0-9\-]+\.html$"), e.Url);
            Assert.EndsWith("/" + e.SectionId + ".html", e.Url);
        });
    }

    [Fact]
    public void Names_are_unique_per_kind()
    {
        var duplicates = PgReference.Instance.Entries
            .GroupBy(e => (e.Kind, Name: e.Name.ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Kind + " " + g.Key.Name)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_min_version_is_one_of_the_supported_releases()
    {
        var supported = PgReference.Instance.SupportedMajors;

        Assert.All(PgReference.Instance.Entries, e => Assert.Contains(e.MinVersion, supported));
    }

    [Fact]
    public void Entries_that_arrived_in_a_later_release_are_dated_to_it()
    {
        var reference = PgReference.Instance;

        Assert.Equal(15, reference.Command("MERGE")!.MinVersion);
        Assert.Equal(14, reference.Command("SELECT")!.MinVersion);
        Assert.True(reference.Keyword("JSON_TABLE")!.MinVersion >= 17);
        Assert.Equal(14, reference.Keyword("SELECT")!.MinVersion);
        Assert.Equal(14, reference.Type("integer")!.MinVersion);
    }

    [Fact]
    public void Nothing_is_left_unresolved_in_the_omissions()
    {
        var assembly = typeof(PgReference).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("omissions.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;

        var omissions = JsonDocument.Parse(stream).RootElement.EnumerateArray().ToList();

        Assert.All(omissions, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.GetProperty("reason").GetString()));
            Assert.NotEqual("unresolved", o.GetProperty("reason").GetString());
        });
    }

    [Fact]
    public void The_reference_ships_inside_the_assembly()
    {
        var names = typeof(PgReference).Assembly.GetManifestResourceNames();

        Assert.Contains(names, n => n.EndsWith("postgresql-reference.json", StringComparison.Ordinal));
        Assert.Contains(names, n => n.EndsWith("coverage.json", StringComparison.Ordinal));
        Assert.Contains(names, n => n.EndsWith("omissions.json", StringComparison.Ordinal));
    }
}
