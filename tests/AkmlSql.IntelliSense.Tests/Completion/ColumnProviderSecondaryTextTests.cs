using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.IntelliSense.Tests.Completion;

/// <summary>
/// Spec 040 (T012, OPT-01) — the column detail text follows the three Suggestions › Behavior
/// flags. Every flag combination is covered; the defaults are today's text.
/// </summary>
public sealed class ColumnProviderSecondaryTextTests
{
    private static readonly Column Key = new()
    {
        ColumnName = "ProductID", TypeName = "int", IsNullable = false, IsPrimaryKey = true, IsIdentity = true,
    };

    private static readonly Column Computed = new()
    {
        ColumnName = "Total", TypeName = "decimal", Precision = 10, Scale = 2, IsNullable = true, IsComputed = true,
    };

    [Theory]
    [InlineData(true, true, true, "int, NOT NULL, PK, IDENTITY")]
    [InlineData(true, true, false, "int, NOT NULL")]
    [InlineData(true, false, true, "int, PK, IDENTITY")]
    [InlineData(true, false, false, "int")]
    [InlineData(false, true, true, "NOT NULL, PK, IDENTITY")]
    [InlineData(false, true, false, "NOT NULL")]
    [InlineData(false, false, true, "PK, IDENTITY")]
    [InlineData(false, false, false, "")]
    public void Key_column_text_follows_every_flag_combination(bool types, bool nullability, bool keys, string expected)
    {
        var provider = new ColumnProvider { ShowDataTypes = types, ShowNullability = nullability, ShowKeyIndicators = keys };

        Assert.Equal(expected, provider.FormatSecondaryText(Key));
    }

    [Theory]
    [InlineData(true, "decimal(10,2), NULL, COMPUTED")]
    [InlineData(false, "decimal(10,2), NULL")]
    public void Computed_follows_the_key_indicators_flag(bool keys, string expected)
    {
        var provider = new ColumnProvider { ShowKeyIndicators = keys };

        Assert.Equal(expected, provider.FormatSecondaryText(Computed));
    }

    [Fact]
    public void Table_suffix_follows_the_details()
        => Assert.Equal("int, NOT NULL, PK, IDENTITY • Products", new ColumnProvider().FormatSecondaryText(Key, "Products"));

    [Fact]
    public void Table_name_alone_when_every_detail_is_off()
    {
        var provider = new ColumnProvider { ShowDataTypes = false, ShowNullability = false, ShowKeyIndicators = false };

        Assert.Equal("Products", provider.FormatSecondaryText(Key, "Products"));
    }

    [Fact]
    public void Defaults_are_all_on()
    {
        var provider = new ColumnProvider();

        Assert.True(provider.ShowDataTypes);
        Assert.True(provider.ShowNullability);
        Assert.True(provider.ShowKeyIndicators);
    }
}
