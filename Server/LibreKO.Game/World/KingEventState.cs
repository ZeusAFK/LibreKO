using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public interface IKingEventState
{
    int GetExpBonus(AccountNation nation);
    int GetNoahBonus(AccountNation nation);
    void ActivateExpBonus(AccountNation nation, int bonusPercent, TimeSpan duration);
    void ActivateNoahBonus(AccountNation nation, int bonusPercent, TimeSpan duration);
}

public class KingEventState : IKingEventState
{
    private readonly Lock sync = new();
    private readonly Dictionary<AccountNation, BonusState> expBonuses = [];
    private readonly Dictionary<AccountNation, BonusState> noahBonuses = [];

    public int GetExpBonus(AccountNation nation)
    {
        lock (sync)
        {
            return GetActiveBonus(expBonuses, nation);
        }
    }

    public int GetNoahBonus(AccountNation nation)
    {
        lock (sync)
        {
            return GetActiveBonus(noahBonuses, nation);
        }
    }

    public void ActivateExpBonus(AccountNation nation, int bonusPercent, TimeSpan duration)
    {
        lock (sync)
        {
            expBonuses[nation] = new BonusState(bonusPercent, DateTime.UtcNow.Add(duration));
        }
    }

    public void ActivateNoahBonus(AccountNation nation, int bonusPercent, TimeSpan duration)
    {
        lock (sync)
        {
            noahBonuses[nation] = new BonusState(bonusPercent, DateTime.UtcNow.Add(duration));
        }
    }

    private static int GetActiveBonus(Dictionary<AccountNation, BonusState> bonuses, AccountNation nation)
    {
        if (!bonuses.TryGetValue(nation, out var state))
            return 0;

        if (DateTime.UtcNow < state.ExpiresAtUtc)
            return state.Amount;

        bonuses.Remove(nation);
        return 0;
    }

    private readonly record struct BonusState(int Amount, DateTime ExpiresAtUtc);
}
