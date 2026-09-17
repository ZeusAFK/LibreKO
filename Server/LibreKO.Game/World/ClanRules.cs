using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class ClanRules
{
    public const short NoCape = -1;
    public const short CapeUnchosen = 0;

    public static short CapeForType(ClanType type, short current) => type switch
    {
        ClanType.Training => NoCape,
        ClanType.Promoted => CapeUnchosen,
        _ => current,
    };
}
