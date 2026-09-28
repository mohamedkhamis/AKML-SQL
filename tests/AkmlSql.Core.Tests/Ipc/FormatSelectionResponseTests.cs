using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// Spec 040 (T084, STY-09) — Format Selection reports a style fallback the way Format Document
/// does: <see cref="FormatSelectionResponse.ProfileFallbackWarning"/> is key 7, appended, and an
/// older engine's 7-key reply still loads with it null.
/// </summary>
public class FormatSelectionResponseTests
{
    [Fact]
    public void Profile_fallback_warning_round_trips_as_key_7()
    {
        var bytes = MessagePackSerializer.Serialize(new FormatSelectionResponse
        {
            Success = true,
            FormattedText = "SELECT 1;",
            ProfileFallbackWarning = "Formatting style 'Gone' could not be loaded",
        });

        var back = MessagePackSerializer.Deserialize<FormatSelectionResponse>(bytes);
        Assert.Equal("Formatting style 'Gone' could not be loaded", back.ProfileFallbackWarning);

        // Positional layout: the warning is the 8th element (key 7).
        var array = MessagePackSerializer.Deserialize<object[]>(bytes);
        Assert.Equal(8, array.Length);
        Assert.Equal("Formatting style 'Gone' could not be loaded", array[7]);
    }

    [Fact]
    public void An_older_reply_without_key_7_loads_with_no_warning()
    {
        // Keys 0–6 as an older engine writes them.
        var legacy = MessagePackSerializer.Serialize(new object[] { true, "SELECT 1;", 0, 8, true, true, 3L });

        var back = MessagePackSerializer.Deserialize<FormatSelectionResponse>(legacy);

        Assert.True(back.Success);
        Assert.Equal("SELECT 1;", back.FormattedText);
        Assert.Null(back.ProfileFallbackWarning);
    }
}
