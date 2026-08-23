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

        /// <summary>Mirror of ProfileImportResponse's pre-031 5-field wire shape (Keys 0-4 only).</summary>
        [MessagePackObject]
        public class LegacyProfileImportResponseShape
        {
            [Key(0)] public bool Success { get; set; }
            [Key(1)] public int MappedOptionsCount { get; set; }
            [Key(2)] public int UnmappedOptionsCount { get; set; }
            [Key(3)] public string[]? UnmappedOptions { get; set; }
            [Key(4)] public string? ErrorMessage { get; set; }
        }

        [Fact]
        public void Old_wire_payload_without_key5_still_deserializes()
        {
            // A pre-031 peer serializes a positional array of exactly 5 elements — no index 5 at all.
            var legacy = MessagePackSerializer.Serialize(new LegacyProfileImportResponseShape
            {
                Success = true,
                MappedOptionsCount = 7,
                UnmappedOptions = new[] { "x" },
            });

            var back = MessagePackSerializer.Deserialize<ProfileImportResponse>(legacy);

            Assert.True(back.Success);
            Assert.Equal(7, back.MappedOptionsCount);
            Assert.Null(back.OptionReports);
        }
    }
}
