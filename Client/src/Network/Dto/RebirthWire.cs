namespace LibreKO.Network;

public static class RebirthWire
{
    public const short RefusedFirst = -1, RefusedLast = -10;
    public const int AcceptedText = 33109;
    public const int UnavailableText = 33105;
    private const int ResultBytes = 2;

    public static bool TryRead(Packet p, out short result)
    {
        result = 0;
        if (p.RemainingBytes != ResultBytes) return false;
        short code = p.ReadShort();
        if ((RebirthResult)code != RebirthResult.Success && code is > RefusedFirst or < RefusedLast) return false;
        result = code;
        return true;
    }

    public static short ReadOrBusy(Packet p) => TryRead(p, out short result) ? result : (short)RebirthResult.Unavailable;

    public static int ResultText(RebirthResult result) => result switch
    {
        RebirthResult.Success => AcceptedText,
        (RebirthResult)(-1) => 33000,
        RebirthResult.NoQualification => 33100,
        RebirthResult.LevelTooLow => 33101,
        (RebirthResult)(-4) => 33102,
        RebirthResult.ExperienceNotFull => 33103,
        (RebirthResult)(-6) => 33104,
        _ => UnavailableText,
    };
}

public enum RebirthResult : short
{
    Unavailable = -7,
    ExperienceNotFull = -5,
    LevelTooLow = -3,
    NoQualification = -2,
    Success = 1,
}
