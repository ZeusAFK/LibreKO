using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ProgressionPacketWriter
{
    public readonly record struct LevelState(
        int CharacterId,
        byte Level,
        short StatPoints,
        byte MasteryPoints,
        long MaxExperience,
        long Experience,
        short MaxHp,
        short Hp,
        short MaxMp,
        short Mp,
        int MaxWeight,
        int ItemWeight);

    public static Packet StatPointApplied(
        byte statType, short statValue, short maxHp, short maxMp, short totalHit, int maxWeight)
    {
        var packet = new Packet(GameOpcodes.GS_POINT_CHANGE);
        packet.WriteByte(statType);
        packet.WriteShort(statValue);
        packet.WriteShort(maxHp);
        packet.WriteShort(maxMp);
        packet.WriteShort(totalHit);
        packet.WriteInt(maxWeight);
        return packet;
    }

    public static Packet SkillPointApplied(byte masteryType, byte points)
    {
        var packet = new Packet(GameOpcodes.GS_SKILLPT_CHANGE);
        packet.WriteByte(masteryType);
        packet.WriteByte(points);
        return packet;
    }

    public static Packet LevelChange(LevelState state)
    {
        var packet = new Packet(GameOpcodes.GS_LEVEL_CHANGE);
        packet.WriteInt(state.CharacterId);
        packet.WriteByte(state.Level);
        packet.WriteShort(state.StatPoints);
        packet.WriteByte(state.MasteryPoints);
        packet.WriteLong(state.MaxExperience);
        packet.WriteLong(state.Experience);
        packet.WriteShort(state.MaxHp);
        packet.WriteShort(state.Hp);
        packet.WriteShort(state.MaxMp);
        packet.WriteShort(state.Mp);
        packet.WriteInt(state.MaxWeight);
        packet.WriteInt(state.ItemWeight);
        return packet;
    }

    public static Packet WeightChange(int itemWeight)
    {
        var packet = new Packet(GameOpcodes.GS_WEIGHT_CHANGE);
        packet.WriteInt(itemWeight);
        return packet;
    }
}
