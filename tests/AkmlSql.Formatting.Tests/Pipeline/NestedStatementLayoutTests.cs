using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T009 — nested-statement layout. Statements inside a block body (a stored-procedure /
/// trigger / function body, BEGIN…END, TRY/CATCH) used to cram onto the BEGIN line because
/// <c>LayoutEngine.BuildStatementStartSet</c> walked only top-level <c>batch.Statements</c>.
/// <c>BuildNestedStatementStartSet</c> now forces each block statement onto its own line (and
/// ControlFlowRules indents it at block depth); an IF/WHILE single-statement body is intentionally
/// left inline (<c>IF cond SET …</c>). Tests use realistic (non-collapsing) bodies so the
/// collapse-short-statement pass — a separate follow-up — doesn't re-merge short statements.
/// </summary>
public class NestedStatementLayoutTests
{
    // A proc body whose statements are individually long enough not to be collapsed.
    private const string Proc =
        "create procedure dbo.getorders @id int as begin " +
        "set nocount on; " +
        "if @id is null set @id = 0; " +
        "select orderid, orderdate, total from orders where customerid = @id order by orderdate desc; " +
        "update orders set processed = 1 where customerid = @id and processed = 0; " +
        "end;";

    private static string[] Format(string sql)
        => new FormatterPipeline().Format(sql, LoadDefaultStyle())
            .FormattedText.Replace("\r\n", "\n").Split('\n');

    [Fact]
    public void ProcBody_IsNotCrammed_OntoBeginLine()
    {
        var lines = Format(Proc);

        // The original bug: the whole body collapsed onto the BEGIN line. The BEGIN line must not
        // carry the SELECT/UPDATE body statements.
        Assert.DoesNotContain(lines, l =>
        {
            var u = l.ToUpperInvariant();
            return u.Contains("BEGIN") && (u.Contains("SELECT ") || u.Contains("UPDATE "));
        });

        // The substantial body statements each begin their own line.
        Assert.Contains(lines, l => l.TrimStart().StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l.TrimStart().StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase));

        // SELECT and UPDATE are distinct body statements — never on the same line.
        Assert.DoesNotContain(lines, l =>
        {
            var u = l.ToUpperInvariant();
            return u.Contains("SELECT ") && u.Contains("UPDATE ");
        });
    }

    [Fact]
    public void IfThen_SingleStatement_StaysInline()
    {
        // The IF then-clause ("set @id = 0") is not force-broken — IF and SET share a line.
        Assert.Contains(Format(Proc), l =>
            l.ToUpperInvariant().Contains("IF ") && l.ToUpperInvariant().Contains(" SET "));
    }

    [Fact]
    public void TryCatch_Statements_EachOnOwnLine()
    {
        var lines = Format(
            "begin try " +
            "update accounts set balance = balance - 100 where id = 1; " +
            "update accounts set balance = balance + 100 where id = 2; " +
            "end try begin catch " +
            "select error_message() as err; " +
            "end catch;");
        // The two TRY UPDATE statements must not share a line.
        Assert.DoesNotContain(lines, l =>
            l.ToUpperInvariant().Split("UPDATE ").Length > 2);  // "UPDATE " appears twice
        Assert.True(lines.Count(l => l.ToUpperInvariant().Contains("UPDATE ")) == 2,
            string.Join("\n", lines));
    }

    [Fact]
    public void NestedLayout_IsIdempotent()
    {
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format(Proc, profile);
        var twice = new FormatterPipeline().Format(once.FormattedText, profile);
        Assert.Equal(once.FormattedText, twice.FormattedText);
    }

    private static FormattingProfile LoadDefaultStyle()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("AKML-SQL.slnx not found");
        var stylePath = Path.Combine(dir.FullName, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", "default.akmlstyle");
        return ProfileSerializer.Deserialize(File.ReadAllText(stylePath));
    }
}
