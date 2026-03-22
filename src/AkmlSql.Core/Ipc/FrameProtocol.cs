using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;

namespace AkmlSql.Core.Ipc
{
    public static class FrameProtocol
    {
        private const int MaxMessageSize = 16 * 1024 * 1024; // 16 MB

        public static async Task WriteFramedAsync(Stream stream, RpcMessage? message, CancellationToken ct = default)
        {
            var payload = MessagePackSerializer.Serialize(message, cancellationToken: ct);

            // Frame header: 4 bytes length + 4 bytes XOR checksum
            var header = new byte[8];
            header[0] = (byte)(payload.Length >> 24);
            header[1] = (byte)(payload.Length >> 16);
            header[2] = (byte)(payload.Length >> 8);
            header[3] = (byte)(payload.Length);

            var checksum = ComputeChecksum(payload);
            header[4] = (byte)(checksum >> 24);
            header[5] = (byte)(checksum >> 16);
            header[6] = (byte)(checksum >> 8);
            header[7] = (byte)(checksum);

            await stream.WriteAsync(header, 0, 8, ct).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        public static async Task<RpcMessage?> ReadFramedAsync(Stream stream, CancellationToken ct = default)
        {
            var header = new byte[8];
            int read = await ReadExactAsync(stream, header, 0, 8, ct).ConfigureAwait(false);
            if (read < 8)
            {
                return null; // stream closed
            }

            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length is <= 0 or > MaxMessageSize)
            {
                throw new InvalidDataException($"Invalid frame length: {length}");
            }

            uint expectedChecksum = (uint)((header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7]);

            var buffer = new byte[length];
            read = await ReadExactAsync(stream, buffer, 0, length, ct).ConfigureAwait(false);
            if (read < length)
            {
                return null; // stream closed
            }

            var actualChecksum = ComputeChecksum(buffer);
            if (actualChecksum != expectedChecksum)
            {
                throw new InvalidDataException($"Frame checksum mismatch: expected {expectedChecksum:X8}, got {actualChecksum:X8}");
            }

            return MessagePackSerializer.Deserialize<RpcMessage>(buffer, cancellationToken: ct);
        }

        /// <summary>
        /// Simple XOR-rotate checksum for frame integrity verification.
        /// </summary>
        private static uint ComputeChecksum(byte[] data)
        {
            uint hash = 0x5A5A5A5A;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= (uint)data[i] << ((i & 3) * 8);
                hash = (hash << 1) | (hash >> 31); // rotate left
            }
            return hash;
        }

        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    return totalRead;
                }

                totalRead += read;
            }
            return totalRead;
        }
    }
}
