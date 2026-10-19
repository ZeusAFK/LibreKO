using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class HostilityRulesTests
{
    private const int MyId = 100;
    private const int OtherId = 200;
    private const short EnemyClan = 77;

    private static GameSession InZone(ZoneAbilityType zoneType)
    {
        var session = new GameSession();
        session.Self.ApplyCharacterId(MyId);
        session.Self.ApplyNation(Nations.Karus);
        session.ZoneAbility.ApplyZoneAbility(new ZoneAbilityInfo { ZoneType = zoneType });
        return session;
    }

    private static EntitySnapshot Player(int nation, int clan = 0, int id = OtherId) => new() { Id = id, Nation = nation, KnightsId = clan };

    [Fact]
    public void OnlyTheOtherNationIsHostileInAPvpZone()
    {
        var rules = InZone(ZoneAbilityType.Pvp).Hostility;
        Assert.True(rules.IsHostilePlayer(Player(Nations.ElMorad)));
        Assert.False(rules.IsHostilePlayer(Player(Nations.Karus)));
    }

    [Fact]
    public void NobodyIsHostileInANeutralZone()
    {
        Assert.False(InZone(ZoneAbilityType.Neutral).Hostility.IsHostilePlayer(Player(Nations.ElMorad)));
    }

    [Fact]
    public void EveryoneIsHostileInAFreeForAll()
    {
        Assert.True(InZone(ZoneAbilityType.FreeForAll).Hostility.IsHostilePlayer(Player(Nations.Karus)));
    }

    [Fact]
    public void TheClanAtWarWithMineIsHostileEvenInANeutralZone()
    {
        var session = InZone(ZoneAbilityType.Neutral);
        session.ClanBattleWar.ApplyEnemyClan(EnemyClan);
        Assert.True(session.Hostility.IsHostilePlayer(Player(Nations.Karus, EnemyClan)));
        Assert.False(session.Hostility.IsHostilePlayer(Player(Nations.Karus)));
    }

    [Fact]
    public void IAmNeverHostileToMyself()
    {
        Assert.False(InZone(ZoneAbilityType.FreeForAll).Hostility.IsHostilePlayer(Player(Nations.ElMorad, id: MyId)));
    }

    [Fact]
    public void RefreshMarksEveryKnownPlayerByTheCurrentZone()
    {
        var session = InZone(ZoneAbilityType.Pvp);
        var enemy = Player(Nations.ElMorad);
        session.Entities.Add(enemy);
        session.Hostility.RefreshKnownHostility();
        Assert.True(enemy.Attackable);

        session.ZoneAbility.ApplyZoneAbility(new ZoneAbilityInfo { ZoneType = ZoneAbilityType.Neutral });
        session.Hostility.RefreshKnownHostility();
        Assert.False(enemy.Attackable);
    }

    [Fact]
    public void NoSiegeChatMaskOutsideTheSiegeWar()
    {
        Assert.Equal(0, InZone(ZoneAbilityType.Pvp).Hostility.SiegeChatMask(OtherId, senderIsGm: false));
    }
}
