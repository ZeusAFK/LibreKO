namespace LibreKO.Domain;

public static class CommanderOrders
{
    public const int MarkingSkill = 492065;
    public const int OrderSkill = 492066;
    public const int MarkSound = 340000;
    public const double MarkSeconds = 60.0;
    public const int NoTargetText = 30952;
    public const int AppointText = 3434;
    public const int NamedText = 3435;
    public const int YouText = 3436;
    public const int KeyHintText = 43641;
    public const float PinReach = 30f;
    public const int ForwardSound = 340001;
    public const int BackSound = 340002;
    public const int BackHomeSound = 340003;
    public const int NoGlyph = -1;
    public const int NoSlot = -1;
    private const int ForwardSlot = 1;
    private const int BackSlot = 2;
    private static readonly (int Id, string Fallback, int Slot)[] SlashWords =
    {
        (9552, "Charge", ForwardSlot), (9553, "Attack", ForwardSlot), (9554, "Go", ForwardSlot),
        (9555, "Retreat", BackSlot), (9556, "Fallback", BackSlot), (9557, "Back", BackSlot),
    };

    public readonly record struct Slot(int Skill, CommanderOrder Order);

    public static readonly Slot[] Bar =
    {
        new(MarkingSkill, CommanderOrder.None),
        new(492066, CommanderOrder.Forward),
        new(492067, CommanderOrder.Back),
        new(492068, CommanderOrder.BackHome),
        new(492073, CommanderOrder.Right),
        new(492072, CommanderOrder.Left),
    };

    public static bool RedFlash(CommanderOrder order) => order is CommanderOrder.Back or CommanderOrder.BackHome;

    public static int Sound(CommanderOrder order) => order switch
    {
        CommanderOrder.Forward or CommanderOrder.Right or CommanderOrder.Left => ForwardSound,
        CommanderOrder.Back => BackSound,
        CommanderOrder.BackHome => BackHomeSound,
        _ => 0,
    };

    public static int Glyph(CommanderOrder order) => order switch
    {
        CommanderOrder.Forward => 0,
        CommanderOrder.Back => 1,
        CommanderOrder.BackHome => 2,
        CommanderOrder.Right => 3,
        CommanderOrder.Left => 4,
        _ => NoGlyph,
    };

    public static bool RedRing(int glyph) => glyph is 1 or 2;

    public static int SheetCell(CommanderOrder order) => order switch
    {
        CommanderOrder.Forward => 0,
        CommanderOrder.Left => 1,
        CommanderOrder.Right => 2,
        CommanderOrder.BackHome => 3,
        CommanderOrder.Back => 4,
        _ => NoGlyph,
    };

    public static int SkillOf(CommanderOrder order)
    {
        foreach (var slot in Bar)
            if (slot.Order == order && order != CommanderOrder.None) return slot.Skill;
        return 0;
    }

    public static bool ClearsMark(int markedId, int targetId) => markedId >= 0 && markedId == targetId;

    public static int SlashSlot(string word, System.Func<int, string, string> text)
    {
        foreach (var (id, fallback, slot) in SlashWords)
            if (string.Equals(word, text(id, fallback), System.StringComparison.OrdinalIgnoreCase)) return slot;
        return NoSlot;
    }
}
