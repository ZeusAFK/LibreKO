using System.Buffers.Binary;
using System.Text;

namespace LibreKO.Network;

public class Packet(byte opcode)
{
    public static readonly byte[] Header = [0xAA, 0x55];
    public static readonly byte[] Tail = [0x55, 0xAA];

    private readonly MemoryStream _stream = new();
    private int _readOffset = 0;

    public Packet(LoginOpcodes opcode) : this((byte)opcode) { }
    public Packet(GameOpcodes opcode) : this((byte)opcode) { }

    // Read methods

    private void EnsureReadable(int bytes)
    {
        if (_readOffset + bytes > _stream.Length)
            throw new InvalidDataException($"Packet underflow: need {bytes} bytes at offset {_readOffset}, but only {_stream.Length - _readOffset} remain");
    }

    public byte ReadByte()
    {
        EnsureReadable(1);
        var data = _stream.GetBuffer();
        return data[_readOffset++];
    }

    public short ReadShort()
    {
        EnsureReadable(2);
        var value = BitConverter.ToInt16(_stream.GetBuffer(), _readOffset);
        _readOffset += 2;
        return value;
    }

    public ushort ReadUShort()
    {
        EnsureReadable(2);
        var value = BitConverter.ToUInt16(_stream.GetBuffer(), _readOffset);
        _readOffset += 2;
        return value;
    }

    public int ReadInt()
    {
        EnsureReadable(4);
        var value = BitConverter.ToInt32(_stream.GetBuffer(), _readOffset);
        _readOffset += 4;
        return value;
    }

    public uint ReadUInt()
    {
        EnsureReadable(4);
        var value = BitConverter.ToUInt32(_stream.GetBuffer(), _readOffset);
        _readOffset += 4;
        return value;
    }

    public long ReadLong()
    {
        EnsureReadable(8);
        var value = BitConverter.ToInt64(_stream.GetBuffer(), _readOffset);
        _readOffset += 8;
        return value;
    }

    public ulong ReadULong()
    {
        EnsureReadable(8);
        var value = BitConverter.ToUInt64(_stream.GetBuffer(), _readOffset);
        _readOffset += 8;
        return value;
    }

    public float ReadFloat()
    {
        EnsureReadable(4);
        var value = BitConverter.ToSingle(_stream.GetBuffer(), _readOffset);
        _readOffset += 4;
        return value;
    }

    public string ReadString()
    {
        var length = ReadShort();
        if (length < 0)
            throw new InvalidDataException($"Packet string has negative length: {length}");
        EnsureReadable(length);
        var str = Encoding.ASCII.GetString(_stream.GetBuffer(), _readOffset, length);
        _readOffset += length;
        return str;
    }

    public string ReadUtf8String()
    {
        var length = ReadUShort();
        EnsureReadable(length);
        var value = Encoding.UTF8.GetString(_stream.GetBuffer(), _readOffset, length);
        _readOffset += length;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative");
        EnsureReadable(count);
        var result = new byte[count];
        Buffer.BlockCopy(_stream.GetBuffer(), _readOffset, result, 0, count);
        _readOffset += count;
        return result;
    }

    // Write methods

    public void WriteByte(byte value)
    {
        _stream.WriteByte(value);
    }

    public void WriteShort(short value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteUShort(ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteInt(int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteUInt(uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteLong(long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteULong(ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteFloat(float value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length > short.MaxValue)
            throw new ArgumentException($"String too long for WriteString: {bytes.Length} bytes (max {short.MaxValue})");
        WriteShort((short)bytes.Length);
        _stream.Write(bytes);
    }

    public void WriteSByteString(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length > byte.MaxValue)
            throw new ArgumentException($"String too long for WriteSByteString: {bytes.Length} bytes (max {byte.MaxValue})");
        WriteByte((byte)bytes.Length);
        _stream.Write(bytes);
    }

    public string ReadSByteString()
    {
        var length = ReadByte();
        EnsureReadable(length);
        var str = Encoding.ASCII.GetString(_stream.GetBuffer(), _readOffset, length);
        _readOffset += length;
        return str;
    }

    public void WriteUtf8String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
            throw new ArgumentException(
                $"String too long for WriteUtf8String: {bytes.Length} bytes (max {ushort.MaxValue})");
        WriteUShort((ushort)bytes.Length);
        _stream.Write(bytes);
    }

    public void WriteBytes(byte[] data)
    {
        _stream.Write(data);
    }

    // Accessors

    public byte[] GetData() => _stream.ToArray();
    public byte GetOpcode() => opcode;
    public int GetLength() => (int)_stream.Length;
    public byte[] GetBytes()
    {
        var len = (int)_stream.Length;
        var result = new byte[1 + len];
        result[0] = opcode;
        _stream.GetBuffer().AsSpan(0, len).CopyTo(result.AsSpan(1));
        return result;
    }
    public void ResetOffset() => _readOffset = 0;
    public int RemainingBytes => (int)_stream.Length - _readOffset;

    private const int CompressionThreshold = 500;

    public Packet CompressIfNeeded()
    {
        var raw = GetBytes(); // opcode + data
        if (raw.Length < CompressionThreshold)
            return this;

        int inLength = raw.Length;
        int outBufferLength = inLength + Lzf.Margin;
        var outBuffer = new byte[outBufferLength];

        int compressedLength = Lzf.Compress(raw, inLength, outBuffer, outBufferLength);
        if (compressedLength == 0 || compressedLength >= inLength)
            return this; // Compression failed or didn't help

        uint crc = Crc32.Compute(raw);

        var result = new Packet(GameOpcodes.GS_COMPRESS_PACKET);
        result.WriteInt(compressedLength);
        result.WriteInt(inLength);
        result.WriteUInt((uint)crc);
        result.WriteBytes(outBuffer.AsSpan(0, compressedLength).ToArray());
        return result;
    }

    private const int MaxDecompressedSize = 1024 * 1024; // 1 MB cap to prevent DoS

    public static Packet? Decompress(Packet compressed)
    {
        int compressedLength = compressed.ReadInt();
        int originalLength = compressed.ReadInt();
        uint crc = compressed.ReadUInt();

        if (compressedLength <= 0 || originalLength <= 0)
            return null;

        if (originalLength > MaxDecompressedSize || compressedLength > MaxDecompressedSize)
            return null;

        var compressedData = compressed.ReadBytes(compressedLength);
        var decompressed = new byte[originalLength];

        int result = Lzf.Decompress(compressedData, compressedLength, decompressed, originalLength);
        if (result != originalLength)
            return null;

        if (Crc32.Compute(decompressed) != crc)
            return null;

        // First byte is the original opcode, rest is data
        var pkt = new Packet(decompressed[0]);
        if (originalLength > 1)
            pkt.WriteBytes(decompressed.AsSpan(1, originalLength - 1).ToArray());
        pkt.ResetOffset();
        return pkt;
    }
}
