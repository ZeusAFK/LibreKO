namespace LibreKO.Network;

public readonly record struct ControlEffect(int SkillId, byte BuffType, int RemainingMs);

public readonly record struct ControlStateReply(
    int UnitId, byte Flags, byte MovePercent, short AttackPercent, IReadOnlyList<ControlEffect> Effects)
{
    private const int HeaderBytes = 4 + 1 + 1 + 2 + 1;
    private const int EffectBytes = 4 + 1 + 4;

    public int LongestMs
    {
        get
        {
            int longest = 0;
            foreach (var effect in Effects)
                if (effect.RemainingMs > longest) longest = effect.RemainingMs;
            return longest;
        }
    }

    public static ControlStateReply? Read(Packet p)
    {
        if (p.RemainingBytes < HeaderBytes) return null;
        int unit = p.ReadInt();
        byte flags = p.ReadByte();
        byte move = p.ReadByte();
        short attack = p.ReadShort();
        int count = p.ReadByte();
        var effects = new List<ControlEffect>(count);
        for (int i = 0; i < count && p.RemainingBytes >= EffectBytes; i++)
            effects.Add(new ControlEffect(p.ReadInt(), p.ReadByte(), p.ReadInt()));
        return new ControlStateReply(unit, flags, move, attack, effects);
    }
}
