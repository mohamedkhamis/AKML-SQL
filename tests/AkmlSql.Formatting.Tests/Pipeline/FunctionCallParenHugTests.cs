using System;
using System.IO;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T009 — a function call's opening parenthesis must hug its name in EVERY style,
/// including aligned-left-bracket (<c>openOnSameLine = false</c>). This is a style-independent
/// invariant, so it needs no Redgate oracle. Regression guard for the bug where the
/// openOnSameLine=false path stranded calls as <c>SUM\n(x)</c> with the "(" at column 0
/// (visible in tests/format-parity/golden/02|04-*__aligned-left-bracket.sql before the fix).
/// </summary>
public class FunctionCallParenHugTests
{
    private static FormattingProfile AlignedLeftBracketProfile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "AkmlSql.Formatting",
            "Profiles", "BuiltIn", "aligned-left-bracket.akmlstyle");
        var profile = ProfileSerializer.Deserialize(File.ReadAllText(path));
        profile.Metadata.EnableIdempotencyCheck = false;
        return profile;
    }

    [Theory]
    [InlineData("SELECT SUM(x) AS s FROM t;", "sum(x)")]
    [InlineData("SELECT COUNT(o.id) AS c FROM o;", "count(o.id)")]
    [InlineData("SELECT DATEADD(MONTH, -6, GETDATE()) AS d;", "dateadd(month")]
    public void FunctionCallParen_HugsName_InAlignedLeftBracket(string input, string expectedContiguousLower)
    {
        var profile = AlignedLeftBracketProfile();

        var formatted = new FormatterPipeline().Format(input, profile).FormattedText;

        // Case-insensitive: styles re-case keywords/functions, so don't assert literal casing —
        // assert the name and its "(" stay contiguous (they wouldn't if the name were stranded).
        Assert.Contains(expectedContiguousLower, formatted.ToLowerInvariant());
    }
}
