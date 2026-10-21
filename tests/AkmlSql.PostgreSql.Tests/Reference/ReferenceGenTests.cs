using System.Text.Json;
using System.Text.RegularExpressions;
using AkmlSql.PostgreSql.ReferenceGen;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Reference;

/// <summary>
/// Spec 042 (task T090) - the reference generator: every reader over an excerpt of the real PostgreSQL source file it reads,
/// the version differ, and the writer's output against contracts/reference-schema.md.
/// </summary>
[Trait("Category", "Reference")]
public class ReferenceGenTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reference", "Fixtures", name));

    // ── kwlist.h ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Kwlist_gives_name_category_and_bare_label()
    {
        var keywords = KwlistReader.Read(Fixture("kwlist.h")).ToDictionary(k => k.Name);

        Assert.Equal(6, keywords.Count);
        Assert.Equal("unreserved", keywords["abort"].Category);
        Assert.Equal("type-function-name", keywords["authorization"].Category);
        Assert.Equal("column-name", keywords["between"].Category);
        Assert.Equal("reserved", keywords["lateral"].Category);
        Assert.True(keywords["lateral"].BareLabel);
        Assert.False(keywords["select"].BareLabel);
    }

    // ── *.dat catalog files ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dat_reader_reads_every_record_and_skips_comments()
    {
        var records = DatFileReader.Read(Fixture("pg_proc.dat"));

        Assert.Equal(9, records.Count);
        Assert.Equal("now", records[0]["proname"]);
        Assert.False(records[0].ContainsKey("prokind"));   // absent keys are the catalog's defaults, not empty strings
    }

    [Fact]
    public void Dat_reader_keeps_braces_commas_and_escaped_quotes_inside_a_quoted_value()
    {
        var records = DatFileReader.Read(Fixture("pg_proc.dat"));

        var demo = records.Single(r => r["proname"] == "quoted_demo");
        Assert.Equal("it's {odd}, but fine", demo["descr"]);
        Assert.Equal("{value,delimiter}", records.First(r => r["proname"] == "string_agg")["proargnames"]);
    }

    [Fact]
    public void Functions_carry_kind_arguments_and_return_type()
    {
        var functions = TagSnapshot.ReadFunctions(Fixture("pg_proc.dat"));

        var now = functions.Single(f => f.Name == "now");
        Assert.Equal('f', now.Kind);
        Assert.Empty(now.Args);
        Assert.Equal("timestamptz", now.Returns);
        Assert.Equal("current transaction time", now.Description);

        var aggregates = functions.Where(f => f.Name == "string_agg").ToList();
        Assert.Equal(2, aggregates.Count);
        Assert.All(aggregates, a => Assert.Equal('a', a.Kind));
        var text = aggregates.Single(a => a.Returns == "text");
        Assert.Equal(new[] { "value", "delimiter" }, text.Args.Select(a => a.Name).ToArray());
        Assert.Equal(new[] { "text", "text" }, text.Args.Select(a => a.Type).ToArray());

        Assert.Equal('w', functions.Single(f => f.Name == "row_number").Kind);
        Assert.True(functions.Single(f => f.Name == "generate_series").SetOf);
    }

    [Fact]
    public void Output_parameters_come_from_the_all_arguments_list_with_their_modes()
    {
        var zones = TagSnapshot.ReadFunctions(Fixture("pg_proc.dat")).Single(f => f.Name == "pg_timezone_names");

        Assert.Equal(new[] { "text", "text", "interval", "bool" }, zones.Args.Select(a => a.Type).ToArray());
        Assert.Equal(new[] { "name", "abbrev", "utc_offset", "is_dst" }, zones.Args.Select(a => a.Name).ToArray());
        Assert.All(zones.Args, a => Assert.Equal("out", a.Mode));
    }

    [Fact]
    public void Operators_have_one_side_missing_when_they_are_unary()
    {
        var operators = TagSnapshot.ReadOperators(Fixture("pg_operator.dat"));

        Assert.Equal(4, operators.Count);
        var equal = operators.Single(o => o.Name == "=");
        Assert.Equal(("int4", "int4", "bool"), (equal.Left, equal.Right, equal.Result));
        var negate = operators.Single(o => o.Name == "-");
        Assert.Null(negate.Left);
        Assert.Equal("int8", negate.Right);
        Assert.Equal(2, operators.Count(o => o.Name == "||"));
    }

    [Fact]
    public void Types_default_to_base_types_and_carry_their_category()
    {
        var types = TagSnapshot.ReadTypes(Fixture("pg_type.dat")).ToDictionary(t => t.Name);

        Assert.Equal(('b', 'N'), (types["int4"].TypeType, types["int4"].Category));
        Assert.Equal('B', types["bool"].Category);
        Assert.Equal("boolean, 'true'/'false'", types["bool"].Description);
        Assert.Equal(('p', 'P'), (types["record"].TypeType, types["record"].Category));
    }

    // ── errcodes.txt ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Errcodes_give_code_condition_and_class()
    {
        var codes = ErrcodesReader.Read(Fixture("errcodes.txt")).ToDictionary(c => c.Code);

        Assert.Equal(7, codes.Count);
        var unique = codes["23505"];
        Assert.Equal(("unique_violation", "E", "23", "Integrity Constraint Violation"), (unique.Condition, unique.Severity, unique.ClassCode, unique.ClassName));
        Assert.Equal("S", codes["00000"].Severity);
        Assert.Equal("exclusion_violation", codes["23P01"].Condition);
    }

    [Fact]
    public void A_code_without_a_condition_name_gets_one_from_its_macro()
    {
        var made = ErrcodesReader.Read(Fixture("errcodes.txt")).Single(c => c.Code == "42XXX");

        Assert.Equal("made_up_code", made.Condition);
    }

    // ── doc/src/sgml/ref/*.sgml ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_command_page_gives_id_names_purpose_and_a_clean_synopsis()
    {
        var page = RefPageReader.Read(Fixture("select.sgml"));

        Assert.NotNull(page);
        Assert.Equal("sql-select", page!.Id);
        Assert.Equal(new[] { "SELECT", "TABLE", "WITH" }, page.Names.ToArray());
        Assert.Equal("retrieve rows from a table or view", page.Purpose);

        var lines = page.Synopsis.Split('\n');
        Assert.Equal("[ WITH [ RECURSIVE ] with_query [, ...] ]", lines[0]);
        Assert.StartsWith("SELECT [ ALL | DISTINCT", lines[1]);
        Assert.StartsWith("    [ FROM from_item [, ...] ]", page.Synopsis.Substring(page.Synopsis.IndexOf("    [ FROM", StringComparison.Ordinal)));
        Assert.Contains("where from_item can be one of:", page.Synopsis);
        Assert.DoesNotContain("<", page.Synopsis);
    }

    [Fact]
    public void A_file_without_a_refentry_is_not_a_page()
    {
        Assert.Null(RefPageReader.Read("<chapter id=\"x\"><title>Not a command</title></chapter>"));
    }

    // ── doc/src/sgml/func.sgml ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Function_rows_are_found_with_or_without_an_id_on_the_entry()
    {
        var rows = FuncSectionIndex.Read(Fixture("func.sgml")).Where(r => !r.IsOperator).ToDictionary(r => r.Name);

        Assert.Contains("pi", rows.Keys);     // <entry id="function-pi" role="func_table_entry">
        Assert.Contains("now", rows.Keys);    // <entry id=".." xreflabel=".." role="func_table_entry">
        Assert.Contains("date_trunc", rows.Keys);
    }

    [Fact]
    public void A_function_row_gives_section_arguments_return_type_and_the_first_sentence()
    {
        var rows = FuncSectionIndex.Read(Fixture("func.sgml")).Where(r => !r.IsOperator).ToDictionary(r => r.Name);

        var trunc = rows["date_trunc"];
        Assert.Equal("functions-datetime", trunc.SectionId);
        Assert.Equal("field text, source timestamp", trunc.Arguments);
        Assert.Equal("timestamp", trunc.Returns);
        Assert.Equal("Truncate to specified precision", trunc.Description);   // the "; see <xref/>" pointer is not text
        Assert.False(trunc.FromProse);

        Assert.Equal("functions-math", rows["pi"].SectionId);
        Assert.Equal("Approximate value of pi", rows["pi"].Description);
        Assert.Equal("timestamp with time zone", rows["now"].Returns);
    }

    [Fact]
    public void An_abbreviation_does_not_end_the_sentence()
    {
        var now = FuncSectionIndex.Read(Fixture("func.sgml")).Single(r => r.Name == "now");

        Assert.Equal("Current date and time (start of current transaction), e.g. for logging", now.Description);
    }

    [Fact]
    public void An_operator_row_is_read_from_its_literal()
    {
        var plus = FuncSectionIndex.Read(Fixture("func.sgml")).Single(r => r.IsOperator);

        Assert.Equal("+", plus.Name);
        Assert.Equal("numeric + numeric", plus.Signature);
        Assert.Equal("numeric", plus.Returns);
        Assert.Equal("Addition", plus.Description);
        Assert.Equal("functions-math", plus.SectionId);
    }

    [Fact]
    public void Functions_written_as_syntax_are_read_from_their_synopsis_and_the_prose_after_it()
    {
        var rows = FuncSectionIndex.Read(Fixture("func.sgml")).Where(r => r.FromProse).ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(new[] { "COALESCE", "GREATEST", "LEAST", "pg_sleep", "pg_sleep_for" }, rows.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal("value , ...", rows["coalesce"].Arguments);
        Assert.Equal("functions-conditional", rows["coalesce"].SectionId);
        Assert.Equal("The COALESCE function returns the first of its arguments that is not null", rows["coalesce"].Description);
        // The sentence chosen is the one about the function, not just the first one after the block.
        Assert.StartsWith("pg_sleep makes", rows["pg_sleep"].Description);
        Assert.StartsWith("pg_sleep_for is a convenience", rows["pg_sleep_for"].Description);
        // Two synopsis blocks in a row: the second block is not mistaken for the description of the first.
        Assert.StartsWith("The GREATEST and LEAST functions", rows["greatest"].Description);
    }

    [Fact]
    public void A_name_found_in_a_table_is_not_read_again_from_a_synopsis()
    {
        var text = Fixture("func.sgml") + "<synopsis>\ndate_trunc(field, source)\n</synopsis>";

        Assert.Single(FuncSectionIndex.Read(text), r => r.Name == "date_trunc");
    }

    [Fact]
    public void The_documented_names_come_from_a_plainer_scan_than_the_reader()
    {
        var (functions, operators) = FuncSectionIndex.DocumentedNames(Fixture("func.sgml"));

        Assert.Equal(11, functions.Count);
        Assert.Contains("PG_SLEEP", functions);   // case does not matter
        Assert.Equal(new[] { "+" }, operators.ToArray());
    }

    // ── doc/src/sgml/datatype.sgml ──────────────────────────────────────────────────────────────

    [Fact]
    public void Type_rows_drop_the_optional_length_and_keep_the_aliases()
    {
        var types = DatatypeSectionIndex.Read(Fixture("datatype.sgml")).ToDictionary(t => t.Name);

        Assert.Equal(new[] { "bigint", "bit", "bit varying", "integer", "record", "timestamp with time zone" }, types.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "varbit" }, types["bit varying"].Aliases.ToArray());
        Assert.Equal(new[] { "int", "int4" }, types["integer"].Aliases.ToArray());
        Assert.Equal("signed eight-byte integer", types["bigint"].Description);
        Assert.Empty(types["bit"].Aliases);
    }

    [Fact]
    public void A_type_is_linked_to_the_section_that_mentions_it_most()
    {
        var types = DatatypeSectionIndex.Read(Fixture("datatype.sgml")).ToDictionary(t => t.Name);

        Assert.Equal("datatype-numeric", types["bigint"].SectionId);
        Assert.Equal("datatype-numeric", types["integer"].SectionId);
        Assert.Equal("datatype-bit", types["bit"].SectionId);
        Assert.Equal("datatype-bit", types["bit varying"].SectionId);   // the overview table itself does not count
        Assert.Equal("datatype", types["timestamp with time zone"].SectionId);   // no section mentions it: the chapter
    }

    [Fact]
    public void Pseudo_types_come_from_their_own_table_and_page()
    {
        var record = DatatypeSectionIndex.Read(Fixture("datatype.sgml")).Single(t => t.Name == "record");

        Assert.True(record.IsPseudo);
        Assert.Equal("datatype-pseudo", record.SectionId);
        Assert.Equal("Identifies a function taking or returning an unspecified row type.", record.Description);
    }

    [Theory]
    [InlineData("bit [ (<replaceable>n</replaceable>) ]", "bit")]
    [InlineData("timestamp [ (<replaceable>p</replaceable>) ] with time zone", "timestamp with time zone")]
    [InlineData("character varying [ (<replaceable>n</replaceable>) ]", "character varying")]
    [InlineData("numeric [ (<replaceable>p</replaceable>, <replaceable>s</replaceable>) ]", "numeric")]
    [InlineData("integer", "integer")]
    public void Optional_parts_of_a_type_name_are_removed(string written, string expected) =>
        Assert.Equal(expected, DatatypeSectionIndex.Normalise(written));

    // ── SgmlText ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Truncate to specified precision; see <xref linkend=\"a\"/>.", "Truncate to specified precision")]
    [InlineData("Does something (see <xref linkend=\"a\"/>) to a row.", "Does something to a row")]
    [InlineData("Does something, see also <xref linkend=\"a\"/> and <xref linkend=\"b\"/>.", "Does something")]
    [InlineData("Is described in <xref linkend=\"a\"/>.", "Is described")]
    [InlineData("Plain sentence without a pointer.", "Plain sentence without a pointer")]
    public void A_pointer_to_another_page_is_not_part_of_a_summary(string sgml, string expected) =>
        Assert.Equal(expected, SgmlText.FirstSentence(sgml));

    [Fact]
    public void Entities_are_decoded_and_tags_removed()
    {
        Assert.Equal("a < b & c > d - e", SgmlText.Plain("a &lt; b &amp; c &gt; d &mdash; <emphasis>e</emphasis>"));
        Assert.Equal("fold", SgmlText.Plain("<![CDATA[fold]]>"));
    }

    [Fact]
    public void A_question_mark_ends_a_sentence_and_stays_in_it()
    {
        Assert.Equal("Is first object contained in second?", SgmlText.FirstSentence("Is first object contained in second? Available for these types."));
    }

    [Fact]
    public void A_synopsis_keeps_its_lines_and_loses_the_common_indent()
    {
        var block = SgmlText.Block("\n    A\n      B\n    C\n");

        Assert.Equal("A\n  B\nC", block);
    }

    // ── VersionDiffer ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_first_release_that_has_an_entry_is_its_min_version()
    {
        var keys = new Dictionary<int, ISet<string>>
        {
            [14] = new HashSet<string> { VersionDiffer.Key("kw", "abort") },
            [15] = new HashSet<string> { VersionDiffer.Key("kw", "abort"), VersionDiffer.Key("kw", "merge") },
            [16] = new HashSet<string> { VersionDiffer.Key("kw", "abort"), VersionDiffer.Key("kw", "merge") },
        };

        Assert.Equal(14, VersionDiffer.MinVersion(keys, new[] { VersionDiffer.Key("kw", "ABORT") }, 16));
        Assert.Equal(15, VersionDiffer.MinVersion(keys, new[] { VersionDiffer.Key("kw", "merge") }, 16));
        Assert.Equal(16, VersionDiffer.MinVersion(keys, new[] { VersionDiffer.Key("kw", "nothing") }, 16));
    }

    [Fact]
    public void An_alias_found_in_an_older_release_counts()
    {
        var keys = new Dictionary<int, ISet<string>>
        {
            [14] = new HashSet<string> { VersionDiffer.Key("type", "int4") },
            [15] = new HashSet<string> { VersionDiffer.Key("type", "int4") },
        };

        Assert.Equal(14, VersionDiffer.MinVersion(keys, new[] { VersionDiffer.Key("type", "integer"), VersionDiffer.Key("type", "int4") }, 15));
    }

    // ── The generator over a small tree ─────────────────────────────────────────────────────────

    private sealed class MemoryTree : ISourceTree
    {
        private readonly Dictionary<string, string> _files;
        public MemoryTree(Dictionary<string, string> files) => _files = files;

        public string? ReadText(string relativePath) => _files.GetValueOrDefault(relativePath);

        public IEnumerable<string> ListFiles(string relativeDirectory, string searchPattern)
        {
            var prefix = relativeDirectory.TrimEnd('/') + "/";
            var extension = searchPattern.TrimStart('*');
            return _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k.EndsWith(extension, StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal);
        }
    }

    private static MemoryTree Tree(Func<string, string>? edit = null)
    {
        string E(string text) => edit == null ? text : edit(text);
        return new MemoryTree(new Dictionary<string, string>
        {
            ["src/include/parser/kwlist.h"] = E(Fixture("kwlist.h")),
            ["src/include/catalog/pg_proc.dat"] = E(Fixture("pg_proc.dat")),
            ["src/include/catalog/pg_operator.dat"] = E(Fixture("pg_operator.dat")),
            ["src/include/catalog/pg_type.dat"] = E(Fixture("pg_type.dat")),
            ["src/backend/utils/errcodes.txt"] = E(Fixture("errcodes.txt")),
            ["doc/src/sgml/ref/select.sgml"] = E(Fixture("select.sgml")),
            ["doc/src/sgml/func.sgml"] = E(Fixture("func.sgml")),
            ["doc/src/sgml/datatype.sgml"] = E(Fixture("datatype.sgml")),
        });
    }

    private static BuildResult Build()
    {
        // 14 lacks the MERGE key word, the made-up error code and the pi function: they are "since 18".
        var older = Tree(text => text
            .Replace("PG_KEYWORD(\"merge\", MERGE, UNRESERVED_KEYWORD, BARE_LABEL)", string.Empty)
            .Replace("42XXX    E    ERRCODE_MADE_UP_CODE", string.Empty)
            .Replace("<function>pi</function>", "<function>pi_before</function>"));
        var snapshots = new Dictionary<int, TagSnapshot>
        {
            [14] = TagSnapshot.Read(older),
            [18] = TagSnapshot.Read(Tree()),
        };
        return Generator.Build(snapshots, 18, new[] { "REL_14_STABLE", "REL_18_STABLE" });
    }

    private static ReferenceEntry Entry(BuildResult result, string kind, string name) =>
        result.File.Entries.Single(e => e.Kind == kind && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Release_numbers_are_read_from_tags()
    {
        Assert.Equal(14, Generator.MajorOf("REL_14_STABLE"));
        Assert.Equal(18, Generator.MajorOf("REL_18_STABLE"));
        Assert.Equal(0, Generator.MajorOf("master"));
    }

    [Fact]
    public void Every_kind_is_built_from_its_source()
    {
        var result = Build();
        var byKind = result.File.Entries.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(3, byKind[EntryKinds.Command]);       // SELECT, TABLE and WITH share one page
        Assert.Equal(6, byKind[EntryKinds.Keyword]);
        Assert.Equal(6, byKind[EntryKinds.Type]);
        Assert.Equal(9, byKind[EntryKinds.Function]);      // pi date_trunc now generate_series pg_sleep pg_sleep_for coalesce greatest least
        Assert.Equal(1, byKind[EntryKinds.Aggregate]);
        Assert.Equal(1, byKind[EntryKinds.Window]);
        Assert.Equal(1, byKind[EntryKinds.Operator]);
        Assert.Equal(7, byKind[EntryKinds.SqlState]);
        Assert.Equal(21, byKind[EntryKinds.Lexical]);
    }

    [Fact]
    public void A_command_entry_has_its_synopsis_and_the_page_it_came_from()
    {
        var select = Entry(Build(), EntryKinds.Command, "SELECT");

        Assert.Equal("retrieve rows from a table or view", select.Summary);
        Assert.StartsWith("[ WITH [ RECURSIVE ]", select.Synopsis);
        Assert.Equal("sql-select", select.Source.SectionId);
        Assert.Equal("https://www.postgresql.org/docs/18/sql-select.html", select.Url);
        Assert.Equal("doc/src/sgml/ref/select.sgml", select.Source.File);
    }

    [Fact]
    public void Aggregates_and_window_functions_are_told_apart_by_the_catalog()
    {
        var result = Build();

        Assert.Equal(EntryKinds.Aggregate, Entry(result, EntryKinds.Aggregate, "string_agg").Kind);
        Assert.Equal(EntryKinds.Window, Entry(result, EntryKinds.Window, "row_number").Kind);
    }

    [Fact]
    public void Overloads_are_one_entry_with_several_signatures_in_sql_type_names()
    {
        var agg = Entry(Build(), EntryKinds.Aggregate, "string_agg");

        Assert.Equal(2, agg.Signatures!.Count);
        Assert.Contains(agg.Signatures, s => s.Returns == "text" && s.Args.Select(a => a.Type).SequenceEqual(new[] { "text", "text" }));
        Assert.Contains(agg.Signatures, s => s.Returns == "bytea");
        Assert.Equal("value", agg.Signatures[0].Args[0].Name);
        Assert.Equal("functions-aggregate", agg.Category);
    }

    [Fact]
    public void Catalog_type_names_are_shown_as_the_sql_names()
    {
        var series = Entry(Build(), EntryKinds.Function, "generate_series");

        var signature = Assert.Single(series.Signatures!);
        Assert.Equal(new[] { "integer", "integer" }, signature.Args.Select(a => a.Type).ToArray());
        Assert.Equal("integer", signature.Returns);
        Assert.True(signature.IsSetReturning);
        Assert.Equal(new[] { "smallint", "double precision", "timestamp with time zone", "integer[]", "boolean" },
            new[] { "int2", "float8", "timestamptz", "_int4", "bool" }.Select(Generator.DisplayType).ToArray());
    }

    [Fact]
    public void A_function_with_no_catalog_row_keeps_the_signature_the_manual_gives()
    {
        var coalesce = Entry(Build(), EntryKinds.Function, "coalesce");
        var trunc = Entry(Build(), EntryKinds.Function, "date_trunc");

        var signature = Assert.Single(coalesce.Signatures!);
        var value = Assert.Single(signature.Args);
        Assert.Equal(("value", "variadic"), (value.Type, value.Mode));
        Assert.Equal("doc/src/sgml/func.sgml", coalesce.Source.File);
        Assert.Equal(new[] { "field text", "source timestamp" }, Assert.Single(trunc.Signatures!).Args.Select(a => a.Type).ToArray());
    }

    [Fact]
    public void An_operator_the_catalog_lacks_is_read_from_the_manuals_signature()
    {
        var plus = Entry(Build(), EntryKinds.Operator, "+");

        var signature = Assert.Single(plus.Signatures!);
        Assert.Equal(new[] { "numeric", "numeric" }, signature.Args.Select(a => a.Type).ToArray());
        Assert.Equal("numeric", signature.Returns);
    }

    [Fact]
    public void Types_have_aliases_category_and_a_page()
    {
        var result = Build();

        var integer = Entry(result, EntryKinds.Type, "integer");
        Assert.Equal(new[] { "int", "int4" }, integer.Aliases.ToArray());
        Assert.Equal("numeric", integer.Category);          // from the catalog row of int4
        Assert.Equal("datatype-numeric", integer.Source.SectionId);
        Assert.Equal("pseudo", Entry(result, EntryKinds.Type, "record").Category);
    }

    [Fact]
    public void Sqlstate_entries_carry_the_condition_name_and_the_class()
    {
        var unique = Entry(Build(), EntryKinds.SqlState, "23505");

        Assert.Equal(new[] { "unique_violation" }, unique.Aliases.ToArray());
        Assert.Equal("Unique violation", unique.Summary);
        Assert.Equal("Class 23 - Integrity Constraint Violation", unique.Category);
    }

    [Fact]
    public void Min_version_is_the_oldest_release_that_has_the_entry()
    {
        var result = Build();

        Assert.Equal(14, Entry(result, EntryKinds.Keyword, "ABORT").MinVersion);
        Assert.Equal(18, Entry(result, EntryKinds.Keyword, "MERGE").MinVersion);
        Assert.Equal(18, Entry(result, EntryKinds.SqlState, "42XXX").MinVersion);
        Assert.Equal(18, Entry(result, EntryKinds.Function, "pi").MinVersion);
        Assert.Equal(14, Entry(result, EntryKinds.Function, "now").MinVersion);
        Assert.Equal(14, Entry(result, EntryKinds.Type, "integer").MinVersion);   // through its alias int4, which the catalog has everywhere
        Assert.All(result.File.Entries.Where(e => e.Kind == EntryKinds.Lexical), e => Assert.Equal(14, e.MinVersion));
    }

    [Fact]
    public void Keywords_are_upper_case_and_say_how_they_may_be_used()
    {
        var result = Build();

        Assert.Equal("reserved", Entry(result, EntryKinds.Keyword, "LATERAL").Category);
        Assert.Contains("cannot be used", Entry(result, EntryKinds.Keyword, "LATERAL").Summary);
        Assert.Equal("LATERAL", Entry(result, EntryKinds.Keyword, "lateral").Name);
    }

    [Fact]
    public void The_catalog_entries_the_manual_does_not_document_are_counted_in_the_omissions()
    {
        var omissions = Build().Omissions;

        Assert.Contains(omissions, o => o.Kind == "function" && o.Name.Contains("distinct names") && o.Reason.Contains("not in the manual"));
        Assert.Contains(omissions, o => o.Kind == "operator" && o.Name.StartsWith("(3 ", StringComparison.Ordinal));
        Assert.DoesNotContain(omissions, o => o.Reason == "unresolved");
    }

    [Fact]
    public void Coverage_counts_what_the_manual_shows_against_what_the_reference_carries()
    {
        var coverage = Build().Coverage;

        Assert.Equal("18", coverage.DocVersion);
        Assert.Equal(new[] { 3, 3 }, new[] { coverage.Commands.Documented, coverage.Commands.Included });
        Assert.Equal(new[] { 6, 6 }, new[] { coverage.Keywords.Documented, coverage.Keywords.Included });
        Assert.Equal(new[] { 6, 6 }, new[] { coverage.Types.Documented, coverage.Types.Included });
        Assert.Equal(new[] { 11, 11 }, new[] { coverage.Functions.Documented, coverage.Functions.Included });
        Assert.Equal(new[] { 1, 1 }, new[] { coverage.Operators.Documented, coverage.Operators.Included });
        Assert.Equal(new[] { 7, 7 }, new[] { coverage.SqlState.Documented, coverage.SqlState.Included });
    }

    // ── The writer ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Names_are_unique_per_kind_and_every_entry_has_a_summary_section_and_url()
    {
        var entries = Build().File.Entries;

        Assert.Empty(entries.GroupBy(e => (e.Kind, e.Name.ToLowerInvariant())).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Summary), e.Kind + " " + e.Name);
            Assert.False(string.IsNullOrWhiteSpace(e.Source.SectionId), e.Kind + " " + e.Name);
            Assert.Matches(new Regex(@"^https://www\.postgresql\.org/docs/18/[a-z0-9\-]+\.html$"), e.Url);
            Assert.InRange(e.MinVersion, 14, 18);
        });
    }

    [Fact]
    public void The_json_has_the_shape_of_the_contract()
    {
        using var doc = JsonDocument.Parse(ReferenceWriter.ToJson(Build().File));
        var root = doc.RootElement;

        Assert.Equal("18", root.GetProperty("version").GetString());
        Assert.Equal(new[] { "REL_14_STABLE", "REL_18_STABLE" }, root.GetProperty("generatedFrom").GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("generatedFrom").GetProperty("generator").GetString()));

        var agg = root.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "string_agg");
        Assert.Equal(
            new[] { "kind", "name", "aliases", "category", "summary", "synopsis", "signatures", "minVersion", "source", "url" },
            agg.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "file", "sectionId" }, agg.GetProperty("source").EnumerateObject().Select(p => p.Name).ToArray());
        var arg = agg.GetProperty("signatures")[0].GetProperty("args")[0];
        Assert.Equal(new[] { "name", "type", "mode", "hasDefault" }, arg.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "args", "returns", "isSetReturning" }, agg.GetProperty("signatures")[0].EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Entries_are_written_in_the_order_of_the_kinds_and_then_by_name()
    {
        var entries = Build().File.Entries;

        var order = EntryKinds.Order.ToList();
        Assert.Equal(entries.OrderBy(e => order.IndexOf(e.Kind)).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.Ordinal).Select(e => e.Name),
            entries.Select(e => e.Name));
    }

    [Fact]
    public void Two_runs_give_the_same_bytes_with_unix_line_endings()
    {
        var first = Build();
        var second = Build();

        Assert.Equal(ReferenceWriter.ToJson(first.File), ReferenceWriter.ToJson(second.File));
        Assert.Equal(ReferenceWriter.ToMarkdown(first.File, first.Coverage), ReferenceWriter.ToMarkdown(second.File, second.Coverage));
        Assert.DoesNotContain('\r', ReferenceWriter.ToJson(first.File));
        Assert.DoesNotContain('\r', ReferenceWriter.ToMarkdown(first.File, first.Coverage));
        Assert.EndsWith("\n", ReferenceWriter.ToJson(first.File));
    }

    [Fact]
    public void The_document_has_a_section_per_kind_and_a_link_per_entry()
    {
        var result = Build();
        var markdown = ReferenceWriter.ToMarkdown(result.File, result.Coverage);

        Assert.StartsWith("# PostgreSQL reference", markdown);
        Assert.Contains("for releases 14, 18", markdown);
        foreach (var heading in new[] { "## Coverage", "## SQL commands", "## Key words", "## Data types", "## Functions", "## Aggregate functions", "## Window functions", "## Operators", "## SQLSTATE codes", "## Lexical rules" })
            Assert.Contains(heading + "\n", markdown);
        Assert.Contains("| `SELECT` | retrieve rows from a table or view | 14 | [sql-select](https://www.postgresql.org/docs/18/sql-select.html) |", markdown);
        Assert.Contains("| `integer (int, int4)` |", markdown);
    }

    [Fact]
    public void The_writer_puts_the_four_files_where_the_product_and_the_reviewers_expect_them()
    {
        var directory = Path.Combine(Path.GetTempPath(), "akml-refgen-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = ReferenceWriter.WriteAll(directory, Build());

            Assert.Equal(4, written.Count);
            Assert.True(File.Exists(Path.Combine(directory, "src", "AkmlSql.PostgreSql", "Reference", "postgresql-reference.json")));
            Assert.True(File.Exists(Path.Combine(directory, "src", "AkmlSql.PostgreSql", "Reference", "omissions.json")));
            Assert.True(File.Exists(Path.Combine(directory, "src", "AkmlSql.PostgreSql", "Reference", "coverage.json")));
            Assert.True(File.Exists(Path.Combine(directory, "doc", "postgresql-reference.md")));
            Assert.False(File.ReadAllBytes(written[0]).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "no byte order mark");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
