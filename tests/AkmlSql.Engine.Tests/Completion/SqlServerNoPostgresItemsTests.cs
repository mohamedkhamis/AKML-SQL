using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Dictionaries;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Tests.PostgreSql;
using AkmlSql.PostgreSql.Reference;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 042 (task T123, SC-004, FR-090, FR-091) - the reverse probe. Every case of the SQL Server completion corpus runs through
/// the T-SQL engine, and no key word or function it offers is a name that only PostgreSQL has (<c>ILIKE</c>, <c>RETURNING</c>,
/// <c>LIMIT</c>, <c>generate_series</c>, <c>jsonb_agg</c> ...). "Only PostgreSQL has" means: in the PostgreSQL reference, not in the
/// T-SQL dictionaries, and not on <c>PostgresNamesThatAreAlsoTransactSql</c> (words the T-SQL engine offers from its own provider
/// lists, outside the dictionaries, that really are T-SQL; each with its reason). The forward probe, PostgreSQL completion returning
/// nothing of SQL Server's, is in the PostgreSQL library tests (<c>DialectIsolationTests</c>).
/// </summary>
[Trait("Category", "Isolation")]
[Collection(TsqlParserCounterCollection.Name)]
public class SqlServerNoPostgresItemsTests
{
    private static string Normalize(string name) => Regex.Replace(name.Trim(), @"\s+", " ").ToUpperInvariant();

    /// <summary>T-SQL words the T-SQL engine offers from its providers' own lists, which are not in the dictionaries and are in PostgreSQL too.</summary>
    private static readonly Dictionary<string, string> PostgresNamesThatAreAlsoTransactSql = new(StringComparer.Ordinal)
    {
        ["ASC"] = "ORDER BY direction; standard SQL, offered by the T-SQL ORDER BY provider.",
        ["DESC"] = "ORDER BY direction; standard SQL, offered by the T-SQL ORDER BY provider.",
        ["BY"] = "The second word of GROUP BY and ORDER BY, offered on its own after the first.",
        ["OFFSET"] = "OFFSET n ROWS [FETCH NEXT m ROWS ONLY], T-SQL 2012 and later.",
    };

    private static HashSet<string> TsqlNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in typeof(KeywordDictionary).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(string[])))
            foreach (var word in (string[])field.GetValue(null)!) names.Add(Normalize(word));
        foreach (var function in BuiltinFunctionDictionary.GetAllFunctionNames()) names.Add(Normalize(function));
        foreach (var item in SystemProcDictionary.GetCompletionItems()) names.Add(Normalize(item.DisplayText));
        return names;
    }

    private static HashSet<string> PostgresOnlyNames()
    {
        var postgres = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in PgReference.Instance.Entries)
        {
            postgres.Add(Normalize(entry.Name));
            foreach (var alias in entry.Aliases) postgres.Add(Normalize(alias));
        }
        postgres.ExceptWith(TsqlNames());
        postgres.ExceptWith(PostgresNamesThatAreAlsoTransactSql.Keys);
        return postgres;
    }

    private sealed class CorpusCase
    {
        public string Id { get; set; } = string.Empty;
        public string Doc { get; set; } = string.Empty;
    }

    private static string CorpusDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "tests", "completion-corpus");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("tests/completion-corpus not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void A_SQL_Server_probe_of_the_whole_corpus_finds_no_suggestion_only_PostgreSQL_has()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var cases = Directory.GetFiles(CorpusDir(), "f*.json").OrderBy(f => f, StringComparer.Ordinal)
            .Where(f => !Path.GetFileName(f).StartsWith("f21", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => JsonSerializer.Deserialize<List<CorpusCase>>(File.ReadAllText(f), options) ?? new List<CorpusCase>())
            .ToList();
        var postgresOnly = PostgresOnlyNames();
        var cache = NorthwindAutoTestCacheFactory.Create();
        var engine = new CompletionEngine(new TsqlParserService());
        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        var probed = 0;

        foreach (var c in cases)
        {
            var caret = c.Doc.IndexOf('|');
            if (caret < 0) continue;
            var response = engine.GetCompletions(c.Doc.Remove(caret, 1), caret, cache);
            probed++;

            // Key words and functions only: a table or column the fake database happens to name like a PostgreSQL word is its own.
            foreach (var item in response.Items.Where(i => i.ObjectType is (int)CompletionObjectType.Keyword or (int)CompletionObjectType.Function))
                if (postgresOnly.Contains(Normalize(item.DisplayText)))
                    offenders.Add(Normalize(item.DisplayText));
        }

        Assert.True(probed >= 200, $"the probe ran {probed} cases; SC-004 asks for at least 200");
        Assert.True(offenders.Count == 0,
            "Suggestions only PostgreSQL has, offered in SQL Server mode (remove them from the T-SQL lists, or, if they are really T-SQL, add them to PostgresNamesThatAreAlsoTransactSql with the reason): "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void The_PostgreSQL_only_set_is_not_trivially_small_and_has_the_well_known_names()
    {
        var postgresOnly = PostgresOnlyNames();

        Assert.True(postgresOnly.Count > 500, "PostgreSQL-only names: " + postgresOnly.Count);
        foreach (var name in new[] { "ILIKE", "RETURNING", "LIMIT", "PG_SLEEP", "JSONB_AGG", "LATERAL" })
            Assert.Contains(name, postgresOnly);
    }
}
