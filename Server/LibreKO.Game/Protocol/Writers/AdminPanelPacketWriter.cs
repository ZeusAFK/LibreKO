using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class AdminPanelPacketWriter
{
    public const byte Denied = 0;
    public const byte Granted = 1;
    public const int SkillCategoryCount = 9;

    public readonly record struct State(
        short Class,
        byte Level,
        byte Strength,
        byte Stamina,
        byte Dexterity,
        byte Intelligence,
        byte Magic,
        short StatPoints,
        short MaxHp,
        short MaxMp,
        short TotalHit,
        short TotalAc,
        int Money,
        IReadOnlyList<byte> SkillPoints,
        IReadOnlyList<short> ClassOptions,
        byte Nation,
        byte Race,
        int Loyalty);

    public static Packet GmFx(int characterId, bool enabled)
    {
        var packet = Sub(0x13);
        packet.WriteInt(characterId);
        packet.WriteByte(enabled ? Granted : Denied);
        return packet;
    }

    public static Packet Grant(byte sub, byte grant, bool speedGranted)
    {
        var packet = Sub(sub);
        packet.WriteByte(grant);
        packet.WriteByte((byte)(speedGranted ? Granted : Denied));
        return packet;
    }

    public static Packet StateDenied(byte sub)
    {
        var packet = Sub(sub);
        packet.WriteByte(Denied);
        return packet;
    }

    public static Packet StateGranted(byte sub, State state)
    {
        var packet = Sub(sub);
        packet.WriteByte(Granted);
        packet.WriteShort(state.Class);
        packet.WriteByte(state.Level);
        packet.WriteByte(state.Strength);
        packet.WriteByte(state.Stamina);
        packet.WriteByte(state.Dexterity);
        packet.WriteByte(state.Intelligence);
        packet.WriteByte(state.Magic);
        packet.WriteShort(state.StatPoints);
        packet.WriteShort(state.MaxHp);
        packet.WriteShort(state.MaxMp);
        packet.WriteShort(state.TotalHit);
        packet.WriteShort(state.TotalAc);
        packet.WriteInt(state.Money);

        for (var i = 0; i < SkillCategoryCount; i++)
            packet.WriteByte(state.SkillPoints[i]);

        packet.WriteByte((byte)state.ClassOptions.Count);
        foreach (var option in state.ClassOptions)
            packet.WriteShort(option);

        packet.WriteByte(state.Nation);
        packet.WriteByte(state.Race);
        packet.WriteInt(state.Loyalty);

        return packet;
    }

    public readonly record struct CollectionRaceRow(
        int Id,
        string Name,
        byte ZoneId,
        byte MinLevel,
        byte MaxLevel,
        int DurationMinutes,
        bool AutoStart,
        bool Active,
        int RemainingSeconds,
        int Completions,
        int MaxWinners,
        string Schedule,
        string Objectives);

    public static Packet CollectionRaces(byte sub, IReadOnlyList<CollectionRaceRow> rows)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)rows.Count);
        foreach (var row in rows)
        {
            packet.WriteInt(row.Id);
            packet.WriteSByteString(row.Name);
            packet.WriteByte(row.ZoneId);
            packet.WriteByte(row.MinLevel);
            packet.WriteByte(row.MaxLevel);
            packet.WriteInt(row.DurationMinutes);
            packet.WriteByte(row.AutoStart ? Granted : Denied);
            packet.WriteByte(row.Active ? Granted : Denied);
            packet.WriteInt(row.RemainingSeconds);
            packet.WriteInt(row.Completions);
            packet.WriteInt(row.MaxWinners);
            packet.WriteSByteString(row.Schedule);
            packet.WriteSByteString(row.Objectives);
        }

        return packet;
    }

    public const byte FindNpcs = 0;
    public const byte FindMonsters = 1;
    public const byte FindPlayers = 2;
    public const byte FindMonsterFlag = 1;
    public const byte FindBotFlag = 2;

    public readonly record struct FindRow(int Id, int SpawnRow, string Name, short Level, byte ZoneId, ushort X, ushort Z, bool Monster, bool Bot);

    public readonly record struct SpawnRowInfo(
        bool CanPersist, int Index, int NpcId, string Name, byte ZoneId, bool Monster, int X, int Z, int YTenths,
        int Direction, byte Count, short RespawnSeconds, short SpawnRange, int Alive);

    public static Packet SpawnRow(byte sub, SpawnRowInfo row)
    {
        var packet = Sub(sub);
        packet.WriteByte((byte)(row.CanPersist ? Granted : Denied));
        packet.WriteInt(row.Index);
        packet.WriteInt(row.NpcId);
        packet.WriteSByteString(row.Name);
        packet.WriteByte(row.ZoneId);
        packet.WriteByte((byte)(row.Monster ? Granted : Denied));
        packet.WriteInt(row.X);
        packet.WriteInt(row.Z);
        packet.WriteInt(row.YTenths);
        packet.WriteInt(row.Direction);
        packet.WriteByte(row.Count);
        packet.WriteShort(row.RespawnSeconds);
        packet.WriteShort(row.SpawnRange);
        packet.WriteInt(row.Alive);
        return packet;
    }

    public static Packet FindResults(byte sub, byte kind, int total, IReadOnlyList<FindRow> rows)
    {
        var packet = Sub(sub);
        packet.WriteByte(kind);
        packet.WriteUShort((ushort)Math.Min(total, ushort.MaxValue));
        packet.WriteUShort((ushort)rows.Count);
        foreach (var row in rows)
        {
            packet.WriteInt(row.Id);
            packet.WriteInt(row.SpawnRow);
            packet.WriteSByteString(row.Name);
            packet.WriteShort(row.Level);
            packet.WriteByte(row.ZoneId);
            packet.WriteUShort(row.X);
            packet.WriteUShort(row.Z);
            packet.WriteByte((byte)((row.Monster ? FindMonsterFlag : 0) | (row.Bot ? FindBotFlag : 0)));
        }

        return packet;
    }

    public static Packet Result(byte sub, bool ok, string message)
    {
        var packet = Sub(sub);
        packet.WriteByte((byte)(ok ? Granted : Denied));
        packet.WriteString(message);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(sub);
        return packet;
    }
}
