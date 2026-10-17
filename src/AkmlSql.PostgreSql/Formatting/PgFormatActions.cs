using AkmlSql.Engine.Schema;
using AkmlSql.PostgreSql.Completion;
using AkmlSql.PostgreSql.Language;

namespace AkmlSql.PostgreSql.Formatting;

/// <summary>What a schema-aware Format SQL action did: the text, how many places it changed, and what it could not do.</summary>
public sealed record PgActionResult(string Text, int Changes, IReadOnlyList<string> Warnings);

/// <summary>
/// Spec 042 (task T152, FR-147) - the two schema-aware Format SQL actions for PostgreSQL scripts: expand <c>*</c> into the column list, and
/// qualify table names with their schema. Both read the schema the editor session holds and write names the way PostgreSQL wants them:
/// double-quoted only where the name needs it. A place that cannot be resolved is left alone and reported, never guessed.
/// </summary>
public static class PgFormatActions
{
    public const string NeedsSchema = "Connect to a database first: the schema is not loaded.";

    // ── expand wildcards ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces each <c>*</c> and <c>alias.*</c> of a select list (and of <c>RETURNING</c>) by the columns of the tables it stands for.
    /// With several tables in the query, or a qualified star, each column is written with its table's name or alias.
    /// </summary>
    public static PgActionResult ExpandWildcards(string text, DatabaseCache? cache)
    {
        if (cache == null) return new PgActionResult(text, 0, new[] { NeedsSchema });

        var tokens = PgFormatTokens.Build(text);
        var stars = new List<(int Start, int End)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (!t.IsOp("*")) continue;
            var qualifiedStart = t.Start;
            // alias.*  : the star, the dot and the name before it are replaced together
            if (i >= 2 && tokens[i - 1].Kind == PgFt.Dot && (tokens[i - 2].IsWord || tokens[i - 2].Kind == PgFt.Quoted))
                qualifiedStart = tokens[i - 2].Start;
            stars.Add((qualifiedStart, t.End));
        }

