namespace LibreKO.Network.Readers;

internal sealed class PresetReader : IPacketReader
{
    private readonly PresetSession preset;

    private const int StatReplyBody = 15;
    private const int SkillReplyBody = 5;

    public PresetReader(PresetSession preset)
    {
        this.preset = preset;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_PRESET, HandlePreset);
    }

    private void HandlePreset(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var sub = (PresetSubOpcode)p.ReadByte();
        var result = (PresetResult)p.ReadByte();

        if (sub == PresetSubOpcode.Stat)
        {
            var state = result == PresetResult.Applied && p.RemainingBytes >= StatReplyBody
                ? ReadStatState(p)
                : default;
            preset.RaisePresetStatResult(result, state);
        }
        else if (sub == PresetSubOpcode.Skill)
        {
            var state = result == PresetResult.Applied && p.RemainingBytes >= SkillReplyBody
                ? ReadSkillState(p)
                : default;
            preset.RaisePresetSkillResult(result, state);
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
}
