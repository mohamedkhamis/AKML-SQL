using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>
/// Spec 041 (T033, research R10) — the declared SQL type of a result column. The engine composes
/// it from <c>DbColumn</c> metadata into <c>ExecuteResultSet.ColumnSqlTypes</c> ("datetime2(3)",
/// "decimal(10,2)", "nvarchar(max)"), and the web edition parses it back to format values the way
/// SSMS shows them. The suffix form is decided by the base type name, never by SqlClient's 255
/// "not applicable" precision/scale sentinel.
/// </summary>
public class SqlTypeNameTests
{
    [Theory]
    [InlineData("nvarchar", 50, null, null, "nvarchar(50)")]
    [InlineData("varchar", 8000, null, null, "varchar(8000)")]
    [InlineData("char", 5, null, null, "char(5)")]
    [InlineData("nchar", 10, null, null, "nchar(10)")]
    [InlineData("binary", 2, null, null, "binary(2)")]
    [InlineData("varbinary", 16, null, null, "varbinary(16)")]
    public void Length_types_carry_their_length(string baseName, int size, int? precision, int? scale, string expected)
    {
        Assert.Equal(expected, SqlTypeName.Format(baseName, size, precision, scale));
    }

    [Theory]
    [InlineData("nvarchar", int.MaxValue, "nvarchar(max)")]
    [InlineData("nvarchar", -1, "nvarchar(max)")]
    [InlineData("varchar", int.MaxValue, "varchar(max)")]
    [InlineData("varbinary", int.MaxValue, "varbinary(max)")]
    [InlineData("varbinary", -1, "varbinary(max)")]
    public void Max_length_types_read_max(string baseName, int size, string expected)
    {
        Assert.Equal(expected, SqlTypeName.Format(baseName, size, null, null));
    }

    [Fact]
    public void Decimal_carries_precision_and_scale()
    {
        Assert.Equal("decimal(10,2)", SqlTypeName.Format("decimal", 17, 10, 2));
        Assert.Equal("decimal(18,0)", SqlTypeName.Format("decimal", 17, 18, 0));
    }

    [Fact]
    public void Numeric_is_reported_as_decimal()
    {
        Assert.Equal("decimal(9,4)", SqlTypeName.Format("numeric", 5, 9, 4));
    }

    [Theory]
    [InlineData("datetime2", 3, "datetime2(3)")]
    [InlineData("datetime2", 7, "datetime2(7)")]
    [InlineData("time", 0, "time(0)")]
    [InlineData("datetimeoffset", 7, "datetimeoffset(7)")]
    public void Fractional_second_types_carry_their_scale(string baseName, int scale, string expected)
    {
        Assert.Equal(expected, SqlTypeName.Format(baseName, 8, null, scale));
    }

    [Theory]
    [InlineData("int", 4, 10, 255)]
    [InlineData("bigint", 8, 19, 255)]
    [InlineData("smallint", 2, 5, 255)]
    [InlineData("tinyint", 1, 3, 255)]
    [InlineData("bit", 1, 255, 255)]
    [InlineData("date", 3, 10, 0)]
    [InlineData("datetime", 8, 23, 3)]
    [InlineData("smalldatetime", 4, 16, 0)]
    [InlineData("money", 8, 19, 255)]
    [InlineData("smallmoney", 4, 10, 255)]
    [InlineData("float", 8, 15, 255)]
    [InlineData("real", 4, 7, 255)]
    [InlineData("uniqueidentifier", 16, 255, 255)]
    [InlineData("xml", int.MaxValue, 255, 255)]
    [InlineData("text", int.MaxValue, 255, 255)]
    [InlineData("ntext", int.MaxValue, 255, 255)]
    [InlineData("image", int.MaxValue, 255, 255)]
    [InlineData("timestamp", 8, 255, 255)]
    [InlineData("sql_variant", 8016, 255, 255)]
    public void Other_types_stay_bare_whatever_the_metadata(string baseName, int size, int precision, int scale)
    {
        Assert.Equal(baseName, SqlTypeName.Format(baseName, size, precision, scale));
    }

    [Fact]
    public void User_defined_type_names_stay_as_given()
    {
        Assert.Equal("dbo.MyType", SqlTypeName.Format("dbo.MyType", 8, null, null));
        Assert.Equal("AdventureWorks.dbo.Flag", SqlTypeName.Format("AdventureWorks.dbo.Flag", 8, 255, 255));
    }

    [Fact]
    public void The_255_sentinel_never_produces_a_suffix()
    {
        Assert.Equal("datetime2", SqlTypeName.Format("datetime2", 8, 255, 255));
        Assert.Equal("decimal", SqlTypeName.Format("decimal", 17, 255, 255));
    }

