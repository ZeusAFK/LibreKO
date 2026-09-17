namespace LibreKO.Network;

public readonly struct ChatLine
{
    public readonly byte Type;
    public readonly int Nation;
    public readonly int CharId;
    public readonly string Name;
    public readonly string Message;
    public readonly bool IsGm;

    public ChatLine(byte type, int nation, int charId, string name, string message, bool isGm)
    {
        Type = type; Nation = nation; CharId = charId; Name = name; Message = message; IsGm = isGm;
    }
}
