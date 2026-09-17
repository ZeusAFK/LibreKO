using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum MagicProcessOpcode : byte
{
    Casting = 1,
    Flying = 2,
    Effecting = 3,
    Fail = 4,
    DurationExpired = 5,
    Cancel = 6,
    CancelTransformation = 7,
    ExtendDuration = 8,
    TransformationList = 9,
    TransformationFailed = 10,
    SkillValueList = 12,
    SkillValueUpdate = 13,
    SkillValueBatch = 14,
}

public enum TransformationFailure : byte
{
    OutsideCastleSiege = 1,
    BeforeCastleSiegeStarts = 2,
    TooFarFromBase = 3,
}

public enum DurationExpiredCode : byte
{
    Stealth = 91,
    Sight = 92,
    Summon = 93,
}

public static class MagicProcessPacketWriter
{
    private const int PayloadSlotCount = 7;

    public static Packet Create(
        MagicProcessOpcode opcode,
        int skillId,
        int casterId,
        int targetId,
        ReadOnlySpan<int> data = default)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)opcode);
        packet.WriteInt(skillId);
        packet.WriteInt(casterId);
        packet.WriteInt(targetId);

        for (var index = 0; index < PayloadSlotCount; index++)
            packet.WriteInt(index < data.Length ? data[index] : 0);

        return packet;
    }

    public static Packet CreateCasting(int skillId, int casterId, int targetId, ReadOnlySpan<int> data = default) =>
        Create(MagicProcessOpcode.Casting, skillId, casterId, targetId, data);

    public static Packet CreateFlying(int skillId, int casterId, int targetId, ReadOnlySpan<int> data = default) =>
        Create(MagicProcessOpcode.Flying, skillId, casterId, targetId, data);

    public static Packet CreateEffecting(int skillId, int casterId, int targetId, ReadOnlySpan<int> data = default) =>
        Create(MagicProcessOpcode.Effecting, skillId, casterId, targetId, data);

    public static Packet CreateCancel(int skillId, int casterId, int targetId) =>
        Create(MagicProcessOpcode.Cancel, skillId, casterId, targetId);

    public static Packet CreateFail(int skillId, int casterId) =>
        Create(MagicProcessOpcode.Fail, skillId, casterId, 0);

    public static Packet CreateDurationExpired(DurationExpiredCode code) =>
        CreateDurationExpired((byte)code);

    public static Packet CreateDurationExpired(byte expiredType)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.DurationExpired);
        packet.WriteByte(expiredType);
        return packet;
    }

    public static Packet CreateCancelTransformation() => Bare(MagicProcessOpcode.CancelTransformation);

    public static Packet CreateExtendDuration(int skillId) =>
        WithSkill(MagicProcessOpcode.ExtendDuration, skillId);

    public static Packet CreateTransformationList(int skillId) =>
        WithSkill(MagicProcessOpcode.TransformationList, skillId);

    public static Packet CreateTransformationFailed(TransformationFailure reason)
    {
        var packet = Bare(MagicProcessOpcode.TransformationFailed);
        packet.WriteByte((byte)reason);
        return packet;
    }

    private static Packet Bare(MagicProcessOpcode opcode)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)opcode);
        return packet;
    }

    private static Packet WithSkill(MagicProcessOpcode opcode, int skillId)
    {
        var packet = Bare(opcode);
        packet.WriteInt(skillId);
        return packet;
    }
}
