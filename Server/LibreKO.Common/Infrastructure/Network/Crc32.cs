namespace LibreKO.Common.Infrastructure.Network;

public static class Crc32
{
    private static readonly uint[] Table = GenerateTable();

    private static uint[] GenerateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    public static uint Compute(byte[] data) => Compute(data.AsSpan());

    public static uint Compute(ReadOnlySpan<byte> data, uint seed = 0)
    {
        uint crc = seed;
        for (int i = 0; i < data.Length; i++)
            crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
