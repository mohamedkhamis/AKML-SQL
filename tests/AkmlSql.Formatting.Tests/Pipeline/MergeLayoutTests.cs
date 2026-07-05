using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 (MERGE closure) — the MERGE keyword must be cased like any reserved keyword, and each
/// top-level MERGE match clause (WHEN [NOT] MATCHED ... THEN ...) must start its own line at the
/// statement indent. The clause breaks are re-asserted in a post-collapse finalization pass
/// (FormatterPipeline.NormalizeMergeWhenLayout) because the rule sets' collapse passes otherwise
/// re-cram a WHEN onto the preceding SET/VALUES/INSERT clause. The finalization is CASE-aware: a
/// WHEN inside a CASE...END in an UPDATE SET is a case branch, NOT a MERGE match clause.
/// </summary>
public class MergeLayoutTests
{
    private static FormattingProfile DefaultProfile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", "default.akmlstyle");
        var p = ProfileSerializer.Deserialize(File.ReadAllText(path));
        p.Metadata.EnableIdempotencyCheck = false;
        return p;
    }

    private static string Fmt(string sql) => new FormatterPipeline().Format(sql, DefaultProfile()).FormattedText;

    private static int TopLevelWhenLines(string formatted) =>
        formatted.Replace("\r\n", "\n").Split('\n')
            .Count(l => l.StartsWith("WHEN ", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Merge_Keyword_IsUppercased()
    {
        var outp = Fmt("merge into dbo.t as t using dbo.s as s on t.id = s.id " +
                       "when matched then update set t.v = s.v " +
                       "when not matched then insert (id) values (s.id);");
        Assert.StartsWith("MERGE", outp.TrimStart(), StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_EachMatchClause_StartsItsOwnLine()
    {
        var outp = Fmt("merge into dbo.t as t using dbo.s as s on t.id = s.id " +
                       "when matched then update set t.v = s.v " +
                       "when not matched by target then insert (id) values (s.id) " +
                       "when not matched by source then delete;");
        Assert.Equal(3, TopLevelWhenLines(outp));   // three MERGE match clauses, each on its own line
    }

    [Fact]
    public void Merge_WithCaseInUpdate_DoesNotHoistCaseWhenToMatchClause()
    {
        // The UPDATE SET contains a CASE...WHEN...END. Only the TWO MERGE match clauses may start a
        // top-level line; the CASE's WHEN branch must NOT be treated as a MERGE match clause.
        var outp = Fmt("merge into dbo.t as t using dbo.s as s on t.id = s.id " +
                       "when matched then update set t.v = case when s.x = 1 then 'a' else 'b' end " +
                       "when not matched then insert (id) values (s.id);");
        Assert.Equal(2, TopLevelWhenLines(outp));
        Assert.Contains("CASE", outp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("END", outp, StringComparison.OrdinalIgnoreCase);
    }
}
