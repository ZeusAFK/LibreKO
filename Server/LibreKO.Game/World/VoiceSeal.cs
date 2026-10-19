using System.Buffers.Binary;
using System.Security.Cryptography;

namespace LibreKO.Game.World;

public sealed class VoiceSeal : IDisposable
{
    public const int KeyLength = 16;
    public const int HeaderLength = 8;
    public const int TagLength = 16;
    public const int Overhead = HeaderLength + TagLength;
    public const byte ClientToServer = 0;
    public const byte ServerToClient = 1;
    private const int NonceLength = 12;
    private const int NonceSessionOffset = 4;
    private const int NonceCounterOffset = 8;

    private readonly AesGcm aes;
    private readonly ReplayWindow window = new();
    private readonly object gate = new();
    private uint sendCounter;

    public VoiceSeal(uint sessionId, byte[] key)
    {
        SessionId = sessionId;
        aes = new AesGcm(key, TagLength);
    }

    public uint SessionId { get; }

    public static uint SessionOf(ReadOnlySpan<byte> datagram) =>
        datagram.Length < HeaderLength ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(datagram);

    public int Seal(byte direction, ReadOnlySpan<byte> plain, Span<byte> datagram)
    {
        Span<byte> nonce = stackalloc byte[NonceLength];
        lock (gate)
        {
            uint counter = ++sendCounter;
            BinaryPrimitives.WriteUInt32LittleEndian(datagram, SessionId);
            BinaryPrimitives.WriteUInt32LittleEndian(datagram[4..], counter);
            Nonce(direction, SessionId, counter, nonce);
            aes.Encrypt(nonce, plain, datagram.Slice(HeaderLength, plain.Length),
                datagram.Slice(HeaderLength + plain.Length, TagLength), datagram[..HeaderLength]);
        }
        return plain.Length + Overhead;
    }

    public bool TryOpen(byte direction, ReadOnlySpan<byte> datagram, Span<byte> plain, out int length)
    {
        length = 0;
        if (datagram.Length < Overhead || SessionOf(datagram) != SessionId)
            return false;
        uint counter = BinaryPrimitives.ReadUInt32LittleEndian(datagram[4..]);
        int size = datagram.Length - Overhead;
        if (plain.Length < size)
            return false;
        Span<byte> nonce = stackalloc byte[NonceLength];
        Nonce(direction, SessionId, counter, nonce);
        lock (gate)
        {
            if (!window.Allows(counter))
                return false;
            try
            {
                aes.Decrypt(nonce, datagram.Slice(HeaderLength, size), datagram.Slice(HeaderLength + size, TagLength),
                    plain[..size], datagram[..HeaderLength]);
            }
            catch (AuthenticationTagMismatchException)
            {
                return false;
            }
            window.Mark(counter);
        }
        length = size;
        return true;
    }

    private static void Nonce(byte direction, uint sessionId, uint counter, Span<byte> nonce)
    {
        nonce.Clear();
        nonce[0] = direction;
        BinaryPrimitives.WriteUInt32LittleEndian(nonce[NonceSessionOffset..], sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce[NonceCounterOffset..], counter);
    }

    public void Dispose() => aes.Dispose();
}

public sealed class ReplayWindow
{
    private const int Width = 64;
    private uint highest;
    private ulong seen;

    public bool Allows(uint counter)
    {
        if (counter == 0) return false;
        if (counter > highest) return true;
        uint back = highest - counter;
        return back < Width && (seen & (1UL << (int)back)) == 0;
    }

    public void Mark(uint counter)
    {
        if (counter > highest)
        {
            uint shift = counter - highest;
            seen = shift >= Width ? 0 : seen << (int)shift;
            seen |= 1;
            highest = counter;
            return;
        }
        seen |= 1UL << (int)(highest - counter);
    }
}
