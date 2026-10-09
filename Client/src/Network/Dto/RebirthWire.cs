namespace LibreKO.Network;

public static class RebirthWire
{
    public const short Accepted = 1;
    public const short RefusedFirst = -1, RefusedLast = -10;
    public const short Busy = -7;
    public const int AcceptedText = 33109;
    public const int UnavailableText = 33105;
    private const int ResultBytes = 2;

    public static bool TryRead(Packet p, out short result)
    {
        result = 0;
        if (p.RemainingBytes != ResultBytes) return false;
        short code = p.ReadShort();
        if (code != Accepted && code is > RefusedFirst or < RefusedLast) return false;
        result = code;
        return true;
    }

    public static short ReadOrBusy(Packet p) => TryRead(p, out short result) ? result : Busy;

    public static int ResultText(short result) => result switch
    {
        Accepted => AcceptedText,
        -1 => 33000,
        -2 => 33100,
        -3 => 33101,
        -4 => 33102,
        -5 => 33103,
        -6 => 33104,
        _ => UnavailableText,
    };
}
