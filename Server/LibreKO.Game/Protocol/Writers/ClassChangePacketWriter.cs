using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum ResetResult : byte
{
    Refused = 0,
    Success = 1,
    NothingToReset = 2,
    InventoryNotEmpty = 4,
}

public enum JobChangeEligibility : byte
{
    Allowed = 1,
    LevelTooLow = 2,
    AlreadyDone = 3,
}

public enum JobChangeOutcome : byte
{
    Succeeded = 1,
    InvalidJob = 2,
    EquipmentWorn = 4,
    JobError = 5,
    NoTokenOrSameClass = 6,
}

public enum ResetKind : byte
{
    Stat = 1,
    Skill = 2,
}

public sealed class ClassChangePacketWriter
{

    public readonly record struct StatBlock(
        int Money,
        short Strength,
        short Stamina,
        short Dexterity,
        short Intelligence,
        short Magic,
        short MaxHp,
        short MaxMp,
        short TotalHit,
        int MaxWeight,
        short StatPoints);


    public static Packet JobChangeResult(JobChangeOutcome outcome)
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)ClassChangeSubOpcode.JobChangeResult);
        packet.WriteByte((byte)outcome);
        return packet;
    }

    public static Packet Promotion(short newClass, int characterId)
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)ClassChangeSubOpcode.JobChangeResult);
        packet.WriteShort(newClass);
        packet.WriteInt(characterId);
        return packet;
    }

    public static Packet OpenJobChangePanel()
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)ClassChangeSubOpcode.Eligibility);
        return packet;
    }

    public static Packet JobChangeState()
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)ClassChangeSubOpcode.JobChangeState);
        return packet;
    }

    public static Packet JobChangeEligibility(JobChangeEligibility result)
    {
        var packet = JobChangeState();
        packet.WriteByte((byte)result);
        return packet;
    }

    public static Packet StatResetSuccess(StatBlock stats)
    {
        var packet = Sub(ClassChangeSubOpcode.StatReset);
        packet.WriteByte((byte)ResetResult.Success);
        packet.WriteInt(stats.Money);
        packet.WriteShort(stats.Strength);
        packet.WriteShort(stats.Stamina);
        packet.WriteShort(stats.Dexterity);
        packet.WriteShort(stats.Intelligence);
        packet.WriteShort(stats.Magic);
        packet.WriteShort(stats.MaxHp);
        packet.WriteShort(stats.MaxMp);
        packet.WriteShort(stats.TotalHit);
        packet.WriteInt(stats.MaxWeight);
        packet.WriteShort(stats.StatPoints);
        return packet;
    }

    public static Packet SkillResetSuccess(int money, byte masteryPoints)
    {
        var packet = Sub(ClassChangeSubOpcode.SkillReset);
        packet.WriteByte((byte)ResetResult.Success);
        packet.WriteInt(money);
        packet.WriteByte(masteryPoints);
        return packet;
    }

    public static Packet Failure(ClassChangeSubOpcode sub, ResetResult reason, int cost)
    {
        var packet = Sub(sub);
        packet.WriteByte((byte)reason);
        packet.WriteInt(cost);
        return packet;
    }

    public static Packet StatResetCost(int cost)
    {
        var packet = Sub(ClassChangeSubOpcode.StatResetCost);
        packet.WriteInt(cost);
        return packet;
    }

    private static Packet Sub(ClassChangeSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
