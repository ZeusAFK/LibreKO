namespace LibreKO.Common.Domain.Entities.GameData;

public static class ItemExchangeFlags
{
    public const byte GiveAll = 0;
    public const byte SmashPoolFirst = 1;
    public const byte SmashPoolLast = 3;
    public const byte GiveEveryListed = 10;
    public const byte PlayerPicksOne = 11;
    public const byte PlayerPicksOneAlt = 12;
    public const byte GiveEveryListedAlt = 13;
    public const byte PremiumExperience = 20;
    public const byte PremiumExperienceAlt = 30;
    public const byte WeightedPickOne = 101;
    public const byte Highest = WeightedPickOne;

    public const int RewardSlotCount = 5;
    public const int BaseExperienceSlot = 0;
    public const int PremiumExperienceSlot = 3;
    public const int WeightedRateTotal = 10000;
    public const int WeightedRewardCount = 1;
    public const int RollScale = 1000;
    public const int NoSelection = -1;

    public static bool QuestGivesEveryReward(byte flag)
        => flag is GiveAll or GiveEveryListed or GiveEveryListedAlt;

    public static bool ExchangeGivesEveryReward(byte flag)
        => flag is GiveAll or GiveEveryListed or PlayerPicksOne or PlayerPicksOneAlt;

    public static bool LetsPlayerPick(byte flag)
        => flag is PlayerPicksOne or PlayerPicksOneAlt;

    public static bool IsPremiumExperience(byte flag)
        => flag is PremiumExperience or PremiumExperienceAlt;

    public static bool IsSmashPool(byte flag)
        => flag is >= SmashPoolFirst and <= SmashPoolLast;
}
