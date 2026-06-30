using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T009 residual — a SHORT proc/block body must keep its BEGIN…END structure. The
/// nested-statement layout (T009 BEGIN-cram fix) breaks each block statement onto its own line,
/// but a short statement's collapse pass re-merged the whole thing onto the BEGIN line
/// ("BEGIN SET …; SELECT …; END") when the statement fit the collapse threshold. Block structure
/// is semantic-visual: SQL Prompt never inlines a procedure body, regardless of length.
/// </summary>
public class ShortBlockLayoutTests
{
    private const string TinyProc =
        "create procedure dbo.p as begin set nocount on; select 1; end";

    [Fact]
    public void TinyProcBody_KeepsBeginEndStructure()
    {
        var result = new FormatterPipeline().Format(TinyProc, LoadDefaultStyle());
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).ToArray();

        // BEGIN must not carry body statements, and END must start its own line.
        Assert.False(lines.Any(l =>
                l.Contains("BEGIN", StringComparison.OrdinalIgnoreCase) &&
                l.Contains("SET", StringComparison.OrdinalIgnoreCase)),
            "BEGIN line carries the body:\n" + result.FormattedText);
        Assert.Contains(lines, l => l.StartsWith("END", StringComparison.OrdinalIgnoreCase));

        // The terminator hugs its statement ("SELECT 1;", never "SELECT 1 ;" or a lone ";").
        Assert.Contains(lines, l => l.Equals("SELECT 1;", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TinyProc_Layout_IsIdempotent()
    {
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format(TinyProc, profile);
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
