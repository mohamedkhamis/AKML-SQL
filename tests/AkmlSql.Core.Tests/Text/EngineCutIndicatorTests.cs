using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>
/// Spec 041 (T035, research R8) — the engine replaces a very large text or binary cell with an
/// in-band indicator (<c>[text N chars]</c> / <c>[binary N bytes]</c>, emitted only by
/// <c>ResultSetReader.EncodeCell</c>). The grid must recognise exactly those forms so it can say
/// the value was cut by the engine, copy it verbatim and count it — and must never mistake an
/// ordinary value for one.
/// </summary>
public class EngineCutIndicatorTests
{
    [Fact]
    public void Text_indicator_is_recognised_with_its_size()
    {
        Assert.True(EngineCutIndicator.TryParse("[text 123 chars]", out var kind, out var size));
        Assert.Equal(EngineCutKind.Text, kind);
        Assert.Equal(123, size);
    }

    [Fact]
    public void Binary_indicator_is_recognised_with_its_size()
    {
        Assert.True(EngineCutIndicator.TryParse("[binary 9 bytes]", out var kind, out var size));
        Assert.Equal(EngineCutKind.Binary, kind);
        Assert.Equal(9, size);
    }

    [Fact]
    public void Large_sizes_fit()
    {
        Assert.True(EngineCutIndicator.TryParse("[text 5000000000 chars]", out _, out var size));
        Assert.Equal(5_000_000_000L, size);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("[text 12 chars] and more")]
    [InlineData("x[text 12 chars]")]
    [InlineData("[text 12 chars")]
    [InlineData("[text  12 chars]")]
    [InlineData("[ text 12 chars]")]
    [InlineData("[TEXT 12 CHARS]")]
    [InlineData("[text 12 bytes]")]
    [InlineData("[binary 12 chars]")]
    [InlineData("[text -1 chars]")]
    [InlineData("[text 1.5 chars]")]
    [InlineData("[binary bytes]")]
    public void Anything_else_is_not_an_indicator(string? value)
    {
        Assert.False(EngineCutIndicator.TryParse(value, out var kind, out var size));
        Assert.Equal(EngineCutKind.None, kind);
        Assert.Equal(0, size);
        Assert.False(EngineCutIndicator.IsIndicator(value));
    }

    [Fact]
    public void IsIndicator_matches_TryParse()
    {
        Assert.True(EngineCutIndicator.IsIndicator("[text 70000 chars]"));
        Assert.True(EngineCutIndicator.IsIndicator("[binary 70000 bytes]"));
    }
}
