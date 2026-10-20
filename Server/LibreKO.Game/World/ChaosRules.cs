using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class ChaosRules
{
    public const byte Zone = (byte)ZoneId.ChaosDungeon;
    public const ushort IndunEvent = 24;
    public const int PlayersPerRoom = 18;
    public const short MaxSpawnSet = 60;
    public const int BattleLockSeconds = 60;
    public const int FinishCountdownSeconds = 20;
    public const int CubeRespawnSeconds = 2;
    public const int CubeNpcId = 31527;
    public const short ChaosSkillTree = 3000;
    public const byte ChaosItemKind = 254;
    public const short MaxHp = 1000;
    public const int SkillDamage = 100;
    public const int KillingBladeSkill = 490226;
    public const int KillingBladeDamage = 1000;

    public const int KillingBladeItem = 700037000;
    public const int DrainRestoreItem = 700040000;
    public const int LightPitItem = 700041000;

    public const int VoucherOfChaos = 900106000;
    public const int BlueTreasureChest = 379156000;
    public const int GreenTreasureChest = 379155000;
    public const int RedTreasureChest = 379154000;
    public const int VoucherOfOracle = 900184000;

    private const int TopTierPercent = 20;
    private const int SecondTierPercent = 40;
    private const int ThirdTierPercent = 80;
    private const int LightPitPercent = 20;
    private const int DrainRestorePercent = 50;
    private const int PercentScale = 100;

    private const double ExpLevelFactor = 0.15;
    private const int ExpPerKill = 5;
    private const long MaxExp = 8_000_000;
    private const long MaxPremiumExp = 10_000_000;
    private const int PremiumExpFactor = 2;

    public readonly record struct Score(int CharacterId, string Name, int Kills, int Deaths);

    public static IReadOnlyList<Score> Ordered(IEnumerable<Score> scores) =>
        scores.OrderByDescending(s => s.Kills).ThenBy(s => s.Deaths).ThenBy(s => s.CharacterId).ToList();

    public static int RankPercent(IReadOnlyList<Score> ordered, int characterId)
    {
        if (ordered.Count == 0) return PercentScale;
        int position = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i == 0 || ordered[i].Kills != ordered[i - 1].Kills || ordered[i].Deaths != ordered[i - 1].Deaths)
                position = i + 1;
            if (ordered[i].CharacterId == characterId)
                return (position * PercentScale + ordered.Count - 1) / ordered.Count;
        }
        return PercentScale;
    }

    public static int CubeItem(int rankPercent) => rankPercent switch
    {
        <= LightPitPercent => LightPitItem,
        <= DrainRestorePercent => DrainRestoreItem,
        _ => KillingBladeItem,
    };

    public static IReadOnlyList<int> Rewards(int rankPercent) => rankPercent switch
    {
        <= TopTierPercent => [VoucherOfChaos, BlueTreasureChest, VoucherOfOracle],
        <= SecondTierPercent => [VoucherOfChaos, GreenTreasureChest],
        <= ThirdTierPercent => [VoucherOfChaos, RedTreasureChest],
        _ => [VoucherOfChaos],
    };

    public static long Exp(byte level, int kills, int deaths)
    {
        long weight = ExpPerKill * (long)kills - deaths;
        if (weight <= 0) return 0;
        return Math.Min(MaxExp, (long)((double)level * level * level * ExpLevelFactor * weight));
    }

    public static long PremiumExp(long exp, bool premium) =>
        premium ? Math.Min(MaxPremiumExp, exp * PremiumExpFactor) : 0;

    public const short DungeonDefenseSkillTree = 3500;
    public const short KnightRoyaleSkillTree = 3100;
    public const short SealedZoneSkillTree = 3200;
    public const short FirstTournamentSkillTree = 3610;
    public const short TournamentSkillTreeEnd = 3650;

    // Event trees work only inside their own event zone.
    public static bool SkillAllowedIn(byte zoneId, short skillTree) =>
        zoneId == Zone
            ? skillTree == ChaosSkillTree
            : zoneId == (byte)ZoneId.DungeonDefence
                ? skillTree == DungeonDefenseSkillTree
                : !IsEventSkillTree(skillTree);

    public static bool IsEventSkillTree(short skillTree) =>
        skillTree is ChaosSkillTree or KnightRoyaleSkillTree or SealedZoneSkillTree or DungeonDefenseSkillTree
            or >= FirstTournamentSkillTree and < TournamentSkillTreeEnd;

    public static int SkillDamageOf(int skillId) => skillId == KillingBladeSkill ? KillingBladeDamage : SkillDamage;
}
