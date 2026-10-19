namespace LibreKO.Network;

public readonly record struct TargetEffect(int SkillId, int Seconds);

public readonly record struct TargetEffectsReply(int TargetId, IReadOnlyList<TargetEffect> Effects)
{
    private const int EffectBytes = 6;

    public static TargetEffectsReply? Read(Packet p)
    {
        if (p.RemainingBytes < 5) return null;
        int targetId = p.ReadInt();
        int count = p.ReadByte();
        var effects = new List<TargetEffect>(count);
        for (int i = 0; i < count && p.RemainingBytes >= EffectBytes; i++)
            effects.Add(new TargetEffect(p.ReadInt(), p.ReadUShort()));
        return new TargetEffectsReply(targetId, effects);
    }
}
