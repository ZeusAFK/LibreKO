using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class MagicSecondaryBuffTests : GameTestBase
{
    private const int BlindingStrafe = 108580;
    private const int Arrow = 391010000;
    private const int BlindSeconds = 3;

    [Fact]
    public async Task ARangedSkillsBlindDoesNotSpendASecondArrow()
    {
        using var provider = Provider();
        var (caster, target) = Duel(provider, arrows: 1);

        await Blind(provider, caster, target, isPrimary: false);

        target.ActiveBuffs.Should().ContainKey(BlindingStrafe);
        target.ActiveBuffs[BlindingStrafe].BuffType.Should().Be(BuffType.Blind);
        caster.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(1,
            "the arrow was paid when it was released");
    }

    [Fact]
    public async Task ARangedSkillsBlindLandsWhenTheLastArrowIsAlreadyInFlight()
    {
        using var provider = Provider();
        var (caster, target) = Duel(provider, arrows: 0);

        await Blind(provider, caster, target, isPrimary: false);

        target.ActiveBuffs.Should().ContainKey(BlindingStrafe);
    }

    [Fact]
    public async Task ABuffCastOnItsOwnStillPaysItsItem()
    {
        using var provider = Provider();
        var (caster, target) = Duel(provider, arrows: 2);

        await Blind(provider, caster, target, isPrimary: true);

        caster.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(1);
    }

    private static Task Blind(ServiceProvider provider, UserSession caster, UserSession target, bool isPrimary) =>
        provider.GetRequiredService<IMagicStatusEffectService>().ExecuteAsync(
            caster, provider.GetRequiredService<IGameDataService>().GetMagic(BlindingStrafe)!,
            MagicSkillType.Buff, BlindingStrafe, target.CharacterId, new int[7], isPrimary);

    private ServiceProvider Provider() => CreateProvider(
        _ => { },
        gameData =>
        {
            gameData.GetMagic(BlindingStrafe).Returns(new MagicData
            {
                Id = BlindingStrafe, Type1 = 2, Type2 = 4, Moral = 7, UseItem = Arrow, SuccessRate = 100,
            });
            gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
            {
                [BlindingStrafe] = new() { Id = BlindingStrafe, BuffType = (byte)BuffType.Blind, Duration = BlindSeconds },
            });
            gameData.GetItem(Arrow).Returns(new ItemData
            {
                Num = Arrow, Kind = 120, Countable = 1, Duration = 1, ReqLevelMax = 83,
            });
        });

    private static (UserSession Caster, UserSession Target) Duel(ServiceProvider provider, ushort arrows)
    {
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = Player(sessions, 720, 820, AccountNation.Karus);
        caster.Class = 108;
        if (arrows > 0)
        {
            caster.Inventory[InventoryConstants.InventoryStart].ItemId = Arrow;
            caster.Inventory[InventoryConstants.InventoryStart].Count = arrows;
            caster.Inventory[InventoryConstants.InventoryStart].Durability = 1;
        }
        var target = Player(sessions, 721, 821, AccountNation.ElMorad);
        return (caster, target);
    }

    private static UserSession Player(SessionManager sessions, int characterId, int accountId, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, characterId, accountId);
        session.Name = $"P{characterId}";
        session.Level = 75;
        session.Nation = nation;
        session.ZoneId = 21;
        session.X = 100;
        session.Z = 100;
        session.Hp = 500;
        session.MaxHp = 500;
        sessions.Regions.AddToRegion(session);
        return session;
    }
}
