using System;
using System.Globalization;
using System.Text;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Core.Text
{
    /// <summary>How one result value reads in the grid.</summary>
    public sealed class DisplayValue
    {
        public DisplayValue(string text, bool isNull = false, bool isEmptyString = false, bool isEngineCut = false, bool isPreview = false)
        {
            Text = text;
            IsNull = isNull;
            IsEmptyString = isEmptyString;
            IsEngineCut = isEngineCut;
            IsPreview = isPreview;
        }

        /// <summary>The text shown in the cell.</summary>
        public string Text { get; }

        /// <summary>SQL NULL (shown as <c>NULL</c> in a muted style).</summary>
        public bool IsNull { get; }

        /// <summary>An empty string — shown empty, told apart from NULL.</summary>
        public bool IsEmptyString { get; }

        /// <summary>The engine sent only the value's size (<c>[text N chars]</c> / <c>[binary N bytes]</c>).</summary>
        public bool IsEngineCut { get; }

        /// <summary>The value is longer than the cell shows (a large binary value's first bytes).</summary>
        public bool IsPreview { get; }
    }

    /// <summary>
    /// Spec 041 (research R11, FR-026) — formats a result value the way SSMS shows it, type by type:
    /// <c>date</c> as yyyy-MM-dd, <c>datetime</c> with milliseconds, <c>datetime2(n)</c>/<c>time(n)</c>/
    /// <c>datetimeoffset(n)</c> with the declared fractional digits (an offset after a space), binary as
    /// <c>0x</c> and upper-case hex, uniqueidentifier upper case, money with four decimals. Display only:
    /// the editor and Apply keep the engine's round-trip wire text, so nothing the user edits is lost.
    /// A value that will not parse is shown as it came.
    /// </summary>
    public static class SqlValueDisplay
    {
        /// <summary>Binary values up to this many bytes are shown in full.</summary>
        public const int BinaryFullBytes = 1024;

        /// <summary>Bytes shown for a larger binary value (followed by "…").</summary>
        public const int BinaryPreviewBytes = 512;

        private const int DefaultFraction = 7;

        /// <param name="fullText">Return the whole value even where the cell shows a preview (copy, save, View value).</param>
        public static DisplayValue Format(string? wire, SqlTypeName type, int clrHint, bool fullText = false)
        {
            if (wire == null) return new DisplayValue(GridTextFormats.NullText, isNull: true);

            var name = (type ?? SqlTypeName.Parse(null)).BaseName;
            // An empty binary value is "0x" in SSMS, not an empty string.
            if (wire.Length == 0 && !IsBinary(name, clrHint)) return new DisplayValue(string.Empty, isEmptyString: true);
            if (EngineCutIndicator.IsIndicator(wire)) return new DisplayValue(wire, isEngineCut: true);

            if (IsBinary(name, clrHint)) return BinaryText(wire, fullText);

            switch (name)
            {
                case "date": return new DisplayValue(DateOnly(wire));
                case "smalldatetime": return new DisplayValue(DateTimeText(wire, 0));
                case "datetime": return new DisplayValue(DateTimeText(wire, 3));
                case "datetime2": return new DisplayValue(DateTimeText(wire, type!.Scale ?? DefaultFraction));
                case "time": return new DisplayValue(TimeText(wire, type!.Scale ?? DefaultFraction));
                case "datetimeoffset": return new DisplayValue(OffsetText(wire, type!.Scale ?? DefaultFraction));
                case "uniqueidentifier": return new DisplayValue(wire.ToUpperInvariant());
                case "money":
                case "smallmoney": return new DisplayValue(MoneyText(wire));
                case "":
                    break;
                default:
                    return new DisplayValue(wire);
            }

            // No declared type (an older engine, or a computed column): fall back to the CLR hint.
            switch (clrHint)
            {
                case ClrTypeHint.DateTime: return new DisplayValue(DateTimeText(wire, DefaultFraction));
                case ClrTypeHint.DateTimeOffset: return new DisplayValue(OffsetText(wire, DefaultFraction));
                case ClrTypeHint.Guid: return new DisplayValue(wire.ToUpperInvariant());
                default: return new DisplayValue(wire);
            }
        }

        private static bool IsBinary(string name, int clrHint)
        {
            switch (name)
            {
                case "binary":
                case "varbinary":
                case "image":
                case "timestamp":
                case "rowversion":
                    return true;
                case "":
                    return clrHint == ClrTypeHint.Binary;
                default:
                    return false;
            }
        }

        private static string DateOnly(string wire)
        {
            return DateTime.TryParse(wire, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : wire;
        }

        private static string DateTimeText(string wire, int fraction)
        {
            if (!DateTime.TryParse(wire, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)) return wire;
            return dt.ToString("yyyy-MM-dd HH:mm:ss" + FractionFormat(fraction), CultureInfo.InvariantCulture);
        }

        private static string OffsetText(string wire, int fraction)
        {
            if (!DateTimeOffset.TryParse(wire, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)) return wire;
            // SSMS separates the offset with a space: 2025-10-01 23:57:23.8800000 +02:00.
            return dto.ToString("yyyy-MM-dd HH:mm:ss" + FractionFormat(fraction) + " zzz", CultureInfo.InvariantCulture);
        }

        private static string TimeText(string wire, int fraction)
        {
            if (!TimeSpan.TryParseExact(wire, "c", CultureInfo.InvariantCulture, out var ts)) return wire;
            var sb = new StringBuilder(ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
            int f = Clamp(fraction);
            if (f > 0)
            {
                long ticks = ts.Ticks % TimeSpan.TicksPerSecond;   // 7 digits of fraction
                sb.Append('.').Append(ticks.ToString("0000000", CultureInfo.InvariantCulture).Substring(0, f));
            }
            return sb.ToString();
        }

        private static DisplayValue BinaryText(string wire, bool fullText)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(wire); }
            catch (FormatException) { return new DisplayValue(wire); }

            bool preview = !fullText && bytes.Length > BinaryFullBytes;
            int count = preview ? BinaryPreviewBytes : bytes.Length;
            var sb = new StringBuilder(2 + count * 2 + 1);
            sb.Append("0x");
            for (int i = 0; i < count; i++) sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            if (preview) sb.Append('…');
            return new DisplayValue(sb.ToString(), isPreview: preview);
        }

        private static string MoneyText(string wire)
        {
            return decimal.TryParse(wire, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
                ? m.ToString("0.0000", CultureInfo.InvariantCulture)
                : wire;
        }

        private static string FractionFormat(int fraction)
        {
            int f = Clamp(fraction);
            return f == 0 ? string.Empty : "." + new string('f', f);
        }

        private static int Clamp(int fraction) => fraction < 0 ? 0 : fraction > 7 ? 7 : fraction;
    }
}
