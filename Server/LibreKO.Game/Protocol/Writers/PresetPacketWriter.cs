using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class PresetPacketWriter
{

    public const byte Applied = 1;

    public const byte StatNeedsRedistribution = 2;
    public const byte StatApplyFailed = 3;
    public const byte StatClassError = 7;
    public const byte StatPointsMismatch = 8;

    public const byte SkillNeedsRedistribution = 2;
    public const byte SkillApplyFailed = 5;
    public const byte SkillPointsMismatch = 8;
    public const byte SkillLevelTooLow = 9;
    public const byte SkillNeedsFirstJobChange = 10;
    public const byte SkillNeedsSecondJobChange = 11;
    public const byte SkillMasterFailed = 12;

    public readonly record struct StatState(
        byte Strength,
        byte Stamina,
        byte Dexterity,
        byte Intelligence,
        byte Magic,
        short StatPoints,
        short MaxHp,
        short MaxMp,
        short TotalHit,
        int MaxWeight);

    public readonly record struct SkillState(
        byte Mastery5,
        byte Mastery6,
        byte Mastery7,
        byte Mastery8,
        byte RemainingPoints);

    public static Packet StatApplied(StatState state)
    {
        var packet = Result(PresetSubOpcode.Stat, Applied);
        packet.WriteByte(state.Strength);
        packet.WriteByte(state.Stamina);
        packet.WriteByte(state.Dexterity);
        packet.WriteByte(state.Intelligence);
        packet.WriteByte(state.Magic);
        packet.WriteShort(state.StatPoints);
        packet.WriteShort(state.MaxHp);
        packet.WriteShort(state.MaxMp);
        packet.WriteShort(state.TotalHit);
        packet.WriteInt(state.MaxWeight);
        return packet;
    }

    public static Packet SkillApplied(SkillState state)
    {
        var packet = Result(PresetSubOpcode.Skill, Applied);
        packet.WriteByte(state.Mastery5);
        packet.WriteByte(state.Mastery6);
        packet.WriteByte(state.Mastery7);
        packet.WriteByte(state.Mastery8);
        packet.WriteByte(state.RemainingPoints);
        return packet;
    }

    public static Packet StatRejected(byte result) => Result(PresetSubOpcode.Stat, result);

    public static Packet SkillRejected(byte result) => Result(PresetSubOpcode.Skill, result);

    private static Packet Result(PresetSubOpcode sub, byte result)
    {
        var packet = new Packet(GameOpcodes.GS_PRESET);
        packet.WriteByte((byte)sub);
        packet.WriteByte(result);
        return packet;
    }
}
