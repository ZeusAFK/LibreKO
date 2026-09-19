using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NpcHostilityTests : GameTestBase
{
    [Theory]
    [InlineData(EntityNation.ElMorad, BattleZoneManager.ZONE_KARUS, true)]
    [InlineData(EntityNation.ElMorad, BattleZoneManager.ZONE_BATTLE1, true)]
    [InlineData(EntityNation.Karus, BattleZoneManager.ZONE_KARUS, false)]
    [InlineData(EntityNation.All, BattleZoneManager.ZONE_KARUS, false)]
    [InlineData(EntityNation.None, BattleZoneManager.ZONE_KARUS, false)]
    [InlineData(EntityNation.ElMorad, BattleZoneManager.ZONE_MORADON, false)]
    [InlineData(EntityNation.ElMorad, BattleZoneManager.ZONE_BIFROST, false)]
    public void AGuardIsOnlyATargetForTheOtherNationAndOnlyWhereTheZoneAllowsIt(
        EntityNation npcNation, byte zoneId, bool expected)
    {
        using var provider = CreateProvider(_ => { });
        var karus = CreatePlayer(provider, zoneId);

        var guard = new NpcInstance
        {
            ZoneId = zoneId,
            NpcType = 11,
            Nation = npcNation,
            Hp = 100,
            MaxHp = 100,
        };

        NpcHostility.IsAttackableBy(guard, karus).Should().Be(expected);
    }

    [Fact]
    public void AMonsterIsATargetForEverybodyEverywhere()
    {
        using var provider = CreateProvider(_ => { });
        var karus = CreatePlayer(provider, BattleZoneManager.ZONE_MORADON);

        var monster = new NpcInstance
        {
            IsMonster = true,
            ZoneId = BattleZoneManager.ZONE_MORADON,
            NpcType = 0,
            Nation = EntityNation.None,
            Hp = 100,
            MaxHp = 100,
        };

        NpcHostility.IsAttackableBy(monster, karus).Should().BeTrue();
    }

    [Fact]
    public void AShopkeeperIsNobodysTarget()
    {
        using var provider = CreateProvider(_ => { });
        var karus = CreatePlayer(provider, BattleZoneManager.ZONE_MORADON);

        var merchant = new NpcInstance
        {
            ZoneId = BattleZoneManager.ZONE_MORADON,
            NpcType = 21,
            Nation = EntityNation.None,
            Hp = 100,
            MaxHp = 100,
            SpawnActType = 101,
        };

        NpcHostility.IsAttackableBy(merchant, karus).Should().BeFalse();
    }

    private static UserSession CreatePlayer(ServiceProvider provider, byte zoneId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(950);
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>()
            .CreateSession(client, characterId: 950, accountId: 1950);
        session.Name = "Scout";
        session.Nation = AccountNation.Karus;
        session.ZoneId = zoneId;
        session.Hp = 200;
        session.MaxHp = 200;
        return session;
    }
}
