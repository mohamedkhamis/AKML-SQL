using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Dictionaries;
using AkmlSql.PostgreSql.Completion;
using AkmlSql.PostgreSql.Reference;
using AkmlSql.PostgreSql.Tests.Completion;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Isolation;

/// <summary>
/// Spec 042 (task T123, SC-004, FR-090, FR-091). The two dialects share nothing by accident:
/// <list type="bullet">
///   <item>no name is in both the PostgreSQL reference and the T-SQL dictionaries unless a person decided it means the same in
///   both and wrote it into <c>shared-sql-names.json</c> (and that list holds nothing that is not actually in both);</item>
///   <item>every PostgreSQL completion case of the corpus (more than 200) returns no <c>GO</c>, <c>@</c>, <c>#</c>, <c>[</c>,
///   <c>sp_</c>, <c>xp_</c> or <c>NOCOUNT</c> item, and none of the names only T-SQL has;</item>
///   <item>the T-SQL dictionaries are still what they were (<see cref="DictionarySnapshotTests"/>).</item>
/// </list>
/// The reverse probe, SQL Server completion returning no PostgreSQL-only name, runs against the SQL Server corpus in the engine
/// tests (<c>SqlServerNoPostgresItemsTests</c>), which owns that corpus and its fake cache.
/// </summary>
[Trait("Category", "Isolation")]
public class DialectIsolationTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Normalize(string name) => Regex.Replace(name.Trim(), @"\s+", " ").ToUpperInvariant();

    /// <summary>Every name the T-SQL dictionaries offer: key words of every category, built-in functions and system procedures.</summary>
    internal static HashSet<string> TsqlNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in typeof(KeywordDictionary).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(string[])))
            foreach (var word in (string[])field.GetValue(null)!) names.Add(Normalize(word));
        foreach (var function in BuiltinFunctionDictionary.GetAllFunctionNames()) names.Add(Normalize(function));
        foreach (var item in SystemProcDictionary.GetCompletionItems()) names.Add(Normalize(item.DisplayText));
        return names;
    }

    /// <summary>Every name the PostgreSQL reference documents, with its aliases (a function's alternative spellings).</summary>
    internal static HashSet<string> PostgresNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in PgReference.Instance.Entries)
        {
            names.Add(Normalize(entry.Name));
            foreach (var alias in entry.Aliases ?? Array.Empty<string>()) names.Add(Normalize(alias));
        }
        return names;
    }

    private static string IsolationDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AkmlSql.PostgreSql.Tests.csproj"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, "Isolation");
    }

    private sealed class SharedGroup
    {
        public string Why { get; set; } = string.Empty;
        public List<string> Names { get; set; } = new();

        /// <summary>A reason of this name's own, where the group's is not enough.</summary>
        public Dictionary<string, string>? Notes { get; set; }
    }

    /// <summary>Name -> the reason it is allowed to be in both. A group gives the reason; a name may have a note of its own.</summary>
    internal static Dictionary<string, string> SharedNames()
    {
        var path = Path.Combine(IsolationDirectory(), "shared-sql-names.json");
        var groups = JsonSerializer.Deserialize<List<SharedGroup>>(File.ReadAllText(path), Json) ?? new List<SharedGroup>();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var notes = group.Notes?.ToDictionary(n => Normalize(n.Key), n => n.Value, StringComparer.Ordinal);
            foreach (var name in group.Names)
            {
                var key = Normalize(name);
                Assert.True(result.TryAdd(key, notes != null && notes.TryGetValue(key, out var note) ? note : group.Why), key + " is listed twice");
            }
        }
        return result;
    }

    // ---- the dictionaries ------------------------------------------------------------------------------------

    [Fact]
    public void A_name_in_both_dictionaries_is_there_because_it_means_the_same_in_both()
    {
        var both = TsqlNames().Intersect(PostgresNames(), StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var shared = SharedNames();

        var unexplained = both.Where(n => !shared.ContainsKey(n)).ToArray();

        Assert.True(unexplained.Length == 0,
            "In both dictionaries with no entry in shared-sql-names.json (add it with the reason it means the same, or remove it from the dialect that should not have it): "
            + string.Join(", ", unexplained));
    }

    [Fact]
    public void The_allow_list_holds_only_names_that_are_really_in_both_and_gives_a_reason_for_each()
    {
        var both = TsqlNames().Intersect(PostgresNames(), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var shared = SharedNames();

        Assert.Empty(shared.Keys.Where(n => !both.Contains(n)));
        Assert.All(shared, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key + " has no reason"));
    }

    [Fact]
    public void The_names_that_only_T_SQL_has_are_not_in_the_PostgreSQL_reference()
    {
        // The well-known ones, so the check above cannot be satisfied by an allow-list that grew too generous.
        string[] tsqlOnly = { "GO", "NOCOUNT", "GETDATE", "GETUTCDATE", "DATEADD", "DATEDIFF", "CHARINDEX", "STUFF", "ISNULL", "IIF", "TOP", "SP_HELP", "SP_EXECUTESQL", "XP_CMDSHELL", "TRY_CAST", "TRY_CONVERT" };
        var postgres = PostgresNames();

        // ISNULL is both a T-SQL function and a (non-standard) PostgreSQL postfix operator word; the allow-list says so.
        var shared = SharedNames();
        Assert.All(tsqlOnly, name => Assert.True(!postgres.Contains(name) || shared.ContainsKey(name), name + " is in the PostgreSQL reference"));
        Assert.Contains("postfix", shared["ISNULL"]);
    }

    // ---- the probe ------------------------------------------------------------------------------------------

    private sealed class CorpusCase
    {
        public string Id { get; set; } = string.Empty;
        public string Doc { get; set; } = string.Empty;
        public int? ServerMajor { get; set; }
        public string[]? SearchPath { get; set; }
    }

    private static string CorpusDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "tests", "pg-completion-corpus");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("tests/pg-completion-corpus not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Words the PostgreSQL completion offers from its own lists, outside the reference, that T-SQL also has in another meaning.
    /// They are PostgreSQL's by right; the probe must not call them a leak.
    /// </summary>
    private static readonly Dictionary<string, string> PostgresOwnListNames = new(StringComparer.Ordinal)
    {
        ["HASH"] = "An index access method name after USING (btree, hash, gist, gin, spgist, brin). T-SQL has HASH as a join and index hint.",
    };

    /// <summary>
    /// A compound key word the completion offers for convenience ("GROUP BY", "FETCH FIRST") is T-SQL-only only when one of its
    /// words is: the reference lists PostgreSQL's key words one by one, so the compound is judged by its parts.
    /// </summary>
    private static bool IsTsqlOnly(string label, HashSet<string> tsqlOnly, HashSet<string> postgres)
    {
        var name = Normalize(label);
        if (!tsqlOnly.Contains(name) || PostgresOwnListNames.ContainsKey(name)) return false;
        var words = name.Split(' ');
        return words.Length == 1 || words.Any(w => !postgres.Contains(w));
    }

    private static bool LooksLikeSqlServer(string label) =>
        string.Equals(label, "GO", StringComparison.OrdinalIgnoreCase)
        || label.StartsWith('@') || label.StartsWith('#') || label.StartsWith('[')
        || label.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) || label.StartsWith("xp_", StringComparison.OrdinalIgnoreCase)
        || label.Contains("NOCOUNT", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void A_PostgreSQL_probe_of_every_corpus_case_finds_no_SQL_Server_only_suggestion()
    {
        var cases = Directory.GetFiles(CorpusDirectory(), "p*.json").OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(f => JsonSerializer.Deserialize<List<CorpusCase>>(File.ReadAllText(f), Json) ?? new List<CorpusCase>())
            .ToList();
        var postgres = PostgresNames();
        var tsqlOnly = TsqlNames().Except(postgres, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var engine = new PgCompletionEngine();
        var offenders = new List<string>();
        var probed = 0;

        foreach (var c in cases)
        {
            var caret = c.Doc.IndexOf('|');
            if (caret < 0) continue;
            var sql = c.Doc.Remove(caret, 1);
            var cache = NorthwindPgTestCacheFactory.Create(c.SearchPath ?? Array.Empty<string>());
            var response = engine.GetCompletions(sql, caret, cache, new PgCompletionOptions { ServerMajor = c.ServerMajor ?? 18 });
            probed++;

            foreach (var item in response.Items)
            {
                var label = item.DisplayText ?? string.Empty;
                var insert = item.InsertText ?? string.Empty;
                if (LooksLikeSqlServer(label) || LooksLikeSqlServer(insert) || IsTsqlOnly(label, tsqlOnly, postgres))
                    offenders.Add($"{c.Id}: '{label}'");
            }
        }

        Assert.True(probed >= 200, $"the probe ran {probed} cases; SC-004 asks for at least 200");
        Assert.True(offenders.Count == 0, "SQL Server-only suggestions in PostgreSQL mode: " + string.Join("; ", offenders.Take(20)));
    }

    [Fact]
    public void The_probe_would_notice_a_leak()
    {
        // The matcher itself: each pattern of SC-004 is caught, and ordinary PostgreSQL words are not.
        foreach (var leak in new[] { "GO", "go", "@id", "#temp", "[dbo]", "sp_help", "XP_cmdshell", "SET NOCOUNT ON" })
            Assert.True(LooksLikeSqlServer(leak), leak);
        foreach (var fine in new[] { "SELECT", "pg_sleep", "customers", "now", "generate_series", "ON CONFLICT", "good", "xpath" })
            Assert.False(LooksLikeSqlServer(fine), fine);
    }
}
