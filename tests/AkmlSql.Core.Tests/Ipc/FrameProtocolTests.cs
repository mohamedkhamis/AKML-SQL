using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AkmlSql.Core.Ipc;

namespace AkmlSql.Core.Tests.Ipc
{
    [SuppressMessage("ReSharper", "RedundantArgumentDefaultValue")]
    public class FrameProtocolTests
    {
        private static RpcMessage Make(int type = MessageTypes.Ping, int id = 1, byte[]? payload = null)
        {
            return new RpcMessage { MessageType = type, RequestId = id, Payload = payload };
        }

        // ── Write + Read round-trips ──────────────────────────────────────────

        [Fact]
        public async Task RoundTrip_SimpleMessage()
        {
            var msg = Make(MessageTypes.Ping, 1);
            using var stream = new MemoryStream();
            await FrameProtocol.WriteFramedAsync(stream, msg);
            stream.Position = 0;

            var result = await FrameProtocol.ReadFramedAsync(stream);

            Assert.NotNull(result);
            Assert.Equal(MessageTypes.Ping, result.MessageType);
            Assert.Equal(1, result.RequestId);
        }

        [Fact]
        public async Task RoundTrip_WithPayload()
        {
            var payload = new byte[] { 10, 20, 30, 40, 50 };
            var msg = Make(MessageTypes.Pong, 99, payload);
            using var stream = new MemoryStream();
            await FrameProtocol.WriteFramedAsync(stream, msg);
            stream.Position = 0;

            var result = await FrameProtocol.ReadFramedAsync(stream);

            Assert.NotNull(result);
            Assert.Equal(99, result.RequestId);
            Assert.Equal(MessageTypes.Pong, result.MessageType);
        }

        [Fact]
        public async Task RoundTrip_MultipleMessages()
        {
            using var stream = new MemoryStream();
            await FrameProtocol.WriteFramedAsync(stream, Make(MessageTypes.Ping, 1));
            await FrameProtocol.WriteFramedAsync(stream, Make(MessageTypes.Pong, 2));
            await FrameProtocol.WriteFramedAsync(stream, Make(MessageTypes.ConnectionChanged, 3, [7, 8]));
            stream.Position = 0;

            var r1 = await FrameProtocol.ReadFramedAsync(stream);
            var r2 = await FrameProtocol.ReadFramedAsync(stream);
            var r3 = await FrameProtocol.ReadFramedAsync(stream);

            Assert.NotNull(r1); Assert.Equal(MessageTypes.Ping, r1.MessageType);
            Assert.NotNull(r2); Assert.Equal(MessageTypes.Pong, r2.MessageType);
            Assert.NotNull(r3); Assert.Equal(3, r3.RequestId);
        }

        [Fact]
        public async Task WriteFramed_WithCancellationToken_Succeeds()
        {
            using var stream = new MemoryStream();
            using var cts = new CancellationTokenSource();
            await FrameProtocol.WriteFramedAsync(stream, Make(), cts.Token);
            Assert.True(stream.Length > 0);
        }

        // ── ReadFramedAsync error / edge cases ───────────────────────────────

        [Fact]
        public async Task ReadFramed_EmptyStream_ReturnsNull()
        {
            using var stream = new MemoryStream(); // 0 bytes
            var result = await FrameProtocol.ReadFramedAsync(stream);
            Assert.Null(result);
        }

        [Fact]
        public async Task ReadFramed_PartialHeader_ReturnsNull()
        {
            // Only 4 bytes — fewer than the 8-byte header
            using var stream = new MemoryStream([0, 0, 0, 5]);
            var result = await FrameProtocol.ReadFramedAsync(stream);
            Assert.Null(result);
        }

        [Fact]
        public async Task ReadFramed_LengthZero_ThrowsInvalidDataException()
        {
            // Header with length field = 0 (all zeros)
            using var stream = new MemoryStream(new byte[8]);
            await Assert.ThrowsAsync<InvalidDataException>(() => FrameProtocol.ReadFramedAsync(stream));
        }

        [Fact]
        public async Task ReadFramed_LengthNegative_ThrowsInvalidDataException()
        {
            // MSB set → negative int32: 0x80_00_00_01 = -2147483647
            var header = new byte[] { 0x80, 0x00, 0x00, 0x01, 0, 0, 0, 0 };
            using var stream = new MemoryStream(header);
            await Assert.ThrowsAsync<InvalidDataException>(() => FrameProtocol.ReadFramedAsync(stream));
        }

        [Fact]
        public async Task ReadFramed_LengthExceedsMax_ThrowsInvalidDataException()
        {
            // MaxMessageSize = 16 MB = 0x01_00_00_00; set length = 0x01_00_00_01 (one byte over)
            var header = new byte[] { 0x01, 0x00, 0x00, 0x01, 0, 0, 0, 0 };
            using var stream = new MemoryStream(header);
            await Assert.ThrowsAsync<InvalidDataException>(() => FrameProtocol.ReadFramedAsync(stream));
        }

        [Fact]
        public async Task ReadFramed_BodyMissing_ReturnsNull()
        {
            // Write a valid message, then strip everything after the 8-byte header
            using var tmp = new MemoryStream();
            await FrameProtocol.WriteFramedAsync(tmp, Make(MessageTypes.Ping, 1));
            var headerOnly = new byte[8];
            Array.Copy(tmp.ToArray(), headerOnly, 8);

            using var stream = new MemoryStream(headerOnly);
            var result = await FrameProtocol.ReadFramedAsync(stream);
            Assert.Null(result);
        }

        [Fact]
        public async Task ReadFramed_BodyTruncated_ReturnsNull()
        {
            // Write full message, then keep header + exactly 1 body byte
            using var tmp = new MemoryStream();
            await FrameProtocol.WriteFramedAsync(tmp, Make(MessageTypes.Ping, 1));
            var full = tmp.ToArray();
            // Only take 8-byte header + 1 body byte (body is definitely longer)
            var truncated = new byte[Math.Min(9, full.Length)];
            Array.Copy(full, truncated, truncated.Length);

            using var stream = new MemoryStream(truncated);
            var result = await FrameProtocol.ReadFramedAsync(stream);
            Assert.Null(result);
        }

        [Fact]
        public async Task ReadFramed_ChecksumMismatch_ThrowsInvalidDataException()
        {
            using var tmp = new MemoryStream();
            await FrameProtocol.WriteFramedAsync(tmp, Make());
            var bytes = tmp.ToArray();

            // Flip checksum bytes (positions 4–7 in the header)
            bytes[4] ^= 0xFF;
            bytes[5] ^= 0xFF;

            using var stream = new MemoryStream(bytes);
            await Assert.ThrowsAsync<InvalidDataException>(() => FrameProtocol.ReadFramedAsync(stream));
        }

        [Fact]
        public async Task ReadFramed_WithCancellationToken_Succeeds()
        {
            using var tmp = new MemoryStream();
            await FrameProtocol.WriteFramedAsync(tmp, Make());
            tmp.Position = 0;

            using var cts = new CancellationTokenSource();
            var result = await FrameProtocol.ReadFramedAsync(tmp, cts.Token);
            Assert.NotNull(result);
        }
    }
}
