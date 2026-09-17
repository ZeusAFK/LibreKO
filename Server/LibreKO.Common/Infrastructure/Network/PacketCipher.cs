using System.Numerics;
using System.Security.Cryptography;

namespace LibreKO.Common.Infrastructure.Network;

public sealed class PacketCipher
{
    private static ulong PrivateKey = 0x1207500120128966;

    private readonly byte[] _keyBytes;

    public static void SetPrivateKey(ulong key) => PrivateKey = key;

    public PacketCipher(BigInteger publicKey)
    {
        // Derive transport key: tkey = public_key ^ private_key
        // then use the raw little-endian bytes of the uint64 as the XOR key
        var publicKeyUlong = (ulong)(publicKey & ulong.MaxValue);
        var tKey = publicKeyUlong ^ PrivateKey;
        _keyBytes = BitConverter.GetBytes(tKey); // little-endian, 8 bytes
    }

    public static (byte[], BigInteger) GeneratePublicKey()
    {
        byte[] keyData;
        BigInteger publicKeyBigInt;

        // Loop until key != 0 to avoid no-op encryption
        do
        {
            keyData = new byte[8];
            RandomNumberGenerator.Fill(keyData);
            publicKeyBigInt = new BigInteger(keyData, isUnsigned: true);
        } while (publicKeyBigInt == 0);

        return (keyData, publicKeyBigInt);
    }

    public byte[] Encrypt(byte[] data)
    {
        var length = data.Length;
        var result = new byte[length];
        var lKey = (byte)((length * 157) & 0xFF);
        var rKey = 2157;

        for (var i = 0; i < length; i++)
        {
            var rsk = (byte)((rKey >> 8) & 0xFF);
            result[i] = (byte)(((data[i] ^ rsk) ^ _keyBytes[i % 8]) ^ lKey);
            unchecked { rKey *= 2171; }
        }

        return result;
    }
}