        var expander = new PgWildcardExpander();
        var warnings = new List<string>();
        var result = text;
        var changes = 0;
        foreach (var (start, end) in stars.OrderByDescending(s => s.Start))
        {
            var response = expander.Handle(text, end, null, cache);
            if (!response.Success)
            {
                // Not every * is a wildcard (count(*), 2 * 3): only report a refusal that says something about the query.
                if (response.ErrorMessage is { } message && !message.StartsWith("Place the caret", StringComparison.Ordinal)
                    && !message.StartsWith("A * inside", StringComparison.Ordinal) && !message.StartsWith("The * ", StringComparison.Ordinal)
                    && !message.StartsWith("The caret is inside", StringComparison.Ordinal) && !warnings.Contains(message))
                    warnings.Add(message);
                continue;
            }

            var qualified = end - start > 1 || response.Tables.Length > 1;
            var columns = response.Tables
                .SelectMany(g => g.Columns.Select(c => qualified && g.Qualifier.Length > 0 ? g.Qualifier + "." + c.ColumnName : c.ColumnName))
                .ToList();
            if (columns.Count == 0) continue;

            result = result.Substring(0, start) + string.Join(", ", columns) + result.Substring(end);
            changes++;
        }
        return new PgActionResult(result, changes, warnings);
    }

    // ── qualify names ────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] TableIntroducers = { "FROM", "JOIN", "UPDATE", "USING", "TABLE" };

    /// <summary>
    /// Writes <c>schema.name</c> for each table named without a schema after FROM, JOIN, UPDATE, INSERT INTO, DELETE FROM and USING, when the
    /// schema cache finds exactly that table. Names of the statement's own CTEs, subqueries and functions are left as they are.
    /// </summary>
    public static PgActionResult QualifyNames(string text, DatabaseCache? cache)
    {
        if (cache == null) return new PgActionResult(text, 0, new[] { NeedsSchema });

        var tokens = PgFormatTokens.Build(text);
        var view = new PgCatalogView(cache, new PgCompletionOptions());
        var ctes = CteNames(tokens);
        var inserts = new List<(int At, string Schema)>();
        var warnings = new List<string>();

        for (var i = 0; i < tokens.Count; i++)
        {
            var first = FirstNameAfterIntroducer(tokens, i);
            if (first < 0) continue;

            // ONLY and LATERAL are not names; a function call or a subquery is not a table
            var name = tokens[first];
            if (first + 1 < tokens.Count && tokens[first + 1].Kind is PgFt.Dot or PgFt.Open) continue;
            if (!name.IsWord && name.Kind != PgFt.Quoted) continue;

            var written = name.Kind == PgFt.Quoted ? Unquote(name.Text) : name.Text.ToLowerInvariant();
            if (name.Kind == PgFt.Word && ctes.Contains(written)) continue;

            var found = view.Resolve(null, written);
            if (found == null) continue;
            if (name.Kind == PgFt.Word && PgWords.Reserved.Contains(name.Upper)) continue;
            inserts.Add((name.Start, found.SchemaName));
        }

        var result = text;
        foreach (var (at, schema) in inserts.OrderByDescending(x => x.At))
            result = result.Insert(at, PgCatalogView.Written(schema, forceQuote: false) + ".");
        return new PgActionResult(result, inserts.Count, warnings);
    }

    /// <summary>The index of the table-name token that follows the introducer at <paramref name="i"/>, or -1.</summary>
    private static int FirstNameAfterIntroducer(PgFormatTokens tokens, int i)
    {
        var t = tokens[i];
        if (!t.IsWord) return -1;
        var isIntroducer = t.IsAny(TableIntroducers)
            || (t.Is("INTO") && i > 0 && tokens[i - 1].Is("INSERT"))
            || (t.Is("FROM") && i > 0 && tokens[i - 1].Is("DELETE"));
        if (!isIntroducer) return -1;
        // FROM inside EXTRACT(... FROM x) / SUBSTRING(... FROM n): the previous open parenthesis belongs to a function taking a keyword
        if (t.Is("FROM") && InsideKeywordCall(tokens, i)) return -1;
        // TABLE is the introducer only in the TABLE name statement or LOCK TABLE
        if (t.Is("TABLE") && !(i == 0 || tokens[i - 1].Kind == PgFt.Semi)) return -1;

        var k = i + 1;
        while (k < tokens.Count && tokens[k].IsAny("ONLY", "LATERAL")) k++;
        return k < tokens.Count ? k : -1;
    }

    private static bool InsideKeywordCall(PgFormatTokens tokens, int i)
    {
        var depth = 0;
        for (var k = i - 1; k >= 0; k--)
        {
            if (tokens[k].Kind == PgFt.Close) depth++;
            else if (tokens[k].Kind == PgFt.Open)
            {
                if (depth == 0)
                    return k > 0 && tokens[k - 1].IsAny("EXTRACT", "SUBSTRING", "TRIM", "OVERLAY", "POSITION", "NORMALIZE");
                depth--;
            }
            else if (tokens[k].Kind == PgFt.Semi) return false;
        }
        return false;
    }

    /// <summary>Names the script's own WITH clauses define, folded the way the server folds them.</summary>
    private static HashSet<string> CteNames(PgFormatTokens tokens)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!tokens[i].Is("AS")) continue;
            // name [ (columns) ] AS [NOT] [MATERIALIZED] (
            var k = i + 1;
            if (k < tokens.Count && tokens[k].Is("NOT")) k++;
            if (k < tokens.Count && tokens[k].Is("MATERIALIZED")) k++;
            if (k >= tokens.Count || tokens[k].Kind != PgFt.Open) continue;
            var nameIndex = i - 1;
            if (nameIndex >= 0 && tokens[nameIndex].Kind == PgFt.Close)
            {
                var open = tokens.MatchOf(nameIndex);
                nameIndex = open - 1;
            }
            if (nameIndex >= 0 && (tokens[nameIndex].IsWord || tokens[nameIndex].Kind == PgFt.Quoted))
                names.Add(tokens[nameIndex].Kind == PgFt.Quoted ? Unquote(tokens[nameIndex].Text) : tokens[nameIndex].Text.ToLowerInvariant());
        }
        return names;
    }

    private static string Unquote(string quoted) => quoted.Substring(1, quoted.Length - 2).Replace("\"\"", "\"");
}
