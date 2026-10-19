namespace LibreKO.Features.Entities;

internal sealed class ClanBattleWarFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly ClanBattleWarSession clanBattleWar;
    private readonly ZoneAbilitySession zoneAbility;
    private readonly IClanBattleWarRequests requests;
    private readonly ZoneAbilityFeature zoneAbilityWindow;

    public ClanBattleWarFeature(IWorldContext context, ClanSession clan, ClanBattleWarSession clanBattleWar, ZoneAbilitySession zoneAbility, IClanBattleWarRequests requests, ZoneAbilityFeature zoneAbilityWindow)
    {
        this.context = context;
        this.clan = clan;
        this.clanBattleWar = clanBattleWar;
        this.zoneAbility = zoneAbility;
        this.requests = requests;
        this.zoneAbilityWindow = zoneAbilityWindow;
    }

    private const int BattleAskText = 6900;
    private const int BattleRefusalFirstText = 6901;
    private const int BattleNotAtWarText = 6909;
    private const int BattleTheySurrenderedText = 6910;
    private const int BattleYouSurrenderedText = 6911;
    private const int BattleBegunText = 6912;

    public void Build()
    {
        clanBattleWar.ClanBattleResultEvent += OnBattleResult;
        clanBattleWar.ClanBattleBegunEvent += OnBattleBegun;
        clanBattleWar.ClanBattleEndEvent += OnBattleEnd;
    }

    public void Dispose()
    {
        clanBattleWar.ClanBattleResultEvent -= OnBattleResult;
        clanBattleWar.ClanBattleBegunEvent -= OnBattleBegun;
        clanBattleWar.ClanBattleEndEvent -= OnBattleEnd;
    }

    private void OnBattleResult(ClanBattleResult result, int challengerId, string challenger)
    {
        if (result == ClanBattleResult.Challenged)
        {
            string ask = TextTemplate.Fill(ItemData.Text(BattleAskText,
                "Clan Leader %s has challenged your clan for a clan battle. Will you accept?"), challenger);
            Notice.Confirm(context.Root, ask, "Accept", "Decline",
                () => requests.ClanBattleAnswer(true, challengerId),
                () => requests.ClanBattleAnswer(false, challengerId));
            return;
        }
        int text = result switch
        {
            >= ClanBattleResult.Offline and <= ClanBattleResult.AlreadyAtWar => BattleRefusalFirstText + (byte)result - 2,
            ClanBattleResult.NotAtWar => BattleNotAtWarText,
            _ => 0,
        };
        if (text > 0) Notice.Show(context.Root, ItemData.Text(text, ""));
    }

    private void OnBattleBegun(short enemyClan)
    {
        zoneAbilityWindow.RefreshPlayerHostility(zoneAbility.CurrentZoneAbility);
        Notice.Show(context.Root, ItemData.Text(BattleBegunText, "The clan battle has begun."));
    }

    private void OnBattleEnd(ClanBattleEnd kind, string clanName)
    {
        if (kind == ClanBattleEnd.Cleared)
        {
            zoneAbilityWindow.RefreshPlayerHostility(zoneAbility.CurrentZoneAbility);
            return;
        }
        int text = kind == ClanBattleEnd.TheySurrendered ? BattleTheySurrenderedText : BattleYouSurrenderedText;
        Notice.Show(context.Root, TextTemplate.Fill(ItemData.Text(text, ""), clanName));
    }

    internal bool CanChallenge(EntityView target) =>
        clan.MyClan.InClan && clan.MyClan.IsChief && clanBattleWar.EnemyClanId == 0
        && target.KnightsId > 0 && target.KnightsId != clan.MyClan.ClanId;
}
