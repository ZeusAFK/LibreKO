namespace LibreKO.Network;

public readonly record struct FlashBonus(bool Ended, int Kind, int Value)
{
    public static FlashBonus? Read(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        var action = (FlashBonusAction)p.ReadByte();
        if (action is not (FlashBonusAction.Started or FlashBonusAction.Finished)) return null;
        if (p.RemainingBytes < 2) return null;
        int kind = p.ReadByte();
        p.ReadByte();
        int value = action == FlashBonusAction.Started && p.RemainingBytes >= sizeof(int) ? p.ReadInt() : 0;
        return new FlashBonus(action == FlashBonusAction.Finished, kind, value);
    }
}
