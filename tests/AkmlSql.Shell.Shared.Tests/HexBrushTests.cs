#nullable enable
using System.Windows.Media;
using AkmlSql.Shell.Shared.Tabs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>Spec 040 (T006) — the shared hex → frozen brush helper.</summary>
    public class HexBrushTests
    {
        [Theory]
        [InlineData("#FF4444", 0xFF, 0x44, 0x44)]
        [InlineData("44BB44", 0x44, 0xBB, 0x44)]
        [InlineData(" #4488ff ", 0x44, 0x88, 0xFF)]
        public void Valid_hex_parses(string hex, byte r, byte g, byte b)
        {
            Assert.True(HexBrush.TryParse(hex, out var color));
            Assert.Equal(Color.FromRgb(r, g, b), color);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("#GGGGGG")]
        [InlineData("not a colour")]
        public void Invalid_input_does_not_parse_and_gives_a_transparent_brush(string? hex)
        {
            Assert.False(HexBrush.TryParse(hex, out _));
            Assert.Equal(Colors.Transparent, HexBrush.Get(hex).Color);
        }

        [Fact]
        public void Brushes_are_frozen_and_cached_per_hex()
        {
            var first = HexBrush.Get("#FFB800");
            var second = HexBrush.Get("#ffb800");

            Assert.True(first.IsFrozen);
            Assert.Same(first, second);
            Assert.Equal(Color.FromRgb(0xFF, 0xB8, 0x00), first.Color);
        }
    }
}
