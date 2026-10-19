namespace LibreKO.Network;

public readonly record struct ExpSealReply(ExpSealSubOpcode Sub, ExpSealResult Code, long Total)
{
    public static ExpSealReply? Read(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        var sub = (ExpSealSubOpcode)p.ReadByte();
        switch (sub)
        {
            case ExpSealSubOpcode.On or ExpSealSubOpcode.Off when p.RemainingBytes >= 1:
                return new ExpSealReply(sub, (ExpSealResult)p.ReadByte(), 0);
            case ExpSealSubOpcode.Store when p.RemainingBytes >= sizeof(short):
                {
                    var code = (ExpSealResult)p.ReadShort();
                    long total = code == ExpSealResult.Succeeded && p.RemainingBytes >= sizeof(long) ? p.ReadLong() : 0;
                    return new ExpSealReply(sub, code, total);
                }
            case ExpSealSubOpcode.Total when p.RemainingBytes >= sizeof(long):
                return new ExpSealReply(sub, ExpSealResult.Succeeded, p.ReadLong());
            case ExpSealSubOpcode.UseJar when p.RemainingBytes >= sizeof(short):
                return new ExpSealReply(sub, (ExpSealResult)p.ReadShort(), 0);
            default:
                return null;
        }
    }
}

public enum ExpSealSubOpcode : byte
{
    On = 1,
    Off = 2,
    Store = 3,
    Total = 4,
    UseJar = 5,
}

public enum ExpSealState : byte
{
    Unknown = 0,
    On = 1,
    Off = 2,
}

public enum ExpSealResult : short
{
    Failed = 0,
    Succeeded = 1,
    TooLittle = 2,
    WhileOn = 3,
    Empty = 6,
    RebirthLimit = 9,
}
