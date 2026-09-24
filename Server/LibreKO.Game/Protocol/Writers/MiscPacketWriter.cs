using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class MiscPacketWriter
{
    public readonly record struct ZonePopulation(ushort ZoneId, ushort Count);

    public static Packet ObjectEventResult(byte objectType, byte result)
    {
        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteByte(objectType);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet ObjectGateFlag(byte objectType, int uniqueId, bool open)
    {
        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteByte(objectType);
        packet.WriteByte(GateFlagChanged);
        packet.WriteInt(uniqueId);
        packet.WriteByte((byte)(open ? 1 : 0));
        return packet;
    }

    private const byte GateFlagChanged = 1;

    public static Packet ObjectEvent(byte objectType, byte result, int objectId)
    {
        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteByte(objectType);
        packet.WriteByte(result);
        packet.WriteInt(objectId);
        return packet;
    }

    public static Packet Premium(byte accountStatus, byte premiumType, int premiumSeconds)
    {
        var packet = new Packet(GameOpcodes.GS_PREMIUM);
        packet.WriteByte(accountStatus);
        packet.WriteByte(premiumType);
        packet.WriteInt(premiumSeconds);
        return packet;
    }

    public static Packet AuthorityChange(byte sub, int characterId, byte fame)
    {
        var packet = new Packet(GameOpcodes.GS_AUTHORITY_CHANGE);
        packet.WriteByte(sub);
        packet.WriteInt(characterId);
        packet.WriteByte(fame);
        return packet;
    }

    public static Packet CorpseLocation(short targetId, short x, short z, short y)
    {
        var packet = new Packet(GameOpcodes.GS_CORPSE);
        packet.WriteShort(targetId);
        packet.WriteShort(x);
        packet.WriteShort(z);
        packet.WriteShort(y);
        return packet;
    }

    public static Packet SantaState(byte state)
    {
        var packet = new Packet(GameOpcodes.GS_SANTA);
        packet.WriteByte(state);
        return packet;
    }

    public static Packet ConcurrentUsers(short players, short summonedFamiliars)
    {
        var packet = new Packet(GameOpcodes.GS_CONCURRENTUSER);
        packet.WriteShort(players);
        packet.WriteShort(summonedFamiliars);
        return packet;
    }

    public static Packet ZoneConcurrentUsers(IReadOnlyList<ZonePopulation> zones)
    {
        var packet = new Packet(GameOpcodes.GS_ZONE_CONCURRENT);
        packet.WriteByte((byte)zones.Count);
        foreach (var zone in zones)
        {
            packet.WriteUShort(zone.ZoneId);
            packet.WriteUShort(zone.Count);
        }
        return packet;
    }

    public const byte ClanNameChangeMarker = 16;

    public static Packet NameChangeResult(byte resultCode)
    {
        var packet = new Packet(GameOpcodes.GS_NAME_CHANGE);
        packet.WriteByte(resultCode);
        return packet;
    }

    public static Packet ClanNameChangeResult(byte resultCode)
    {
        var packet = NameChangeResult(ClanNameChangeMarker);
        packet.WriteByte(resultCode);
        return packet;
    }

    public static Packet ClanRenamed(byte resultCode, string newName)
    {
        var packet = ClanNameChangeResult(resultCode);
        packet.WriteString(newName);
        return packet;
    }
}
