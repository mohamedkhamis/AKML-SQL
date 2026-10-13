using System;
using System.Globalization;

namespace AkmlSql.Core.Text
{
    /// <summary>
    /// Spec 041 (research R10) — the declared SQL Server type of a result column, as SSMS shows it:
    /// <c>nvarchar(50)</c>, <c>nvarchar(max)</c>, <c>decimal(10,2)</c>, <c>datetime2(3)</c>, or a bare
    /// name (<c>int</c>, <c>date</c>, <c>dbo.MyType</c>).
    ///
    /// <para>
    /// The engine composes it with <see cref="Format"/> from <c>DbColumn</c> metadata into the existing
    /// <c>ExecuteResultSet.ColumnSqlTypes</c> string; the web edition reads it back with
    /// <see cref="Parse"/> to format values (fractional-second digits, decimal scale) and to tell
    /// character columns from the rest. Which suffix a type takes is decided by its base name, never by
    /// the precision/scale values: SqlClient reports a real precision for int (10) and float (15) and
    /// the sentinel 255 wherever a value does not apply, so "precision != 255" is not a signal.
    /// </para>
    /// <para>Pure C#, netstandard2.0-safe; an older engine's bare names parse to a name with no
    /// arguments.</para>
    /// </summary>
    public sealed class SqlTypeName
    {
        /// <summary>SqlClient's "not applicable" value for <c>NumericPrecision</c>/<c>NumericScale</c>.</summary>
        private const int NotApplicable = 255;

        private SqlTypeName(string baseName, int? length, bool isMax, int? precision, int? scale)
        {
            BaseName = baseName;
            Length = length;
            IsMax = isMax;
            Precision = precision;
            Scale = scale;
        }

        /// <summary>The type name without arguments, lower-case for system types; a user-defined
        /// type keeps its spelling (<c>dbo.MyType</c>). Empty when unknown.</summary>
        public string BaseName { get; }

        /// <summary>The declared length of a length type (<c>nvarchar(50)</c> → 50); null otherwise
        /// and for <c>(max)</c>.</summary>
        public int? Length { get; }

        /// <summary>True for a <c>(max)</c> length type.</summary>
        public bool IsMax { get; }

        /// <summary>Precision of <c>decimal(p,s)</c>; null otherwise.</summary>
        public int? Precision { get; }

        /// <summary>Scale of <c>decimal(p,s)</c>, or the fractional-second digits of
        /// <c>datetime2</c>/<c>time</c>/<c>datetimeoffset</c>; null when not declared.</summary>
        public int? Scale { get; }

        /// <summary>True for a type whose emptied cell is an empty string rather than invalid
        /// (spec 041 FR-014).</summary>
        public bool IsCharacter => IsCharacterType(BaseName);

        /// <summary>
        /// Compose the declared type from column metadata. <paramref name="columnSize"/> is the
        /// length in characters (bytes for binary types), with <see cref="int.MaxValue"/> or -1
        /// meaning <c>(max)</c>.
        /// </summary>
        public static string Format(string? baseName, int? columnSize, int? precision, int? scale)
        {
            var name = Normalise(baseName);
            if (name.Length == 0) return string.Empty;

            switch (Kind(name))
            {
                case TypeKind.Length:
                    if (columnSize == null) return name;
                    if (columnSize.Value == int.MaxValue || columnSize.Value == -1) return name + "(max)";
                    if (columnSize.Value <= 0) return name;
                    return name + "(" + columnSize.Value.ToString(CultureInfo.InvariantCulture) + ")";

                case TypeKind.Decimal:
                    if (precision == null || precision.Value == NotApplicable || precision.Value <= 0) return name;
                    int s = scale == null || scale.Value == NotApplicable || scale.Value < 0 ? 0 : scale.Value;
                    return name + "(" + precision.Value.ToString(CultureInfo.InvariantCulture)
                        + "," + s.ToString(CultureInfo.InvariantCulture) + ")";

                case TypeKind.FractionalSeconds:
                    if (scale == null || scale.Value < 0 || scale.Value > 7) return name;
                    return name + "(" + scale.Value.ToString(CultureInfo.InvariantCulture) + ")";

                default:
                    return name;
            }
        }

