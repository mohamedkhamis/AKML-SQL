using System.Text;
using AkmlSql.PostgreSql.Reference;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Reference;

/// <summary>Spec 042 (task T092, T094) - the embedded reference loads, passes its own self-check, and answers the lookups completion and quick info need.</summary>
[Trait("Category", "Reference")]
public class ReferenceLoadTests
{
    private static PgReference Reference => PgReference.Instance;

    [Fact]
    public void The_embedded_reference_loads_and_says_which_releases_it_covers()
    {
        Assert.Equal("18", Reference.Version);
        Assert.Equal(new[] { 14, 15, 16, 17, 18 }, Reference.SupportedMajors.ToArray());
        Assert.True(Reference.Entries.Count > 1500, "the reference should carry the whole manual, not a stub");
    }

    [Fact]
    public void Every_kind_is_present()
    {
        foreach (var kind in Enum.GetValues<PgEntryKind>().Where(k => k != PgEntryKind.Procedure))   // the manual documents no procedure other than by CALL
            Assert.Contains(Reference.Entries, e => e.Kind == kind);
    }

    [Fact]
    public void Keywords_are_found_in_any_case_with_their_category()
    {
        Assert.Equal("reserved", Reference.Keyword("select")!.Category);
        Assert.Equal("SELECT", Reference.Keyword("SeLeCt")!.Name);
        Assert.Equal("reserved", Reference.Keyword("lateral")!.Category);
        Assert.Equal("unreserved", Reference.Keyword("abort")!.Category);
        Assert.Null(Reference.Keyword("nolock"));    // a SQL Server hint, not a PostgreSQL key word
    }

