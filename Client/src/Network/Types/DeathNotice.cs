namespace LibreKO.Network;

public readonly record struct DeathNotice(
        int VictimNation, int KillerNation, int Kind, int KillerId, string KillerName,
        int VictimId, string VictimName, int X, int Z, bool VictimIsKing = false);
