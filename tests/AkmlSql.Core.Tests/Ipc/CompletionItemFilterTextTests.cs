using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc
{
    /// <summary>
    /// Spec 032 (T005, FR-026) — `CompletionItem.FilterText` at [Key(7)] is additive:
    /// new peers round-trip it, old payloads (no key 7) deserialize with null.
    /// </summary>
    public class CompletionItemFilterTextTests
    {
        [Fact]
        public void FilterText_roundtrips_through_messagepack()
        {
            var item = new CompletionItem
            {
                DisplayText = "o.OrderID",
                InsertText = "o.OrderID",
                ObjectType = (int)CompletionObjectType.Column,
                SortPriority = 30,
                FilterText = "OrderID",
            };

            var bytes = MessagePackSerializer.Serialize(item);
            var back = MessagePackSerializer.Deserialize<CompletionItem>(bytes);

            Assert.Equal("OrderID", back.FilterText);
            Assert.Equal("o.OrderID", back.DisplayText);
        }

        [Fact]
        public void Old_payload_without_key7_deserializes_with_null_filtertext()
        {
            // Simulate a pre-032 peer: a 7-element array payload (keys 0..6 only).
            var legacy = MessagePackSerializer.Serialize(new object?[]
            {
                "DisplayText", "InsertText", 2, "SecondaryText", "SourceObject", 30, false,
            });

            var back = MessagePackSerializer.Deserialize<CompletionItem>(legacy);

            Assert.Null(back.FilterText);
            Assert.Equal("DisplayText", back.DisplayText);
            Assert.False(back.IsLinkedServer);
        }

        [Fact]
        public void Response_with_filtertext_items_roundtrips()
        {
            var response = new CompletionResponse
            {
                Items =
                [
                    new CompletionItem { DisplayText = "OrderDate" },
                    new CompletionItem { DisplayText = "c.CustomerName", FilterText = "CustomerName" },
                ],
                IsIncomplete = true,
            };

            var back = MessagePackSerializer.Deserialize<CompletionResponse>(
                MessagePackSerializer.Serialize(response));

            Assert.Null(back.Items[0].FilterText);
            Assert.Equal("CustomerName", back.Items[1].FilterText);
            Assert.True(back.IsIncomplete);
        }
    }
}