    [Fact]
    public void Missing_metadata_leaves_the_base_name()
    {
        Assert.Equal("nvarchar", SqlTypeName.Format("nvarchar", null, null, null));
        Assert.Equal("decimal", SqlTypeName.Format("decimal", null, null, null));
        Assert.Equal("datetime2", SqlTypeName.Format("datetime2", null, null, null));
    }

    [Fact]
    public void Format_normalises_case_and_whitespace()
    {
        Assert.Equal("nvarchar(20)", SqlTypeName.Format("  NVarChar ", 20, null, null));
    }

    [Fact]
    public void Format_of_null_or_empty_is_empty()
    {
        Assert.Equal(string.Empty, SqlTypeName.Format(null, 4, null, null));
        Assert.Equal(string.Empty, SqlTypeName.Format("   ", 4, null, null));
    }

    [Fact]
    public void Parse_reads_the_declared_forms()
    {
        var n = SqlTypeName.Parse("nvarchar(50)");
        Assert.Equal("nvarchar", n.BaseName);
        Assert.Equal(50, n.Length);
        Assert.False(n.IsMax);

        var max = SqlTypeName.Parse("varbinary(max)");
        Assert.Equal("varbinary", max.BaseName);
        Assert.True(max.IsMax);
        Assert.Null(max.Length);

        var d = SqlTypeName.Parse("decimal(10,2)");
        Assert.Equal("decimal", d.BaseName);
        Assert.Equal(10, d.Precision);
        Assert.Equal(2, d.Scale);

        var t = SqlTypeName.Parse("datetime2(3)");
        Assert.Equal("datetime2", t.BaseName);
        Assert.Equal(3, t.Scale);
        Assert.Null(t.Precision);

        var off = SqlTypeName.Parse(" DateTimeOffset( 7 ) ");
        Assert.Equal("datetimeoffset", off.BaseName);
        Assert.Equal(7, off.Scale);
    }

    [Fact]
    public void Parse_reads_bare_names_from_older_engines()
    {
        var bare = SqlTypeName.Parse("datetime2");
        Assert.Equal("datetime2", bare.BaseName);
        Assert.Null(bare.Scale);
        Assert.Null(bare.Precision);
        Assert.Null(bare.Length);

        Assert.Equal("int", SqlTypeName.Parse("INT").BaseName);
    }

    [Fact]
    public void Parse_keeps_a_user_defined_type_name()
    {
        var udt = SqlTypeName.Parse("dbo.MyType");
        Assert.Equal("dbo.MyType", udt.BaseName);
    }

    [Fact]
    public void Parse_of_null_or_empty_is_safe()
    {
        Assert.Equal(string.Empty, SqlTypeName.Parse(null).BaseName);
        Assert.Equal(string.Empty, SqlTypeName.Parse("").BaseName);
        Assert.Equal(string.Empty, SqlTypeName.Parse("   ").ToString());
    }

    [Fact]
    public void Parse_tolerates_malformed_arguments()
    {
        var bad = SqlTypeName.Parse("decimal(x,y)");
        Assert.Equal("decimal", bad.BaseName);
        Assert.Null(bad.Precision);
        Assert.Null(bad.Scale);

        var unclosed = SqlTypeName.Parse("nvarchar(50");
        Assert.Equal("nvarchar", unclosed.BaseName);
        Assert.Equal(50, unclosed.Length);
    }

    [Theory]
    [InlineData("nvarchar(50)")]
    [InlineData("varbinary(max)")]
    [InlineData("decimal(10,2)")]
    [InlineData("datetime2(3)")]
    [InlineData("int")]
    [InlineData("dbo.MyType")]
    public void Parse_then_ToString_round_trips(string declared)
    {
        Assert.Equal(declared, SqlTypeName.Parse(declared).ToString());
    }

    [Theory]
    [InlineData("char", true)]
    [InlineData("nchar", true)]
    [InlineData("varchar", true)]
    [InlineData("nvarchar", true)]
    [InlineData("text", true)]
    [InlineData("ntext", true)]
    [InlineData("xml", true)]
    [InlineData("sql_variant", true)]
    [InlineData("NVARCHAR", true)]
    [InlineData("int", false)]
    [InlineData("bit", false)]
    [InlineData("varbinary", false)]
    [InlineData("date", false)]
    [InlineData("uniqueidentifier", false)]
    [InlineData("dbo.MyType", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCharacterType_is_true_exactly_for_character_types(string? baseName, bool expected)
    {
        Assert.Equal(expected, SqlTypeName.IsCharacterType(baseName));
    }

    [Fact]
    public void IsCharacter_reads_the_parsed_base_name()
    {
        Assert.True(SqlTypeName.Parse("nvarchar(max)").IsCharacter);
        Assert.False(SqlTypeName.Parse("decimal(10,2)").IsCharacter);
    }
}
