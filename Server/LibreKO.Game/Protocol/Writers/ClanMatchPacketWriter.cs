using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public static class ClanMatchPacketWriter
{
    public const byte ResultSub = 1;
    public const byte RosterSub = 2;
    public const byte VitalsSub = 3;
    public const byte KnightsPairSub = 96;

    public readonly record struct Fighter(short Clan, int Id, string Name, short MaxHp, short Hp, short Class, short MaxMp, short Mp, byte Nation);

    public static Packet Pair(short zone, short clanA, short markA, string nameA, short clanB, short markB, string nameB)
    {
        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte(KnightsPairSub);
        packet.WriteShort(zone);
        WriteSide(packet, clanA, markA, nameA);
        WriteSide(packet, clanB, markB, nameB);
        return packet;
    }

    public static Packet Result(short winnerClan)
    {
        var packet = Battle(ResultSub);
        packet.WriteShort(winnerClan);
        return packet;
    }

    public static Packet Roster(IReadOnlyList<Fighter> fighters)
    {
        var packet = Battle(RosterSub);
        packet.WriteUShort((ushort)fighters.Count);
        foreach (var fighter in fighters)
        {
            packet.WriteShort(fighter.Clan);
            packet.WriteInt(fighter.Id);
            packet.WriteString(fighter.Name);
            packet.WriteShort(fighter.MaxHp);
            packet.WriteShort(fighter.Hp);
            packet.WriteByte(0);
            packet.WriteShort(fighter.Class);
            packet.WriteShort(fighter.MaxMp);
            packet.WriteShort(fighter.Mp);
            packet.WriteByte(fighter.Nation);
        }
        return packet;
    }

    public static Packet Vitals(int id, short maxHp, short hp, short maxMp, short mp)
    {
        var packet = Battle(VitalsSub);
        packet.WriteInt(id);
        packet.WriteShort(maxHp);
        packet.WriteShort(hp);
        packet.WriteShort(maxMp);
        packet.WriteShort(mp);
        return packet;
    }

    private static void WriteSide(Packet packet, short clan, short mark, string name)
    {
        packet.WriteShort(clan);
        packet.WriteShort(mark);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteSByteString(name);
        packet.WriteSByteString(string.Empty);
    }

    private static Packet Battle(byte kind)
    {
        var packet = BattleEventPacketWriter.Sub(BattleEventSubOpcode.ClanMatch);
        packet.WriteByte(kind);
        return packet;
    }
}
