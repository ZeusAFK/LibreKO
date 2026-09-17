using System;

namespace LibreKO.Network;

public partial class Net
{
    public const int PresetSubStat = 1;
    public const int PresetSubSkill = 2;

    public const int PresetApplied = 1;
    public const int PresetStatNeedsRedistribution = 2;
    public const int PresetStatApplyFailed = 3;
    public const int PresetStatClassError = 7;
    public const int PresetStatPointsMismatch = 8;

    public const int PresetSkillNeedsRedistribution = 2;
    public const int PresetSkillApplyFailed = 5;
    public const int PresetSkillPointsMismatch = 8;
    public const int PresetSkillLevelTooLow = 9;
    public const int PresetSkillNeedsFirstJobChange = 10;
    public const int PresetSkillNeedsSecondJobChange = 11;
    public const int PresetSkillMasterFailed = 12;

    private const int PresetStatReplyBody = 15;
    private const int PresetSkillReplyBody = 5;

    public event Action<int, PresetStatState>? PresetStatResultEvent;
    public event Action<int, PresetSkillState>? PresetSkillResultEvent;

    private void HandlePreset(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        int sub = p.ReadByte();
        int result = p.ReadByte();

        if (sub == PresetSubStat)
        {
            var state = result == PresetApplied && p.RemainingBytes >= PresetStatReplyBody
                ? ReadStatState(p)
                : default;
            PresetStatResultEvent?.Invoke(result, state);
        }
        else if (sub == PresetSubSkill)
        {
            var state = result == PresetApplied && p.RemainingBytes >= PresetSkillReplyBody
                ? ReadSkillState(p)
                : default;
            PresetSkillResultEvent?.Invoke(result, state);
        }
    }

    private static PresetStatState ReadStatState(Packet p)
    {
        var stats = new int[CharacterSheet.StatCount];
        for (int i = 0; i < stats.Length; i++) stats[i] = p.ReadByte();
        return new PresetStatState(
            stats, p.ReadUShort(), p.ReadUShort(), p.ReadUShort(), p.ReadUShort(), p.ReadInt());
    }

    private static PresetSkillState ReadSkillState(Packet p)
    {
        var trees = new int[PresetPlan.TreeCount];
        for (int i = 0; i < trees.Length; i++) trees[i] = p.ReadByte();
        return new PresetSkillState(trees, p.ReadByte());
    }

    public void SendStatPreset(int[] stats, int pointsLeft)
    {
        var p = new Packet(GameOpcodes.GS_PRESET);
        p.WriteByte(PresetSubStat);
        for (int i = 0; i < CharacterSheet.StatCount; i++)
            p.WriteByte((byte)(i < stats.Length ? Math.Clamp(stats[i], 0, byte.MaxValue) : 0));
        p.WriteUShort((ushort)Math.Clamp(pointsLeft, 0, ushort.MaxValue));
        _conn.Send(p);
    }

    public void SendSkillPreset(int[] trees, int poolLeft)
    {
        var p = new Packet(GameOpcodes.GS_PRESET);
        p.WriteByte(PresetSubSkill);
        for (int i = 0; i < PresetPlan.TreeCount; i++)
            p.WriteByte((byte)(i < trees.Length ? Math.Clamp(trees[i], 0, byte.MaxValue) : 0));
        p.WriteByte((byte)Math.Clamp(poolLeft, 0, byte.MaxValue));
        _conn.Send(p);
    }
}
