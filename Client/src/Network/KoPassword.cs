namespace LibreKO.Network;

public static class KoPassword
{
    private static readonly int[] Key =
    {
        0x1A,0x1F,0x11,0x0A,0x1E,0x10,0x18,0x02,0x1D,0x08,0x14,0x0F,
        0x1C,0x0B,0x0D,0x04,0x13,0x17,0x00,0x0C,0x0E,0x1B,0x06,0x12,
        0x15,0x03,0x09,0x07,0x16,0x01,0x19,0x05,0x12,0x1D,0x07,0x19,
        0x0F,0x1F,0x16,0x1B,0x09,0x1A,0x03,0x0D,0x13,0x0E,0x14,0x0B,
        0x05,0x02,0x17,0x10,0x0A,0x18,0x1C,0x11,0x06,0x1E,0x00,0x15,
        0x08,0x04,0x01,
    };
    private const string Hash = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Encode(string password)
    {
        var raw = System.Text.Encoding.ASCII.GetBytes(password);
        int paddedLen = raw.Length;
        if (paddedLen % 4 != 0)
            paddedLen += 4 - (paddedLen % 4);
        var buf = new byte[paddedLen];
        raw.CopyTo(buf, 0);

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < paddedLen / 4; i++)
        {
            uint block = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(i * 4, 4));
            uint tmp = block + 0x3E8u;
            ulong acc = 0;
            int counter = 0;
            while (tmp != 0)
            {
                if ((tmp & 1) != 0)
                    acc += 1ul << Key[counter];
                tmp >>= 1;
                counter++;
            }
            for (int j = 0; j < 7; j++)
            {
                sb.Append(Hash[(int)(acc % 36)]);
                acc /= 36;
            }
        }
        return sb.ToString();
    }
}
