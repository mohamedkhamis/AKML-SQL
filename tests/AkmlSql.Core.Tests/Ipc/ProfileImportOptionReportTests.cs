using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc
{
    public class ProfileImportOptionReportTests
    {
        [Fact]
        public void Response_with_option_reports_roundtrips_through_messagepack()
        {
            var response = new ProfileImportResponse
            {
                Success = true,
                MappedOptionsCount = 2,
                OptionReports =
                [
                    new ProfileImportOptionReport { Path = "casing.reservedKeywords", Value = "uppercase", Status = "mapped" },
                    new ProfileImportOptionReport { Path = "lists.commaAlignment", Value = "toList", Status = "mapped-pending-render", Reason = "Rendering ships in phase 3 (FR-021)" },
                ],
            };

            var bytes = MessagePackSerializer.Serialize(response);
            var back = MessagePackSerializer.Deserialize<ProfileImportResponse>(bytes);

            Assert.NotNull(back.OptionReports);
            Assert.Equal(2, back.OptionReports!.Length);
            Assert.Equal("lists.commaAlignment", back.OptionReports[1].Path);
            Assert.Equal("mapped-pending-render", back.OptionReports[1].Status);
            Assert.Null(back.OptionReports[0].Reason);
        }

        [Fact]
        public void Old_wire_payload_without_key5_still_deserializes()
        {
            // Simulate a pre-031 peer: serialize a response shape lacking OptionReports.
            var legacy = MessagePackSerializer.Serialize(new ProfileImportResponse { Success = true });
            var back = MessagePackSerializer.Deserialize<ProfileImportResponse>(legacy);
            Assert.True(back.Success);
        }
    }
}
