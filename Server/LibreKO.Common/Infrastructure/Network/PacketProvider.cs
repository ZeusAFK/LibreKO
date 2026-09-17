using System.Buffers.Binary;
using System.Net.Sockets;

namespace LibreKO.Common.Infrastructure.Network;

public static class PacketProvider
{
    private const short CRYPTO_OPCODE = 0x1EFC;

    public static async Task<Packet> ReadFromStream(Stream stream, CancellationToken ct)
    {
        var twoBytes = new byte[2];

        try
        {
            await stream.ReadExactlyAsync(twoBytes, ct);
            if (!twoBytes.AsSpan().SequenceEqual(Packet.Header))
                throw new InvalidDataException($"Invalid packet header: {Convert.ToHexString(twoBytes)}");

            await stream.ReadExactlyAsync(twoBytes, ct);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(twoBytes);
            if (length > short.MaxValue)
                throw new InvalidDataException($"Packet maximum size exceeded: {length}");

            var data = new byte[length];
            await stream.ReadExactlyAsync(data, ct);

            await stream.ReadExactlyAsync(twoBytes, ct);
            if (!twoBytes.AsSpan().SequenceEqual(Packet.Tail))
                throw new InvalidDataException($"Invalid packet tail: {Convert.ToHexString(twoBytes)}");

            return BuildPacket(data);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException)
        {
            throw new ClientDisconnectedException(ex);
        }
    }

    public static async Task WriteToStream(Packet packet, NetworkStream stream, PacketCipher? packetCipher = null, uint clientPacketSequenceId = 0, CancellationToken ct = default)
    {
        var finalPacketBytes = WrapPacket(packet, packetCipher, clientPacketSequenceId);
        await stream.WriteAsync(finalPacketBytes, ct);
        await stream.FlushAsync(ct);
    }

    public static byte[] WrapPacket(Packet packet, PacketCipher? packetCipher = null, uint sequenceId = 0, bool asClient = false)
    {
        var data = packet.GetBytes();
        if (packetCipher != null)
        {
            data = asClient ? BuildClientPacket(data, sequenceId) : BuildServerPacket(data, sequenceId);
            data = packetCipher.Encrypt(data);
        }

        // Header(2) + Length(2) + Data(n) + Tail(2)
        var frame = new byte[2 + 2 + data.Length + 2];
        frame[0] = Packet.Header[0];
        frame[1] = Packet.Header[1];
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(2), (short)data.Length);
        data.CopyTo(frame.AsSpan(4));
        frame[^2] = Packet.Tail[0];
        frame[^1] = Packet.Tail[1];
        return frame;
    }

    public static (Packet, uint sequenceId) UnwrapPacket(Packet packet, bool encrypted, PacketCipher? packetCipher, bool asClient = false)
    {
        if (encrypted && packetCipher != null)
        {
            var decrypted = packetCipher.Encrypt(packet.GetBytes());
            if (decrypted.Length < (asClient ? 6 : 9))
                throw new InvalidDataException("Encrypted packet payload is too short.");

            uint sequenceId;
            int dataStart;
            int dataEnd;

            if (asClient)
            {
                var cryptoOpcode = BinaryPrimitives.ReadInt16LittleEndian(decrypted.AsSpan(0, 2));
                if (cryptoOpcode != CRYPTO_OPCODE)
                    throw new InvalidDataException($"Invalid crypto opcode: 0x{cryptoOpcode:X4}");

                sequenceId = BinaryPrimitives.ReadUInt16LittleEndian(decrypted.AsSpan(2, 2));
                dataStart = 5;
                dataEnd = decrypted.Length;
            }
            else
            {
                sequenceId = BinaryPrimitives.ReadUInt32LittleEndian(decrypted.AsSpan(0, 4));
                var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(decrypted.AsSpan(decrypted.Length - 4, 4));
                var actualCrc = Crc32.Compute(decrypted.AsSpan(0, decrypted.Length - 4), uint.MaxValue);
                if (actualCrc != expectedCrc)
                    throw new InvalidDataException(
                        $"Encrypted client packet CRC check failed. " +
                        $"Len={decrypted.Length} Seq={sequenceId} Expected=0x{expectedCrc:X8} Actual=0x{actualCrc:X8} " +
                        $"Raw={Convert.ToHexString(packet.GetBytes())} Decrypted={Convert.ToHexString(decrypted)}");

                dataStart = 4;
                dataEnd = decrypted.Length - 4;
            }

            return (BuildPacket(decrypted.AsSpan(dataStart, dataEnd - dataStart)), sequenceId);
        }

        return (packet, 0);
    }

    public static Packet UnwrapLoginSeedPacket(Packet packet, byte[] seedBytes)
    {
        var decrypted = LoginSeedCipher.Unprotect(packet.GetBytes(), seedBytes);
        return BuildPacket(decrypted);
    }

    private static byte[] BuildServerPacket(byte[] data, uint sequenceId)
    {
        var result = new byte[5 + data.Length];
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(0, 2), CRYPTO_OPCODE);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2, 2), (ushort)sequenceId);
        result[4] = 0;
        data.CopyTo(result.AsSpan(5));
        return result;
    }

    private static byte[] BuildClientPacket(byte[] data, uint sequenceId)
    {
        var result = new byte[4 + data.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), sequenceId);
        data.CopyTo(result.AsSpan(4));
        var checksum = Crc32.Compute(result.AsSpan(0, 4 + data.Length), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + data.Length, 4), checksum);
        return result;
    }

    private static Packet BuildPacket(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new InvalidDataException("Packet payload is empty.");

        var packet = new Packet(data[0]);
        if (data.Length > 1)
            packet.WriteBytes(data[1..].ToArray());
        packet.ResetOffset();
        return packet;
    }
}
