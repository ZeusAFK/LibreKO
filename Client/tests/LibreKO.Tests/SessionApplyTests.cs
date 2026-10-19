using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class SessionApplyTests
{
    [Fact]
    public void FriendDetailsUpdateTheStoredListByName()
    {
        var friends = new FriendsSession();
        List<FriendEntry>? announced = null;
        friends.FriendListEvent += list => announced = list;

        friends.ApplyList(new[] { new FriendEntry { Name = "Rikka" }, new FriendEntry { Name = "Zeus" } });
        friends.ApplyDetails(new[] { new FriendDetails("zeus", 83, 211, 2, 21), new FriendDetails("Nobody", 1, 1, 1, 1) });

        Assert.Equal(2, friends.Friends.Count);
        Assert.Equal(83, friends.Friends[1].Level);
        Assert.Equal(21, friends.Friends[1].ZoneId);
        Assert.Equal(0, friends.Friends[0].Level);
        Assert.NotNull(announced);
        Assert.Equal(83, announced![1].Level);
    }

    [Fact]
    public void BothNationRanksFromTheEnterRecordAreKept()
    {
        var self = new SelfSession();

        self.ApplyMyInfo(new MyInfo { KnightsRank = 1, PersonalRank = NationRankAura.Unranked }, new ItemSlot[InventoryConstants.InventoryTotal]);

        Assert.Equal(1, self.KnightsRank);
        Assert.Equal(NationRankAura.Unranked, self.PersonalRank);
    }

    [Fact]
    public void ARankRefreshThatOnlyMovesTheKnightsRankIsStoredAndAnnouncedOnce()
    {
        var self = new SelfSession();
        self.ApplyMyInfo(new MyInfo { KnightsRank = NationRankAura.Unranked, PersonalRank = NationRankAura.Unranked }, new ItemSlot[InventoryConstants.InventoryTotal]);
        int announced = 0;
        self.NationRanksEvent += () => announced++;

        self.ApplyNationRanks(1, NationRankAura.Unranked);
        self.ApplyNationRanks(1, NationRankAura.Unranked);

        Assert.Equal(1, self.KnightsRank);
        Assert.Equal(1, self.LastEnter.KnightsRank);
        Assert.Equal(NationRankAura.Unranked, self.PersonalRank);
        Assert.Equal(1, announced);
    }

    [Fact]
    public void HitPointsAndManaAreStoredBeforeTheyAreAnnounced()
    {
        var self = new SelfSession();
        self.Vitals.Seed(hp: 500, maxHp: 500, mp: 200, maxMp: 200);
        SelfHpChange? hpSeen = null;
        VitalChange? mpSeen = null;
        int storedHp = 0, storedMp = 0;
        self.SelfHpEvent += change => { hpSeen = change; storedHp = self.Vitals.Hp; };
        self.SelfMpEvent += change => { mpSeen = change; storedMp = self.Vitals.Mp; };

        self.ApplyHp(420, 500, 77);
        self.ApplyMp(230, 250);

        Assert.Equal(new SelfHpChange(420, 500, 80, 0, 77), hpSeen);
        Assert.Equal(420, storedHp);
        Assert.Equal(new VitalChange(230, 250, 30), mpSeen);
        Assert.Equal(230, storedMp);
    }

    [Fact]
    public void GoldIsStoredBeforeItIsAnnouncedAndTransfersAreMarked()
    {
        var session = new GameSession();
        session.Self.Sheet.SeedWealth(1_000, 0);
        var seen = new List<(GoldChange Change, int Stored)>();
        session.Progress.GoldChangeEvent += change => seen.Add((change, session.Self.Sheet.Gold));

        session.Progress.ApplyGold(1_500);
        session.Progress.TransferGoldOut(300);
        session.Progress.TransferGoldIn(100);

        Assert.Equal(new[]
        {
            (new GoldChange(1_500, 500, false), 1_500),
            (new GoldChange(1_200, -300, true), 1_200),
            (new GoldChange(1_300, 100, true), 1_300),
        }, seen);
    }

    [Fact]
    public void ExpAndLevelAreStoredBeforeTheyAreAnnounced()
    {
        var session = new GameSession();
        session.Self.Sheet.SeedProgress(level: 10, exp: 900, maxExp: 1_000);
        ExpChange? expSeen = null;
        LevelChange? levelSeen = null;
        long storedExp = 0;
        int storedLevel = 0, storedHp = 0, storedPool = -1;
        session.Progress.ExpChangeEvent += change => { expSeen = change; storedExp = session.Self.Sheet.Exp; };
        session.Progress.LevelChangeEvent += change =>
        {
            levelSeen = change;
            storedLevel = session.Self.Sheet.Level;
            storedHp = session.Self.Vitals.Hp;
            storedPool = session.Self.Mastery.Pool;
        };

        session.Progress.ApplyExp(950);
        session.Progress.ApplyLevel(11, 3, 2, 2_000, 40, 600, 600, 300, 300);

        Assert.Equal(new ExpChange(950, 50), expSeen);
        Assert.Equal(950, storedExp);
        Assert.Equal(new LevelChange(11, 10, 40, 950, 2_000, 1_000), levelSeen);
        Assert.True(levelSeen!.Value.LeveledUp);
        Assert.Equal(11, storedLevel);
        Assert.Equal(600, storedHp);
        Assert.Equal(2, storedPool);
    }

    [Fact]
    public void LoyaltyWithoutAMonthlyTotalKeepsTheLadder()
    {
        var session = new GameSession();
        var seen = new List<LoyaltyChange>();
        session.Progress.LoyaltyChangeEvent += seen.Add;

        session.Progress.ApplyLoyalty(1_000, 400);
        session.Progress.ApplyLoyalty(900, null);
        session.Progress.ApplyLoyalty(950, 420);

        Assert.Equal(new[] { new LoyaltyChange(1_000, 1_000, 0), new LoyaltyChange(900, -100, 0), new LoyaltyChange(950, 50, 20) }, seen);
        Assert.Equal(420, session.Self.Sheet.MonthlyNp);
    }

    [Fact]
    public void MannerIsStoredBeforeItIsAnnounced()
    {
        var session = new GameSession();
        MannerChange? seen = null;
        int stored = 0;
        session.Progress.MannerChangeEvent += change => { seen = change; stored = session.Self.Sheet.Manner; };

        session.Progress.ApplyManner(36);

        Assert.Equal(new MannerChange(36, 36), seen);
        Assert.Equal(36, stored);
    }

    [Fact]
    public void AnAdminStateSetsTheSheetVitalsAndMastery()
    {
        var self = new SelfSession();
        var points = new byte[MasteryPoints.SlotCount];
        points[MasteryPoints.PoolSlot] = 6;

        self.ApplyAdminState(new AdminState
        {
            Granted = true, Level = 72, StatPoints = 12, Ap = 517,
            Str = 190, Sta = 148, Dex = 90, Intel = 60, MagicStat = 61,
            MaxHp = 3_284, MaxMp = 812, SkillPoints = points,
        });

        Assert.Equal(72, self.Sheet.Level);
        Assert.Equal(12, self.Sheet.Points);
        Assert.Equal(190, self.Sheet.Str);
        Assert.Equal(61, self.Sheet.Mag);
        Assert.Equal(3_284, self.Vitals.MaxHp);
        Assert.Equal(812, self.Vitals.MaxMp);
        Assert.Equal(6, self.Mastery.Pool);
    }

    [Fact]
    public void TheClockAndWeatherAreStoredBeforeTheyAreAnnounced()
    {
        var zone = new ZoneSession();
        (int, int)? seenTime = null, seenWeather = null;
        zone.TimeEvent += (hour, minute) => seenTime = zone.LastTime;
        zone.WeatherEvent += (type, amount) => seenWeather = zone.LastWeather;

        zone.ApplyTime(21, 30);
        zone.ApplyWeather(2, 80);

        Assert.Equal((21, 30), seenTime);
        Assert.Equal((2, 80), seenWeather);
    }

    [Fact]
    public void TheAngerGaugeIsStoredBeforeItIsAnnounced()
    {
        var pvp = new PvpSession();
        bool? fullWhenAnnounced = null;
        pvp.PvpAngerEvent += (gauge, full) => fullWhenAnnounced = pvp.AngerFull && pvp.AngerGauge == gauge;

        pvp.ApplyAnger(10, true);

        Assert.True(fullWhenAnnounced);
    }

    [Fact]
    public void TheClanBonusAndUnreadMailAnnounceWhatTheyStore()
    {
        var clan = new ClanSession();
        var mail = new MailSession();
        int bonus = 0, unread = 0;
        clan.ClanBonusEvent += count => bonus = count;
        mail.MailUnreadEvent += count => unread = count;

        clan.ApplyOnlineCount(12);
        mail.ApplyUnread(3);

        Assert.Equal((12, 12), (clan.ClanOnlineCount, bonus));
        Assert.Equal((3, 3), (mail.MailUnread, unread));
    }

    [Fact]
    public void AnAdminGrantSetsBothFlagsAndAnnouncesThePanel()
    {
        var admin = new AdminSession();
        AdminPanelGrant? announced = null;
        admin.AdminGrantEvent += grant => announced = grant;

        admin.ApplyGrant(AdminPanelGrant.GameMaster, gmSpeed: true);

        Assert.Equal(AdminPanelGrant.GameMaster, admin.PanelGrant);
        Assert.True(admin.GmSpeedGranted);
        Assert.Equal(AdminPanelGrant.GameMaster, announced);
    }

    [Fact]
    public void TheChaosFinishMarksTheBattleOverOnlyOnceItStarted()
    {
        var chaos = new ChaosSession();
        chaos.Finish(5_000, 5);
        Assert.False(chaos.ChaosOver);

        chaos.Start();
        chaos.Finish(5_000, 5);
        Assert.True(chaos.ChaosOver);
        Assert.True(chaos.Finished);
    }
}
