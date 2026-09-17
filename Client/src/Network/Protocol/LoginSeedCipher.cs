using System.Security.Cryptography;

namespace LibreKO.Network;

public static class LoginSeedCipher
{
    public const byte SelectorByte = 0x01;

    private static readonly byte[] Iv =
    [
        0x32, 0x4E, 0xAA, 0x58, 0xBC, 0xB3, 0xAE, 0xE3,
        0x6B, 0xC7, 0x4C, 0x56, 0x36, 0x47, 0x34, 0xF2
    ];

    public static bool LooksLikeProtectedPacket(ReadOnlySpan<byte> payload)
    {
        return payload.Length >= 17
            && payload[0] == SelectorByte
            && ((payload.Length - 1) % 16) == 0;
    }

    public static byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> seedBytes)
    {
        using var aes = CreateAes(seedBytes);
        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext.ToArray(), 0, plaintext.Length);
        return [SelectorByte, .. ciphertext];
    }

    public static byte[] Unprotect(ReadOnlySpan<byte> protectedPayload, ReadOnlySpan<byte> seedBytes)
    {
        if (!LooksLikeProtectedPacket(protectedPayload))
            throw new InvalidDataException("Login seed-protected packet must start with 0x01 and contain 16-byte CBC blocks.");

        using var aes = CreateAes(seedBytes);
        using var decryptor = aes.CreateDecryptor();

        var ciphertext = protectedPayload[1..].ToArray();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    private static Aes CreateAes(ReadOnlySpan<byte> seedBytes)
    {
        var key = DeriveKey(seedBytes);
        var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = Iv;
        return aes;
    }

    private static byte[] DeriveKey(ReadOnlySpan<byte> seedBytes)
    {
        if (seedBytes.Length is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(seedBytes), "Login seed length must be between 1 and 32 bytes.");

        var keyLength = seedBytes.Length <= 16 ? 16
            : seedBytes.Length <= 24 ? 24
            : 32;

        var key = new byte[keyLength];
        seedBytes.CopyTo(key);
        return key;
    }
}
