using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc
{
    /// <summary>
    /// Spec 033 (T027) — MessagePack round-trip + key-layout contract for the ProfileRename
    /// message pair (request 35 / result 135).
    /// </summary>
    public class ProfileRenameMessageTests
    {
        [Fact]
        public void MessageType_constants_match_contract()
        {
            Assert.Equal(35, MessageTypes.ProfileRename);
            Assert.Equal(135, MessageTypes.ProfileRenameResult);
        }

        [Fact]
        public void Request_defaults_and_round_trip()
        {
            var m = new ProfileRenameRequest();
            Assert.Equal(string.Empty, m.OldName);
            Assert.Equal(string.Empty, m.NewName);

            m.OldName = "Team Standard";
            m.NewName = "Team Standard v2";
            var back = MessagePackSerializer.Deserialize<ProfileRenameRequest>(MessagePackSerializer.Serialize(m));
            Assert.Equal("Team Standard", back.OldName);
            Assert.Equal("Team Standard v2", back.NewName);
        }

        [Fact]
        public void Response_round_trips_success_and_failure()
        {
            var ok = new ProfileRenameResponse { Success = true, NewName = "Renamed" };
            var okBack = MessagePackSerializer.Deserialize<ProfileRenameResponse>(MessagePackSerializer.Serialize(ok));
            Assert.True(okBack.Success);
            Assert.Equal("Renamed", okBack.NewName);
            Assert.Null(okBack.ErrorMessage);

            var fail = new ProfileRenameResponse { Success = false, ErrorMessage = "Cannot rename built-in profile 'Default'." };
            var failBack = MessagePackSerializer.Deserialize<ProfileRenameResponse>(MessagePackSerializer.Serialize(fail));
            Assert.False(failBack.Success);
            Assert.Contains("built-in", failBack.ErrorMessage);
            Assert.Null(failBack.NewName);
        }

        [Fact]
        public void Request_key_layout_is_positional_0_to_1()
        {
            var m = new ProfileRenameRequest { OldName = "a", NewName = "b" };
            var slots = MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(m));
            Assert.Equal(2, slots.Length);
            Assert.Equal("a", slots[0]);
            Assert.Equal("b", slots[1]);
        }
    }
}
