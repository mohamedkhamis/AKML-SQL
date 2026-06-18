using System;
using System.Data;
using System.Globalization;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// Spec 030 — Phase 5. The SINGLE source of truth for SAFE, invariant-culture, round-trippable
    /// SQL-scalar ↔ string encoding. The read path (<see cref="ResultSetReader"/>) calls
    /// <see cref="Encode"/> to stringify each cell; the write path (<see cref="CrudWriteGenerator"/>)
    /// calls <see cref="Decode"/> to turn the same text back into a typed parameter value. Because
    /// both run engine-side on net10.0 the encode/decode pair is symmetric.
    ///
    /// <para>No <c>object[][]</c> ever crosses the wire — the repo configures only the implicit
    /// MessagePack StandardResolver, which cannot serialize an <c>object</c> member and would lose
    /// SQL type fidelity even with Typeless. Cells travel as <c>string?</c> (null == SQL NULL).</para>
    /// </summary>
    public static class SqlScalarEncoder
    {
        /// <summary>
        /// Stringify one cell value for the wire. Returns <c>null</c> for <see cref="DBNull"/> / null
        /// (the receiver treats a null array element as SQL NULL). Formatting is invariant-culture and
        /// chosen to be loss-free for <see cref="Decode"/>:
        ///   datetime/datetime2 → ISO-8601 "o"; datetimeoffset → "o"; uniqueidentifier → "D";
        ///   varbinary/binary/timestamp → Base64; bit → "0"/"1"; decimal/float/real → invariant string;
        ///   everything else → invariant ToString.
        /// </summary>
        public static string? Encode(object? value)
        {
            if (value is null || value is DBNull) return null;

            switch (value)
            {
                case bool b:
                    return b ? "1" : "0";
                case byte[] bytes:
                    return Convert.ToBase64String(bytes);
                case Guid g:
                    return g.ToString("D", CultureInfo.InvariantCulture);
                case DateTimeOffset dto:
                    return dto.ToString("o", CultureInfo.InvariantCulture);
                case DateTime dt:
                    return dt.ToString("o", CultureInfo.InvariantCulture);
                case TimeSpan ts:
                    // time(n) surfaces as TimeSpan via Microsoft.Data.SqlClient.
                    return ts.ToString("c", CultureInfo.InvariantCulture);
                case decimal dec:
                    return dec.ToString(CultureInfo.InvariantCulture);
                case double dbl:
                    // Parameterless invariant ToString IS the shortest round-trippable form on net10.0.
                    return dbl.ToString(CultureInfo.InvariantCulture);
                case float fl:
                    return fl.ToString(CultureInfo.InvariantCulture);
                case string s:
                    return s;
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        /// <summary>
        /// Parse wire text back into a CLR value suitable for a <see cref="SqlParameter"/> of the given
        /// <see cref="SqlDbType"/>. Returns <see cref="DBNull.Value"/> when <paramref name="text"/> is
        /// null. The exact inverse of <see cref="Encode"/> for the supported SQL types.
        /// </summary>
        public static object Decode(string? text, SqlDbType sqlDbType)
        {
            if (text is null) return DBNull.Value;

            switch (sqlDbType)
            {
                case SqlDbType.Bit:
                    // Accept "0"/"1" (our encoding) and also "true"/"false" defensively.
                    if (text == "1") return true;
                    if (text == "0") return false;
                    return bool.Parse(text);

                case SqlDbType.TinyInt:
                    return byte.Parse(text, CultureInfo.InvariantCulture);
                case SqlDbType.SmallInt:
                    return short.Parse(text, CultureInfo.InvariantCulture);
                case SqlDbType.Int:
                    return int.Parse(text, CultureInfo.InvariantCulture);
                case SqlDbType.BigInt:
                    return long.Parse(text, CultureInfo.InvariantCulture);

                case SqlDbType.Decimal:
                case SqlDbType.Money:
                case SqlDbType.SmallMoney:
                    return decimal.Parse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

                case SqlDbType.Float:
                    return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                case SqlDbType.Real:
                    return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

                case SqlDbType.UniqueIdentifier:
                    return Guid.Parse(text);

                case SqlDbType.Binary:
                case SqlDbType.VarBinary:
                case SqlDbType.Image:
                case SqlDbType.Timestamp:
                    return Convert.FromBase64String(text);

                case SqlDbType.Date:
                case SqlDbType.DateTime:
                case SqlDbType.DateTime2:
                case SqlDbType.SmallDateTime:
                    return DateTime.ParseExact(text, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

                case SqlDbType.DateTimeOffset:
                    return DateTimeOffset.ParseExact(text, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

                case SqlDbType.Time:
                    return TimeSpan.ParseExact(text, "c", CultureInfo.InvariantCulture);

                // Char / NChar / VarChar / NVarChar / Text / NText / Xml / Variant → keep the string.
                default:
                    return text;
            }
        }

        /// <summary>
        /// Map a <see cref="SqlDbType"/> to the per-column CLR hint shipped in
        /// <see cref="ExecuteResultSet.ClrTypeHints"/> so the browser can parse each cell's text back.
        /// </summary>
        public static int ClrHint(SqlDbType sqlDbType)
        {
            switch (sqlDbType)
            {
                case SqlDbType.Bit:
                    return ClrTypeHint.Bool;
                case SqlDbType.TinyInt:
                case SqlDbType.SmallInt:
                case SqlDbType.Int:
                case SqlDbType.BigInt:
                    return ClrTypeHint.Int64;
                case SqlDbType.Decimal:
                case SqlDbType.Money:
                case SqlDbType.SmallMoney:
                    return ClrTypeHint.Decimal;
                case SqlDbType.Float:
                case SqlDbType.Real:
                    return ClrTypeHint.Double;
                case SqlDbType.UniqueIdentifier:
                    return ClrTypeHint.Guid;
                case SqlDbType.Binary:
                case SqlDbType.VarBinary:
                case SqlDbType.Image:
                case SqlDbType.Timestamp:
                    return ClrTypeHint.Binary;
                case SqlDbType.Date:
                case SqlDbType.DateTime:
                case SqlDbType.DateTime2:
                case SqlDbType.SmallDateTime:
                    return ClrTypeHint.DateTime;
                case SqlDbType.DateTimeOffset:
                    return ClrTypeHint.DateTimeOffset;
                case SqlDbType.Variant:
                    // Correct mapping — but in PRACTICE this is rarely emitted: ResolveSqlDbType infers
                    // the SqlDbType from the reader's CLR field type, which is System.Object for a variant
                    // column → InferSqlDbType returns NVarChar. So a variant cell typically surfaces as
                    // invariant text and round-trips as a string (its per-cell underlying type is not
                    // preserved — an accepted limitation).
                    return ClrTypeHint.Variant;
                default:
                    return ClrTypeHint.String;
            }
        }

        /// <summary>
        /// Best-effort map from a CLR <see cref="Type"/> (DbColumn.DataType) to a <see cref="SqlDbType"/>,
        /// used when a column's provider type code is unavailable. Defaults to NVarChar.
        /// </summary>
        public static SqlDbType InferSqlDbType(Type? clrType)
        {
            if (clrType is null) return SqlDbType.NVarChar;
            var t = Nullable.GetUnderlyingType(clrType) ?? clrType;
            if (t == typeof(bool)) return SqlDbType.Bit;
            if (t == typeof(byte)) return SqlDbType.TinyInt;
            if (t == typeof(short)) return SqlDbType.SmallInt;
            if (t == typeof(int)) return SqlDbType.Int;
            if (t == typeof(long)) return SqlDbType.BigInt;
            if (t == typeof(decimal)) return SqlDbType.Decimal;
            if (t == typeof(double)) return SqlDbType.Float;
            if (t == typeof(float)) return SqlDbType.Real;
            if (t == typeof(Guid)) return SqlDbType.UniqueIdentifier;
            if (t == typeof(byte[])) return SqlDbType.VarBinary;
            if (t == typeof(DateTimeOffset)) return SqlDbType.DateTimeOffset;
            if (t == typeof(DateTime)) return SqlDbType.DateTime2;
            if (t == typeof(TimeSpan)) return SqlDbType.Time;
            return SqlDbType.NVarChar;
        }
    }
}
