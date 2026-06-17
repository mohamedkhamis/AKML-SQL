using System;
using System.Data;
using AkmlSql.Engine.Execution;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Spec 030 — Phase 5. Round-trip tests for the SINGLE encode/decode source of truth. Because the
/// read path encodes and the write path decodes the same text, these prove no SQL-scalar fidelity is
/// lost on the wire (locked constraint #2/#4: SAFE string encoding, no object[][]).
/// </summary>
public sealed class SqlScalarEncoderTests
{
    [Fact]
    public void Null_EncodesToNull_DecodesToDBNull()
    {
        Assert.Null(SqlScalarEncoder.Encode(null));
        Assert.Null(SqlScalarEncoder.Encode(DBNull.Value));
        Assert.Equal(DBNull.Value, SqlScalarEncoder.Decode(null, SqlDbType.Int));
        Assert.Equal(DBNull.Value, SqlScalarEncoder.Decode(null, SqlDbType.NVarChar));
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void Bit_RoundTrips(bool value, string expectedText)
    {
        var text = SqlScalarEncoder.Encode(value);
        Assert.Equal(expectedText, text);
        Assert.Equal(value, SqlScalarEncoder.Decode(text, SqlDbType.Bit));
    }

    [Fact]
    public void Int_RoundTrips()
    {
        var text = SqlScalarEncoder.Encode(1234567890);
        Assert.Equal("1234567890", text);
        Assert.Equal(1234567890, SqlScalarEncoder.Decode(text, SqlDbType.Int));
    }

    [Fact]
    public void BigInt_RoundTrips()
    {
        long v = 9_223_372_036_854_775_807L;
        var text = SqlScalarEncoder.Encode(v);
        Assert.Equal(v, SqlScalarEncoder.Decode(text, SqlDbType.BigInt));
    }

    [Fact]
    public void Decimal_RoundTrips_InvariantCulture()
    {
        decimal v = 12345.6789m;
        var text = SqlScalarEncoder.Encode(v);
        Assert.Equal("12345.6789", text);
        Assert.Equal(v, SqlScalarEncoder.Decode(text, SqlDbType.Decimal));
    }

    [Fact]
    public void Decimal_PreservesTrailingZeros_Scale()
    {
        decimal v = 100.00m;
        var text = SqlScalarEncoder.Encode(v);
        // invariant ToString preserves the decimal's scale.
        Assert.Equal("100.00", text);
        Assert.Equal(v, (decimal)SqlScalarEncoder.Decode(text, SqlDbType.Decimal));
    }

    [Fact]
    public void Double_RoundTrips()
    {
        double v = 3.141592653589793;
        var text = SqlScalarEncoder.Encode(v);
        Assert.Equal(v, SqlScalarEncoder.Decode(text, SqlDbType.Float));
    }

    [Fact]
    public void Float_Real_RoundTrips()
    {
        float v = 2.5f;
        var text = SqlScalarEncoder.Encode(v);
        Assert.Equal(v, (float)SqlScalarEncoder.Decode(text, SqlDbType.Real));
    }

    [Fact]
    public void Guid_RoundTrips_CanonicalD()
    {
        var g = Guid.Parse("0fd3a5d8-1f4b-4c2e-9a7b-2e6c1d8a5b30");
        var text = SqlScalarEncoder.Encode(g);
        Assert.Equal("0fd3a5d8-1f4b-4c2e-9a7b-2e6c1d8a5b30", text);
        Assert.Equal(g, SqlScalarEncoder.Decode(text, SqlDbType.UniqueIdentifier));
    }

    [Fact]
    public void DateTime2_RoundTrips_Iso8601O()
    {
        var dt = new DateTime(2026, 6, 17, 13, 45, 12, 345, DateTimeKind.Unspecified).AddTicks(6789);
        var text = SqlScalarEncoder.Encode(dt);
        var decoded = (DateTime)SqlScalarEncoder.Decode(text, SqlDbType.DateTime2);
        Assert.Equal(dt, decoded);
        Assert.Equal(dt.Ticks, decoded.Ticks); // sub-second precision preserved by "o".
    }

    [Fact]
    public void DateTimeOffset_RoundTrips_WithOffset()
    {
        var dto = new DateTimeOffset(2026, 6, 17, 13, 45, 12, TimeSpan.FromHours(-5)).AddTicks(1234567);
        var text = SqlScalarEncoder.Encode(dto);
        var decoded = (DateTimeOffset)SqlScalarEncoder.Decode(text, SqlDbType.DateTimeOffset);
        Assert.Equal(dto, decoded);
        Assert.Equal(dto.Offset, decoded.Offset);
    }

    [Fact]
    public void VarBinary_RoundTrips_Base64()
    {
        byte[] bytes = { 0x00, 0x01, 0xFE, 0xFF, 0x10, 0x20, 0x7F };
        var text = SqlScalarEncoder.Encode(bytes);
        Assert.Equal(Convert.ToBase64String(bytes), text);
        Assert.Equal(bytes, (byte[])SqlScalarEncoder.Decode(text, SqlDbType.VarBinary));
    }

    [Fact]
    public void Timestamp_RoundTrips_Base64()
    {
        byte[] rowVersion = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x07, 0xD1 };
        var text = SqlScalarEncoder.Encode(rowVersion);
        Assert.Equal(rowVersion, (byte[])SqlScalarEncoder.Decode(text, SqlDbType.Timestamp));
    }

