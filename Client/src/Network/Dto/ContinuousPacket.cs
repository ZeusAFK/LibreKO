namespace LibreKO.Network;

public static class ContinuousPacket
{
    private const int LengthBytes = 2;

    public static List<Packet> Split(Packet p)
    {
        var pieces = new List<Packet>();
        if (p.RemainingBytes < LengthBytes) return pieces;
        int total = p.ReadShort();
        int read = LengthBytes;
        while (read < total && p.RemainingBytes >= LengthBytes)
        {
            int length = p.ReadShort();
            if (length <= 0 || length >= total || p.RemainingBytes < length) break;
            byte[] bytes = p.ReadBytes(length);
            var piece = new Packet(bytes[0]);
            if (length > 1) piece.WriteBytes(bytes[1..]);
            piece.ResetOffset();
            pieces.Add(piece);
            read += length + LengthBytes;
        }
        return pieces;
    }
}
