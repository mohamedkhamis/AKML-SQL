using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T010(c) — DDL column + procedure parameter alignment verification. Two bugs found:
/// (1) ApplyCreateTableColumnsOnNewLine broke after EVERY comma inside the CREATE TABLE parens —
/// including commas nested in type/identity arguments — splitting "decimal(18, 2)" and
/// "identity(1, 1)" across lines (ParseColumnDefinitions was already depth-aware; only the break
/// loop was blind). (2) parameterAlignment "aligned" was a dead option: only the FIRST parameter
/// ever got a line break, yet the datatype/default alignment padded every parameter as if each
/// had its own line — stale double-spacing inside a single inline line
/// ("@customerid int, @startdate  datetime = NULL"). Parameters now go one per line under
/// "aligned" and padding only applies to a parameter that actually starts its line.
/// </summary>
public class DdlAlignmentLayoutTests
{
    private const string CreateTable =
        "create table dbo.orders (orderid int identity(1, 1) not null primary key, " +
        "total decimal(18, 2) not null, status varchar(20) not null);";

    private const string Proc =
        "create procedure dbo.GetCustomerOrders @customerid int, @startdate datetime = null, " +
        "@enddate datetime = null as begin select orderid from orders where customerid = @customerid; end";

    [Fact]
    public void CreateTable_TypeArguments_StayInline()
    {
        var result = new FormatterPipeline().Format(CreateTable, LoadDefaultStyle());
        Assert.True(result.ValidationPassed, result.FormattedText);
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');

        // The nested type/identity argument lists must not be split across lines.
        var decimalLine = Array.Find(lines, l => l.Contains("decimal", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(decimalLine);
        Assert.Matches(@"decimal\s*\(\s*18,\s*2\s*\)", decimalLine!);

        var identityLine = Array.Find(lines, l => l.Contains("identity", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(identityLine);
        Assert.Matches(@"identity\s*\(\s*1,\s*1\s*\)", identityLine!);
    }

    [Fact]
    public void ProcParameters_Aligned_OnePerLine_WithAlignedTypes()
    {
        var result = new FormatterPipeline().Format(Proc, LoadDefaultStyle());
        Assert.True(result.ValidationPassed, result.FormattedText);
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');

        var custLine = Array.Find(lines, l => l.TrimStart().StartsWith("@customerid", StringComparison.OrdinalIgnoreCase));
        var startLine = Array.Find(lines, l => l.TrimStart().StartsWith("@startdate", StringComparison.OrdinalIgnoreCase));
        var endLine = Array.Find(lines, l => l.TrimStart().StartsWith("@enddate", StringComparison.OrdinalIgnoreCase));
        Assert.True(custLine != null && startLine != null && endLine != null,
            "each parameter must start its own line:\n" + result.FormattedText);

        // Datatypes align to one column across the parameter lines.
        int intCol = custLine!.IndexOf("int", StringComparison.OrdinalIgnoreCase);
        int dt1Col = startLine!.IndexOf("datetime", StringComparison.OrdinalIgnoreCase);
        int dt2Col = endLine!.IndexOf("datetime", StringComparison.OrdinalIgnoreCase);
        Assert.True(intCol == dt1Col && dt1Col == dt2Col,
            $"datatypes not aligned (cols {intCol}/{dt1Col}/{dt2Col}):\n{result.FormattedText}");
    }

    [Fact]
    public void ProcParameters_NotAligned_StayInline_WithoutStalePadding()
    {
        var profile = LoadDefaultStyle();
        profile.Ddl.ParameterAlignment = "none";
        var result = new FormatterPipeline().Format(Proc, profile);
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');

        // Inline parameters must not carry alignment padding (no double spaces after the name).
        var paramLines = lines.Where(l => l.Contains('@')).ToArray();
        Assert.DoesNotContain(paramLines, l =>
            System.Text.RegularExpressions.Regex.IsMatch(l, @"@\w+ {2,}"));
    }

    [Fact]
    public void DdlAlignment_IsIdempotent()
    {
        var profile = LoadDefaultStyle();
        var onceTable = new FormatterPipeline().Format(CreateTable, profile);
        Assert.Equal(onceTable.FormattedText,
            new FormatterPipeline().Format(onceTable.FormattedText, profile).FormattedText);

        var onceProc = new FormatterPipeline().Format(Proc, profile);
        Assert.Equal(onceProc.FormattedText,
            new FormatterPipeline().Format(onceProc.FormattedText, profile).FormattedText);
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
