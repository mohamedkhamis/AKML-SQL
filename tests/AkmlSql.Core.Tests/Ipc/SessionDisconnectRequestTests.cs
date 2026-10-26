using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// Spec 043 (T005, contracts/ipc.md §1): <see cref="SessionDisconnectRequest.Release"/> is appended after the session id, so an
/// older browser's request (one key) reads as an ordinary disconnect, and an engine from before the key ignores it.
/// </summary>
public class SessionDisconnectRequestTests
{
    private static T Legacy<T>(params object?[] values) =>
        MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(values));

    private static T RoundTrip<T>(T value) =>
        MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(value));

    [Fact]
    public void A_request_from_before_the_key_is_a_plain_disconnect()
    {
        var legacy = Legacy<SessionDisconnectRequest>("tab-session-1");

        Assert.Equal("tab-session-1", legacy.SessionId);
        Assert.False(legacy.Release);
    }

    [Fact]
    public void Release_round_trips_at_key_1()
    {
        var back = RoundTrip(new SessionDisconnectRequest { SessionId = "s", Release = true });
        Assert.True(back.Release);

        // Positional: the key is the second array element, after the session id.
        var raw = MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(new SessionDisconnectRequest { SessionId = "s", Release = true }));
        Assert.Equal(2, raw.Length);
        Assert.Equal(true, raw[1]);
    }
}
