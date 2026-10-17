using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// Spec 042 (user story 7, contracts/ipc.md "Appended keys for formatting") - the keys the PostgreSQL formatter added to the
/// formatting requests: the style as JSON (key 6 on the three document requests) and the sample's language (key 3 on the preview).
/// Each round-trips, and a payload written before the key existed still loads with the field at its "as today" value.
/// </summary>
public class PostgreSqlFormatContractTests
{
    private static T RoundTrip<T>(T value) => MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(value));

    [Fact]
    public void The_style_travels_as_key_6_on_the_three_document_requests()
    {
        const string style = "{\"sqlPrompt\":{}}";

        Assert.Equal(style, RoundTrip(new FormatRequest { Text = "select 1", ProfileJson = style }).ProfileJson);
        Assert.Equal(style, RoundTrip(new FormatSelectionRequest { Text = "select 1", ProfileJson = style }).ProfileJson);
        Assert.Equal(style, RoundTrip(new FormatActionRequest { Text = "select 1", ProfileJson = style }).ProfileJson);

        Assert.Equal(7, MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(new FormatRequest { ProfileJson = style })).Length);
    }

    [Fact]
    public void A_request_written_before_key_6_has_no_inline_style()
    {
        var legacy = MessagePackSerializer.Serialize(new object?[] { "s1", "select 1", "Default", null, null, null });

        Assert.Null(MessagePackSerializer.Deserialize<FormatRequest>(legacy).ProfileJson);
        Assert.Null(MessagePackSerializer.Deserialize<FormatSelectionRequest>(MessagePackSerializer.Serialize(new object?[] { "s1", "select 1", 0, 1, "Default", null })).ProfileJson);
        Assert.Null(MessagePackSerializer.Deserialize<FormatActionRequest>(MessagePackSerializer.Serialize(new object?[] { "s1", "select 1", 0, "Default", 0, 0 })).ProfileJson);
    }

    [Fact]
    public void The_preview_states_the_language_of_its_sample_as_key_3()
    {
        var postgres = RoundTrip(new FormatPreviewRequest { SampleText = "select 1", ProfileJson = "{}", Dialect = 1 });
        Assert.Equal(1, postgres.Dialect);

        var legacy = MessagePackSerializer.Deserialize<FormatPreviewRequest>(MessagePackSerializer.Serialize(new object?[] { "s1", "select 1", "{}" }));
        Assert.Equal(0, legacy.Dialect);      // an older peer's preview is T-SQL
    }
}
