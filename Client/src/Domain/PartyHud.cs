namespace LibreKO.Domain;

public enum PartyHpLook { Normal, Lasting, Drop, Slow, Undead }

public static class PartyHud
{
    public const int MaxRows = 8;
    public const int NoMember = -1;

    public static int Highlight(int selectedId, bool selectedInParty, int pick) =>
        selectedId >= 0 ? (selectedInParty ? selectedId : NoMember) : pick;

    public static int KeepPick(int pick, int selectedId, int selectionAtPick) =>
        pick >= 0 && selectedId != selectionAtPick ? NoMember : pick;

    private static readonly (byte Status, PartyHpLook Look)[] LookOrder =
    {
        ((byte)PartyStatusIcon.GreyHealth, PartyHpLook.Undead),
        ((byte)PartyStatusIcon.OverTimeDamage, PartyHpLook.Lasting),
        ((byte)PartyStatusIcon.Poison, PartyHpLook.Drop),
        ((byte)PartyStatusIcon.Slow, PartyHpLook.Slow),
    };

    private const int ElMoradClassBase = 200;
    private const int ElMoradIconOffset = 5;

    public static int ClassIconIndex(int classCode)
    {
        int index = CharacterClassCatalog.Family(classCode) switch
        {
            CharacterClassCatalog.FamilyWarrior => 0,
            CharacterClassCatalog.FamilyRogue => 1,
            CharacterClassCatalog.FamilyMage => 2,
            CharacterClassCatalog.FamilyKurian => 4,
            _ => 3,
        };
        return classCode >= ElMoradClassBase ? index + ElMoradIconOffset : index;
    }

    public static float Fill(int current, int max) =>
        max > 0 ? System.Math.Clamp((float)current / max, 0f, 1f) : 0f;

    public static PartyHpLook HpLook(System.Collections.Generic.IReadOnlyCollection<byte>? statuses, long second)
    {
        if (statuses == null || statuses.Count == 0) return PartyHpLook.Normal;
        int present = 0;
        foreach (var (status, _) in LookOrder)
            if (Contains(statuses, status)) present++;
        if (present == 0) return PartyHpLook.Normal;
        long pick = second % present;
        foreach (var (status, look) in LookOrder)
        {
            if (!Contains(statuses, status)) continue;
            if (pick-- == 0) return look;
        }
        return PartyHpLook.Normal;
    }

    public static string ClassIcon(int classCode) => CharacterClassCatalog.Family(classCode) switch
    {
        1 => "system/stat-str",
        2 => "system/stat-dex",
        3 => "system/stat-int",
        4 => "system/stat-hp",
        5 => "system/combat-defence",
        _ => "system/user",
    };

    private static bool Contains(System.Collections.Generic.IReadOnlyCollection<byte> statuses, byte status)
    {
        foreach (byte s in statuses)
            if (s == status) return true;
        return false;
    }

    public const int PermillePerPercent = 10;

    public static double LevelPercent(int permille) => System.Math.Clamp(permille / (double)PermillePerPercent, 0.0, 100.0);
}

public enum PartySignalLevel { Unknown, Lost, Poor, Weak, Good, Strong }

public static class PartySignal
{
    public const int Unknown = -1;
    public const int NoSignal = ushort.MaxValue;
    public const int StrongMs = 100;
    public const int GoodMs = 200;
    public const int WeakMs = 350;

    public static PartySignalLevel Level(int ms) => ms switch
    {
        < 0 => PartySignalLevel.Unknown,
        >= NoSignal => PartySignalLevel.Lost,
        <= StrongMs => PartySignalLevel.Strong,
        <= GoodMs => PartySignalLevel.Good,
        <= WeakMs => PartySignalLevel.Weak,
        _ => PartySignalLevel.Poor,
    };

    public static int Arcs(PartySignalLevel level) => level switch
    {
        PartySignalLevel.Strong => 3,
        PartySignalLevel.Good => 2,
        PartySignalLevel.Weak => 1,
        _ => 0,
    };

    public static string Tooltip(int ms) => Level(ms) switch
    {
        PartySignalLevel.Unknown => "Ping: unknown",
        PartySignalLevel.Lost => "No signal",
        _ => $"Ping: {ms} ms",
    };
}