        /// <summary>Parse a declared type (or an older engine's bare name). Never throws.</summary>
        public static SqlTypeName Parse(string? text)
        {
            if (text == null) return new SqlTypeName(string.Empty, null, false, null, null);
            var trimmed = text.Trim();
            if (trimmed.Length == 0) return new SqlTypeName(string.Empty, null, false, null, null);

            int open = trimmed.IndexOf('(');
            var name = Normalise(open < 0 ? trimmed : trimmed.Substring(0, open));
            if (open < 0) return new SqlTypeName(name, null, false, null, null);

            int close = trimmed.IndexOf(')', open + 1);
            var inner = close < 0 ? trimmed.Substring(open + 1) : trimmed.Substring(open + 1, close - open - 1);
            var args = inner.Split(',');

            switch (Kind(name))
            {
                case TypeKind.Length:
                    var arg0 = args[0].Trim();
                    if (string.Equals(arg0, "max", StringComparison.OrdinalIgnoreCase))
                        return new SqlTypeName(name, null, true, null, null);
                    return new SqlTypeName(name, ParseInt(arg0), false, null, null);

                case TypeKind.Decimal:
                    var p = ParseInt(args[0]);
                    var sc = args.Length > 1 ? ParseInt(args[1]) : (p == null ? null : (int?)0);
                    if (p == null) sc = null;
                    return new SqlTypeName(name, null, false, p, sc);

                case TypeKind.FractionalSeconds:
                    return new SqlTypeName(name, null, false, null, ParseInt(args[0]));

                default:
                    return new SqlTypeName(name, null, false, null, null);
            }
        }

        /// <summary>True exactly for the types whose emptied cell is saved as an empty string:
        /// <c>char nchar varchar nvarchar text ntext xml sql_variant</c>.</summary>
        public static bool IsCharacterType(string? baseName)
        {
            switch (Normalise(baseName))
            {
                case "char":
                case "nchar":
                case "varchar":
                case "nvarchar":
                case "text":
                case "ntext":
                case "xml":
                case "sql_variant":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The declared form, e.g. <c>decimal(10,2)</c>; the bare name when no argument is known.</summary>
        public override string ToString()
        {
            if (BaseName.Length == 0) return string.Empty;
            if (IsMax) return BaseName + "(max)";
            switch (Kind(BaseName))
            {
                case TypeKind.Length:
                    return Length == null ? BaseName : BaseName + "(" + Length.Value.ToString(CultureInfo.InvariantCulture) + ")";
                case TypeKind.Decimal:
                    return Precision == null ? BaseName
                        : BaseName + "(" + Precision.Value.ToString(CultureInfo.InvariantCulture) + ","
                          + (Scale ?? 0).ToString(CultureInfo.InvariantCulture) + ")";
                case TypeKind.FractionalSeconds:
                    return Scale == null ? BaseName : BaseName + "(" + Scale.Value.ToString(CultureInfo.InvariantCulture) + ")";
                default:
                    return BaseName;
            }
        }

        private enum TypeKind { Other, Length, Decimal, FractionalSeconds }

        private static TypeKind Kind(string name)
        {
            switch (name)
            {
                case "char":
                case "varchar":
                case "nchar":
                case "nvarchar":
                case "binary":
                case "varbinary":
                    return TypeKind.Length;
                case "decimal":
                case "numeric":
                    return TypeKind.Decimal;
                case "datetime2":
                case "time":
                case "datetimeoffset":
                    return TypeKind.FractionalSeconds;
                default:
                    return TypeKind.Other;
            }
        }

        /// <summary>Lower-case a system type name (and report <c>numeric</c> as <c>decimal</c>, as
        /// SqlClient does); keep a user-defined (dotted) type name as given.</summary>
        private static string Normalise(string? name)
        {
            if (name == null) return string.Empty;
            var t = name.Trim();
            if (t.Length == 0) return string.Empty;
            if (t.IndexOf('.') >= 0) return t;
            var lower = t.ToLowerInvariant();
            return lower == "numeric" ? "decimal" : lower;
        }

        private static int? ParseInt(string s)
        {
            int v;
            return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : (int?)null;
        }
    }
}