    [Fact]
    public void NVarChar_PreservesUnicodeAndSpecials()
    {
        var s = "O'Brien — “quoted” 日本語 \t tab; semic=eq";
        var text = SqlScalarEncoder.Encode(s);
        Assert.Equal(s, text);
        Assert.Equal(s, SqlScalarEncoder.Decode(text, SqlDbType.NVarChar));
    }

    [Fact]
    public void SqlVariant_KeptAsString()
    {
        // sql_variant arrives boxed; for a string payload the encoder keeps the text and the decoder
        // (Variant) returns it verbatim (the per-cell ClrTypeHint tells the client how to interpret it).
        var text = SqlScalarEncoder.Encode("variant-payload-42");
        Assert.Equal("variant-payload-42", SqlScalarEncoder.Decode(text, SqlDbType.Variant));
    }

    [Theory]
    [InlineData(SqlDbType.Int, AkmlSql.Core.Ipc.Messages.ClrTypeHint.Int64)]
    [InlineData(SqlDbType.Bit, AkmlSql.Core.Ipc.Messages.ClrTypeHint.Bool)]
    [InlineData(SqlDbType.Decimal, AkmlSql.Core.Ipc.Messages.ClrTypeHint.Decimal)]
    [InlineData(SqlDbType.Float, AkmlSql.Core.Ipc.Messages.ClrTypeHint.Double)]
    [InlineData(SqlDbType.UniqueIdentifier, AkmlSql.Core.Ipc.Messages.ClrTypeHint.Guid)]
    [InlineData(SqlDbType.VarBinary, AkmlSql.Core.Ipc.Messages.ClrTypeHint.Binary)]
    [InlineData(SqlDbType.DateTime2, AkmlSql.Core.Ipc.Messages.ClrTypeHint.DateTime)]
    [InlineData(SqlDbType.DateTimeOffset, AkmlSql.Core.Ipc.Messages.ClrTypeHint.DateTimeOffset)]
    [InlineData(SqlDbType.Variant, AkmlSql.Core.Ipc.Messages.ClrTypeHint.Variant)]
    [InlineData(SqlDbType.NVarChar, AkmlSql.Core.Ipc.Messages.ClrTypeHint.String)]
    public void ClrHint_MapsExpected(SqlDbType sqlDbType, int expectedHint)
    {
        Assert.Equal(expectedHint, SqlScalarEncoder.ClrHint(sqlDbType));
    }
}
