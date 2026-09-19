using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class PhysicalDamageTests : GameTestBase
{
    private const int Samples = 300;
    private const ushort AttackPower = 50;
    private const byte FullAttackAmount = 100;
    private const short MoradonZone = 21;

    [Fact]
    public void AMonsterHitIsEightyFivePercentOfTheBaseDamagePlusUpToThirtyMore()
    {
        using var provider = CreateProvider(_ => { });
        var attacker = CreateAttacker(provider, MoradonZone);
        var gameData = provider.GetRequiredService<IGameDataService>();

        var monster = new NpcInstance { IsMonster = true, Hp = 100000, MaxHp = 100000, Ac = 0, EvadeRate = 1 };
        var baseDamage = AttackPower * FullAttackAmount * 2 / 240;

        var landed = Roll(() => PhysicalDamageCalculator.Calculate(
            attacker, PhysicalDefender.Of(monster), gameData));

        landed.Should().NotBeEmpty();
        landed.Should().OnlyContain(
            damage => damage >= (int)(0.85f * baseDamage)
                   && damage <= (int)(0.85f * baseDamage + 0.3f * baseDamage),
            "a normal swing lands at 0.85 of the base plus up to 0.3 more");
        landed.Should().Contain(
            damage => damage > (int)(0.85f * baseDamage),
            "the random share has to reach the target sometimes");
    }

    [Fact]
    public void APriestSwingsForTheSameAsEveryoneElse()
    {
        using var provider = CreateProvider(_ => { });
        var gameData = provider.GetRequiredService<IGameDataService>();
        var monster = new NpcInstance { IsMonster = true, Hp = 100000, MaxHp = 100000, Ac = 0, EvadeRate = 1 };

        var warrior = CreateAttacker(provider, MoradonZone, characterId: 1, classId: 106);
        var priest = CreateAttacker(provider, MoradonZone, characterId: 2, classId: 110);

        var warriorFloor = Roll(() => PhysicalDamageCalculator.Calculate(
            warrior, PhysicalDefender.Of(monster), gameData)).Min();
        var priestFloor = Roll(() => PhysicalDamageCalculator.Calculate(
            priest, PhysicalDefender.Of(monster), gameData)).Min();

        priestFloor.Should().Be(
            warriorFloor,
            "a normal swing has no class branch — only skills do");
    }

    [Fact]
    public void APlayerTargetHalvesTheDamage()
    {
        using var provider = CreateProvider(_ => { });
        var gameData = provider.GetRequiredService<IGameDataService>();

        var attacker = CreateAttacker(provider, MoradonZone, characterId: 1);
        var victim = CreateAttacker(provider, MoradonZone, characterId: 2);

        var monster = new NpcInstance { IsMonster = true, Hp = 100000, MaxHp = 100000, Ac = 0, EvadeRate = 1 };
        var monsterFloor = Roll(() => PhysicalDamageCalculator.Calculate(
            attacker, PhysicalDefender.Of(monster), gameData)).Min();
        var playerFloor = Roll(() => PhysicalDamageCalculator.Calculate(
            attacker, PhysicalDefender.Of(victim), gameData)).Min();

        playerFloor.Should().Be(monsterFloor / 2);
    }

    [Fact]
    public void BlockingPhysicalDamageStopsTheSwingOutright()
    {
        using var provider = CreateProvider(_ => { });
        var gameData = provider.GetRequiredService<IGameDataService>();

        var attacker = CreateAttacker(provider, MoradonZone, characterId: 1);
        var victim = CreateAttacker(provider, MoradonZone, characterId: 2);
        victim.BlockPhysical = true;

        PhysicalDamageCalculator.Calculate(attacker, PhysicalDefender.Of(victim), gameData)
            .Should().Be(0);
    }

    private static List<int> Roll(Func<int> calculate)
    {
        var landed = new List<int>();
        for (var i = 0; i < Samples; i++)
        {
            var damage = calculate();
            if (damage > 0)
                landed.Add(damage);
        }

        return landed;
    }

    private static UserSession CreateAttacker(
        ServiceProvider provider, short zoneId, int characterId = 900, short classId = 106)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var session = provider.GetRequiredService<SessionManager>()
            .CreateSession(client, characterId, accountId: characterId + 1000);
        session.Name = $"Attacker{characterId}";
        session.ZoneId = (byte)zoneId;
        session.Class = classId;
        session.Hp = 100;
        session.MaxHp = 100;
        session.AttackAmount = FullAttackAmount;
        session.PlayerAttackAmount = FullAttackAmount;
        session.Stats.TotalHit = AttackPower;
        session.Stats.TotalHitrate = 100;
        session.Stats.TotalEvasionrate = 1;
        session.Stats.TotalAc = 0;
        return session;
    }
}