    [Fact]
    public void A_command_has_a_synopsis_and_a_link_to_its_page()
    {
        var create = Reference.Command("create   table");

        Assert.NotNull(create);
        Assert.Equal("CREATE TABLE", create!.Name);
        Assert.StartsWith("CREATE ", create.Synopsis);
        Assert.Equal("https://www.postgresql.org/docs/18/sql-createtable.html", create.Url);
        Assert.Contains("table", create.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Commands_are_found_under_their_first_word_too()
    {
        var create = Reference.CommandsStartingWith("create").Select(c => c.Name).ToList();

        Assert.Contains("CREATE TABLE", create);
        Assert.Contains("CREATE INDEX", create);
        Assert.Contains("CREATE FUNCTION", create);
        Assert.True(create.Count > 30);
        Assert.Empty(Reference.CommandsStartingWith("nonsense"));
    }

    [Fact]
    public void Commands_that_share_a_page_are_each_found()
    {
        Assert.Equal("sql-select", Reference.Command("SELECT")!.SectionId);
        Assert.Equal("sql-select", Reference.Command("TABLE")!.SectionId);
        Assert.Equal("sql-select", Reference.Command("WITH")!.SectionId);
    }

    [Fact]
    public void An_aggregate_keeps_all_its_overloads_in_one_entry()
    {
        var agg = Assert.Single(Reference.Callables("string_agg"));

        Assert.Equal(PgEntryKind.Aggregate, agg.Kind);
        Assert.Equal(2, agg.Signatures.Count);
        Assert.Contains(agg.Signatures, s => s.Returns == "bytea");
        Assert.Contains(agg.Signatures, s => s.Returns == "text" && s.ArgumentText == "(value text, delimiter text)");
    }

    [Fact]
    public void Window_functions_and_set_returning_functions_are_marked()
    {
        Assert.Equal(PgEntryKind.Window, Assert.Single(Reference.Callables("row_number")).Kind);
        Assert.True(Assert.Single(Reference.Callables("generate_series")).Signatures.All(s => s.IsSetReturning));
        Assert.Equal(PgEntryKind.Function, Assert.Single(Reference.Callables("now")).Kind);
    }

    [Fact]
    public void Types_are_shown_with_sql_names_and_not_catalog_names()
    {
        var trunc = Assert.Single(Reference.Callables("date_trunc"));

        Assert.All(trunc.Signatures, s => Assert.DoesNotContain("timestamptz", s.ArgumentText + s.Returns));
        Assert.Contains(trunc.Signatures, s => s.Returns == "timestamp with time zone");
    }

    [Fact]
    public void Functions_written_as_syntax_are_found()
    {
        foreach (var name in new[] { "coalesce", "nullif", "greatest", "least", "pg_sleep", "extract", "json_exists" })
            Assert.NotEmpty(Reference.Callables(name));
    }

    [Fact]
    public void Operators_are_found_by_their_symbol()
    {
        var arrow = Reference.Operator("->>");

        Assert.NotNull(arrow);
        Assert.Contains(arrow!.Signatures, s => s.Args.Select(a => a.Type).SequenceEqual(new[] { "jsonb", "text" }) && s.Returns == "text");
        Assert.NotNull(Reference.Operator("@>"));
        Assert.NotNull(Reference.Operator("||"));
    }

    [Fact]
    public void A_type_is_found_by_its_name_or_any_alias()
    {
        var integer = Reference.Type("integer");

        Assert.NotNull(integer);
        Assert.Same(integer, Reference.Type("int4"));
        Assert.Same(integer, Reference.Type("INT"));
        Assert.Same(Reference.Type("timestamp with time zone"), Reference.Type("timestamptz"));
        Assert.Same(Reference.Type("character varying"), Reference.Type("varchar"));
        Assert.Equal("datatype-numeric", integer!.SectionId);
    }

    [Fact]
    public void Error_codes_are_found_by_code_and_by_condition_name()
    {
        var unique = Reference.SqlState("23505");

        Assert.NotNull(unique);
        Assert.Contains("unique_violation", unique!.Aliases);
        Assert.Same(unique, Reference.SqlStateByCondition("UNIQUE_VIOLATION"));
        Assert.Equal("Class 23 - Integrity Constraint Violation", unique.Category);
        Assert.Equal("undefined_table", Assert.Single(Reference.SqlState("42P01")!.Aliases));
    }

    [Fact]
    public void The_lexical_forms_the_language_needs_are_there()
    {
        var names = Reference.Lexical.Select(e => e.Name).ToList();

        foreach (var form in new[] { "::", "$$", "$n", "E''", "U&''", "--", "ARRAY[]", "identifier" })
            Assert.Contains(form, names);
    }

    [Fact]
    public void A_server_gets_only_what_its_release_has()
    {
        var v14 = Reference.For(14);
        var v18 = Reference.For(18);

        Assert.NotNull(v14.Command("SELECT"));
        Assert.Null(v14.Command("MERGE"));                       // since 15
        Assert.NotNull(v18.Command("MERGE"));
        Assert.Null(v14.Keyword("json_table"));                  // since 17
        Assert.NotNull(v18.Keyword("json_table"));
        Assert.Empty(v14.Callables("json_exists"));              // since 17
        Assert.NotEmpty(v18.Callables("json_exists"));
        Assert.True(v14.Entries.Count < v18.Entries.Count);
        Assert.All(v14.Entries, e => Assert.True(e.MinVersion <= 14));
        Assert.Equal(Reference.Entries.Count, v18.Entries.Count);
    }

    [Fact]
    public void A_release_newer_than_the_reference_gets_everything_and_the_view_is_cached()
    {
        Assert.Equal(Reference.Entries.Count, Reference.For(19).Entries.Count);
        Assert.Same(Reference.For(15), Reference.For(15));
    }

    [Fact]
    public void Nothing_that_is_only_in_the_t_sql_dictionaries_is_offered_as_a_postgresql_function()
    {
        // SC-004: the two languages do not leak into each other.
        foreach (var name in new[] { "getdate", "isnull", "datediff", "len", "charindex", "newid", "scope_identity", "dateadd", "iif", "stuff" })
            Assert.Empty(Reference.Callables(name));
    }

    // ── The self-check on load ──────────────────────────────────────────────────────────────────

    private static string Json(string entries, string version = "18", string tags = "\"REL_14_STABLE\",\"REL_18_STABLE\"") =>
        "{\"version\":\"" + version + "\",\"generatedFrom\":{\"tags\":[" + tags + "],\"generator\":\"1.0.0\"},\"entries\":[" + entries + "]}";

    private static string Entry(string kind = "keyword", string name = "FOO", string summary = "A summary.", int minVersion = 14, string section = "sql-keywords-appendix", string? url = null) =>
        "{\"kind\":\"" + kind + "\",\"name\":\"" + name + "\",\"aliases\":[],\"category\":\"reserved\",\"summary\":\"" + summary + "\",\"synopsis\":null,\"signatures\":null,\"minVersion\":" + minVersion +
        ",\"source\":{\"file\":\"x\",\"sectionId\":\"" + section + "\"},\"url\":\"" + (url ?? "https://www.postgresql.org/docs/18/" + section + ".html") + "\"}";

    private static PgReference Load(string json) => PgReference.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void A_valid_file_loads()
    {
        var reference = Load(Json(Entry()));

        Assert.Equal("FOO", reference.Keyword("foo")!.Name);
        Assert.Equal(new[] { 14, 18 }, reference.SupportedMajors.ToArray());
    }

    [Theory]
    [InlineData("duplicate", "not unique")]
    [InlineData("summary", "no summary")]
    [InlineData("section", "no section")]
    [InlineData("url", "url does not follow")]
    [InlineData("minVersion", "not a supported release")]
    public void A_file_that_breaks_the_contract_is_refused_and_says_why(string problem, string expected)
    {
        var entries = problem switch
        {
            "duplicate" => Entry(name: "FOO") + "," + Entry(name: "foo"),
            "summary" => Entry(summary: "  "),
            "section" => Entry(section: ""),
            "url" => Entry(url: "https://example.com/docs/foo.html"),
            _ => Entry(minVersion: 13),
        };

        var error = Assert.Throws<InvalidDataException>(() => Load(Json(entries)));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void The_same_name_may_appear_under_two_kinds()
    {
        var reference = Load(Json(Entry(kind: "keyword", name: "COALESCE") + "," + Entry(kind: "function", name: "coalesce")));

        Assert.NotNull(reference.Keyword("coalesce"));
        Assert.Single(reference.Callables("COALESCE"));
    }

    [Fact]
    public void An_unknown_kind_or_an_empty_file_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => Load(Json(Entry(kind: "gadget"))));
        Assert.Throws<InvalidDataException>(() => Load(Json(string.Empty)));
        Assert.Throws<InvalidDataException>(() => Load(Json(Entry(), tags: string.Empty)));
    }
}
