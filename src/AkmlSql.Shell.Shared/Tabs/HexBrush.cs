#nullable enable
using System;
using System.Collections.Concurrent;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Spec 040 — the one hex → colour / frozen brush conversion for environment colours (tab
    /// colouring, History environment badges, the Color options page).
    /// </summary>
    internal static class HexBrush
    {
        private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache =
            new ConcurrentDictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Parses <c>#RRGGBB</c> (the leading <c>#</c> is optional). False for empty or invalid input.</summary>
        public static bool TryParse(string? hex, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;

            var value = hex!.Trim();
            if (!value.StartsWith("#", StringComparison.Ordinal)) value = "#" + value;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>A frozen brush for <paramref name="hex"/>, cached per string; transparent when it doesn't parse.</summary>
        public static SolidColorBrush Get(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
            return Cache.GetOrAdd(hex!.Trim(), key =>
            {
                var brush = new SolidColorBrush(TryParse(key, out var color) ? color : Colors.Transparent);
                brush.Freeze();
                return brush;
            });
        }

        /// <summary>Black or white, whichever reads better on <paramref name="hex"/> (BT.601 luminance).</summary>
        public static SolidColorBrush ContrastFor(string? hex)
        {
            if (!TryParse(hex, out var color)) return Brushes.Black;
            var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return luminance > 0.55 ? Brushes.Black : Brushes.White;
        }
    }
}
