using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc
{
    /// <summary>
    /// Spec 033 (T004) — MessagePack round-trip + key-layout contract for the ProfileGet
    /// message pair (request 34 / result 134). See
    /// specs/033-format-styles-window/contracts/ipc-profile-messages.md.
    /// </summary>
    public class ProfileGetMessageTests
    {
        [Fact]
        public void MessageType_constants_match_contract()
        {
            Assert.Equal(34, MessageTypes.ProfileGet);
            Assert.Equal(134, MessageTypes.ProfileGetResult);
        }

        [Fact]
        public void Request_defaults_and_round_trip()
        {
            var m = new ProfileGetRequest();
            Assert.Equal(string.Empty, m.Name);

            m.Name = "Khamis Style";
            var bytes = MessagePackSerializer.Serialize(m);
            var back = MessagePackSerializer.Deserialize<ProfileGetRequest>(bytes);
            Assert.Equal("Khamis Style", back.Name);
        }

        [Fact]
        public void Response_defaults_and_round_trip()
        {
            var m = new ProfileGetResponse();
            Assert.False(m.Success);
            Assert.Null(m.ErrorMessage);
            Assert.Null(m.Name);
            Assert.Null(m.ProfileJson);
            Assert.False(m.IsBuiltIn);

            m.Success = true;
            m.Name = "Default";
            m.ProfileJson = "{\"metadata\":{\"name\":\"Default\"},\"unknownRootKey\":1}";
            m.IsBuiltIn = true;

            var back = MessagePackSerializer.Deserialize<ProfileGetResponse>(MessagePackSerializer.Serialize(m));
            Assert.True(back.Success);
            Assert.Null(back.ErrorMessage);
            Assert.Equal("Default", back.Name);
            Assert.Equal(m.ProfileJson, back.ProfileJson);
            Assert.True(back.IsBuiltIn);
        }

        [Fact]
        public void Response_failure_round_trip_carries_error()
        {
            var m = new ProfileGetResponse { Success = false, ErrorMessage = "Profile 'x' was not found." };
            var back = MessagePackSerializer.Deserialize<ProfileGetResponse>(MessagePackSerializer.Serialize(m));
            Assert.False(back.Success);
            Assert.Equal("Profile 'x' was not found.", back.ErrorMessage);
            Assert.Null(back.ProfileJson);
        }

        [Fact]
        public void Response_key_layout_is_positional_and_append_only()
        {
            // Guards the append-only [Key(n)] contract: serialize as array, assert slot order.
            //
            // Keys 5 and 6 were appended when built-in styles became editable — an edited built-in
            // resolves from the custom directory, so IsBuiltIn (slot 4) is false for it even though
            // it is still a shipped style. Slots 0-4 keep their meaning and position, which is the
            // actual compatibility promise: a shell built before those keys existed reads the first
            // five and ignores the rest.
            var m = new ProfileGetResponse
            {
                Success = true,
                ErrorMessage = "e",
                Name = "n",
                ProfileJson = "{}",
                IsBuiltIn = true,
                HasBuiltIn = true,
                IsCustomizedBuiltIn = true,
            };
            var dynamicModel = MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(m));
            Assert.Equal(7, dynamicModel.Length);
            Assert.Equal(true, dynamicModel[0]);
            Assert.Equal("e", dynamicModel[1]);
            Assert.Equal("n", dynamicModel[2]);
            Assert.Equal("{}", dynamicModel[3]);
            Assert.Equal(true, dynamicModel[4]);
            Assert.Equal(true, dynamicModel[5]);
            Assert.Equal(true, dynamicModel[6]);
        }
    }
}
