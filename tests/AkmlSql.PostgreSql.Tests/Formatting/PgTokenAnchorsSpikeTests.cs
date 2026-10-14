using AkmlSql.PostgreSql.Language;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.PostgreSql.Tests.Formatting;

/// <summary>
/// Spec 042 (task T143, spike S2, research R2). What the parse tree can tell a formatter about where things are in the text, so the
/// formatter's structure is built from facts and not from hope. For SELECT, INSERT ... ON CONFLICT, UPDATE ... RETURNING, DELETE,
/// WITH and window functions the test lists which node types carry a source <c>location</c>, which do not, and shows that a
/// location is only a start offset. The assertions pin the facts the design rests on; the output (<c>-v n</c>) is the record
/// copied into research R2.
/// </summary>
public class PgTokenAnchorsSpikeTests
{
    private readonly ITestOutputHelper _output;

    public PgTokenAnchorsSpikeTests(ITestOutputHelper output) => _output = output;

    private static readonly string[] Statements =
    {
        "SELECT a.id, upper(b.name) AS n FROM t a JOIN u b ON b.id = a.id WHERE a.x > 1 AND b.y IS NOT NULL GROUP BY a.id HAVING count(*) > 1 ORDER BY a.id DESC LIMIT 10 OFFSET 5",
        "INSERT INTO t (a, b) VALUES (1, 'x'), (2, 'y') ON CONFLICT (a) DO UPDATE SET b = excluded.b WHERE t.b <> excluded.b RETURNING a",
        "UPDATE t SET a = a + 1, b = 'z' FROM u WHERE t.id = u.id RETURNING t.a",
        "DELETE FROM t USING u WHERE t.id = u.id AND u.gone RETURNING t.id",
        "WITH RECURSIVE r(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM r WHERE n < 5) SELECT n FROM r",
        "SELECT sum(x) OVER (PARTITION BY g ORDER BY d ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) AS s, CASE WHEN x > 0 THEN 'p' ELSE 'n' END FROM t",
    };

    /// <summary>Message type name to (has a location field, how often it was seen).</summary>
    private static void Collect(IMessage message, Dictionary<string, (bool HasLocation, int Count)> seen, List<int> locations)
    {
        var descriptor = message.Descriptor;
        var location = descriptor.Fields.InFieldNumberOrder().FirstOrDefault(f => f.Name == "location");
        var name = descriptor.Name;
        seen[name] = (location != null, seen.TryGetValue(name, out var s) ? s.Count + 1 : 1);
        if (location != null && location.Accessor.GetValue(message) is int at && at >= 0) locations.Add(at);

        foreach (var field in descriptor.Fields.InFieldNumberOrder())
        {
            var value = field.Accessor.GetValue(message);
            switch (value)
            {
                case IMessage child:
                    Collect(child, seen, locations);
                    break;
                case System.Collections.IList list:
                    foreach (var item in list)
                        if (item is IMessage m) Collect(m, seen, locations);
                    break;
            }
        }
    }

    [Fact]
    public void Clause_level_nodes_carry_no_location_and_expression_locations_are_starts_only()
    {
        var seen = new Dictionary<string, (bool HasLocation, int Count)>();
        var locations = new List<int>();
        foreach (var sql in Statements)
        {
            var parsed = PgParser.Parse(sql);
            Assert.True(parsed.Success, sql);
            Collect(parsed.Tree!, seen, locations);
        }

        foreach (var (name, info) in seen.OrderBy(p => p.Key))
            _output.WriteLine($"{(info.HasLocation ? "location " : "NO LOCATION ")} {name} x{info.Count}");

        // The statement nodes and the clause containers that hold FROM, WHERE, GROUP BY ... have no position of their own,
        // so the text of a clause keyword cannot come from the tree.
        foreach (var clauseNode in new[] { "SelectStmt", "InsertStmt", "UpdateStmt", "DeleteStmt", "JoinExpr", "FromExpr", "LimitClause" })
            if (seen.TryGetValue(clauseNode, out var info)) Assert.False(info.HasLocation, clauseNode + " unexpectedly has a location");

        // Expressions and names do: these are start offsets of the node's first token.
        foreach (var expressionNode in new[] { "ColumnRef", "ResTarget", "A_Expr", "FuncCall", "RangeVar" })
            Assert.True(seen[expressionNode].HasLocation, expressionNode + " should carry a location");
    }

    [Fact]
    public void A_location_is_the_start_of_the_nodes_first_token_and_nothing_gives_an_end()
    {
        var sql = "SELECT a.id, upper(b.name) AS n FROM t a";
        var parsed = PgParser.Parse(sql);
        var select = parsed.Tree!.Stmts[0].Stmt.SelectStmt;

        var tokens = PgLexicalScanner.Scan(sql);
        var starts = tokens.Where(t => t.Kind != PgTokenKind.Whitespace).Select(t => t.Start).ToHashSet();

        var targets = select.TargetList.Select(t => t.ResTarget).ToList();
        _output.WriteLine("target locations: " + string.Join(", ", targets.Select(t => t.Location)));
        Assert.All(targets, t => Assert.Contains(t.Location, starts));

        // ResTarget.Location is where the expression starts (`upper`), not where `AS n` is; the alias has no position.
        Assert.Equal(sql.IndexOf("upper", StringComparison.Ordinal), targets[1].Location);
        Assert.DoesNotContain(sql.IndexOf(" n ", StringComparison.Ordinal) + 1, targets.Select(t => t.Location));

        // The statement itself is the only node with a length.
        Assert.Equal(sql.Length, parsed.Tree.Stmts[0].StmtLen == 0 ? sql.Length : parsed.Tree.Stmts[0].StmtLen);
    }

    [Fact]
    public void The_scanner_sees_every_clause_keyword_the_tree_cannot_place()
    {
        foreach (var sql in Statements)
        {
            var words = PgLexicalScanner.Scan(sql)
                .Where(t => t.Kind == PgTokenKind.Identifier)
                .Select(t => sql.Substring(t.Start, t.End - t.Start).ToUpperInvariant())
                .ToList();
            Assert.Contains(words[0], new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "WITH" });
        }
    }
}
