namespace LibreKO.Domain;

public sealed class AttackChain
{
    public const int BasicAttack = 1;
    public const int InstantSkill = 6;
    public const double Window = 0.7;

    private const int FirstEffect = 70128;
    private const int EffectCount = 3;

    private const int Slots = 6;

    private readonly int[] kinds = new int[Slots];
    private readonly double[] times = new double[Slots];
    private int next;

    public bool Chained { get; private set; }

    public bool Record(int kind, double now)
    {
        if (next >= Slots) next = 0;
        int slot = next;
        int previous = (slot + Slots - 1) % Slots;
        bool hasPrevious = times[previous] > 0;
        bool sameKind = slot == 0 ? kinds[previous] == kind : kind == BasicAttack && kinds[previous] == BasicAttack;
        kinds[slot] = kind;
        times[slot] = now;
        next = slot + 1;
        Chained = hasPrevious && !sameKind && now - times[previous] < Window;
        return Chained;
    }

    public static int Effect(uint roll) => FirstEffect + (int)(roll % EffectCount);

    public bool Consume()
    {
        bool chained = Chained;
        Chained = false;
        return chained;
    }
}
