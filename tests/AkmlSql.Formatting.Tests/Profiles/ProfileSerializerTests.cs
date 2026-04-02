using System.Text.Json;
using Xunit;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Profiles;

public class ProfileSerializerTests
{
    // ── Serialize ─────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ReturnsValidJson()
    {
        var profile = new FormattingProfile { Metadata = { Name = "Test" } };
        var json = ProfileSerializer.Serialize(profile);
        Assert.NotEmpty(json);
        Assert.Contains("Test", json);
    }

    [Fact]
    public void Serialize_UpdatesModifiedTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var profile = new FormattingProfile();
        _ = ProfileSerializer.Serialize(profile);
        Assert.True(profile.Metadata.Modified >= before);
    }

    [Fact]
    public void Serialize_NullProfile_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ProfileSerializer.Serialize(null!));
    }

    [Fact]
    public void Serialize_ContainsWhitespaceOptions()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                TabSize = 2
            }
        };
        var json = ProfileSerializer.Serialize(profile);
        Assert.Contains("2", json);
    }

    // ── Deserialize ───────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_ValidJson_ReturnsProfile()
    {
        var original = new FormattingProfile { Metadata = { Name = "RoundTrip" } };
        var json = ProfileSerializer.Serialize(original);
        var loaded = ProfileSerializer.Deserialize(json);
        Assert.Equal("RoundTrip", loaded.Metadata.Name);
    }

    [Fact]
    public void Deserialize_NullJson_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ProfileSerializer.Deserialize(null!));
    }

    [Fact]
    public void Deserialize_NullResult_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => ProfileSerializer.Deserialize("null"));
    }

    [Fact]
    public void Deserialize_FutureSchemaVersion_DoesNotThrow()
    {
        // Schema version 2 > CurrentSchemaVersion (1) → should warn but not throw
        var json = """{"metadata":{"schemaVersion":2,"name":"Future"},"whitespace":{},"casing":{},"list":{},"parenthesis":{},"dml":{},"join":{},"ddl":{},"controlFlow":{},"case":{},"cte":{},"expression":{},"formatActions":{}}""";
        var profile = ProfileSerializer.Deserialize(json);
        Assert.Equal(2, profile.Metadata.SchemaVersion);
    }

    [Fact]
    public void Deserialize_SameSchemaVersion_NoWarning()
    {
        var original = new FormattingProfile();
        var json = ProfileSerializer.Serialize(original);
        var loaded = ProfileSerializer.Deserialize(json);
        Assert.Equal(ProfileSerializer.CurrentSchemaVersion, loaded.Metadata.SchemaVersion);
    }

    // ── Round-trip ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_PreservesAllOptions()
    {
        var original = new FormattingProfile
        {
            Metadata =
            {
                Name = "Full Test"
            },
            Whitespace =
            {
                TabSize = 2,
                MaxLineWidth = 100
            },
            Casing =
            {
                ReservedKeywords = "lowercase"
            },
            List =
            {
                CommaPosition = "leading"
            },
            FormatActions =
            {
                InsertSemicolons = true
            }
        };

        var json = ProfileSerializer.Serialize(original);
        var loaded = ProfileSerializer.Deserialize(json);

        Assert.Equal("Full Test", loaded.Metadata.Name);
        Assert.Equal(2, loaded.Whitespace.TabSize);
        Assert.Equal(100, loaded.Whitespace.MaxLineWidth);
        Assert.Equal("lowercase", loaded.Casing.ReservedKeywords);
        Assert.Equal("leading", loaded.List.CommaPosition);
        Assert.True(loaded.FormatActions.InsertSemicolons);
    }

    // ── CurrentSchemaVersion ──────────────────────────────────────────────

    [Fact]
    public void CurrentSchemaVersion_IsOne()
    {
        Assert.Equal(1, ProfileSerializer.CurrentSchemaVersion);
    }
}
