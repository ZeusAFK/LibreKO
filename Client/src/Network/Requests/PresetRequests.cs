namespace LibreKO.Network.Requests;

public interface IPresetRequests
{
    void StatPreset(int[] stats, int pointsLeft);
    void SkillPreset(int[] trees, int poolLeft);
}

public sealed class PresetRequests : IPresetRequests
{
    private readonly IPacketSink sink;

    public PresetRequests(IPacketSink sink) => this.sink = sink;

    public void StatPreset(int[] stats, int pointsLeft)
    {
        var p = new Packet(GameOpcodes.GS_PRESET);
        p.WriteByte((byte)PresetSubOpcode.Stat);
        for (int i = 0; i < CharacterSheet.StatCount; i++)
            p.WriteByte((byte)(i < stats.Length ? Math.Clamp(stats[i], 0, byte.MaxValue) : 0));
        p.WriteUShort((ushort)Math.Clamp(pointsLeft, 0, ushort.MaxValue));
        sink.Send(p);
    }

    public void SkillPreset(int[] trees, int poolLeft)
    {
        var p = new Packet(GameOpcodes.GS_PRESET);
        p.WriteByte((byte)PresetSubOpcode.Skill);
        for (int i = 0; i < PresetPlan.TreeCount; i++)
            p.WriteByte((byte)(i < trees.Length ? Math.Clamp(trees[i], 0, byte.MaxValue) : 0));
        p.WriteByte((byte)Math.Clamp(poolLeft, 0, byte.MaxValue));
        sink.Send(p);
    }
}
