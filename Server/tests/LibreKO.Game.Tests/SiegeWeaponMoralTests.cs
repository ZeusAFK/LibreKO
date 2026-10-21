using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class SiegeWeaponMoralTests : GameTestBase
{
    private const int AttackScroll = 490137;
    private const int AttackScrollItem = 389099000;
    private const byte Delos = 30;
    private const int AreaTarget = -1;
    private const byte ScrollRadius = 30;

    [Fact]
    public async Task ASiegeScrollBuffsTheCasterAndAlliesButNotEnemies()
    {
        using var provider = CreateProvider(_ => { }, Scroll);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessionManager, 910, AccountNation.Karus, x: 100, z: 100);
        var ally = CreatePlayer(sessionManager, 911, AccountNation.Karus, x: 108, z: 104);
        var enemy = CreatePlayer(sessionManager, 912, AccountNation.ElMorad, x: 104, z: 108);
        var bag = caster.Inventory[InventoryConstants.InventoryStart];
        bag.ItemId = AttackScrollItem;
        bag.Count = 1;

        await Cast(provider, caster, AttackScroll, AreaTarget);

        caster.ActiveBuffs.Should().ContainKey(AttackScroll);
        ally.ActiveBuffs.Should().ContainKey(AttackScroll);
        enemy.ActiveBuffs.Should().NotContainKey(AttackScroll);
    }

    private static void Scroll(IGameDataService gameData)
    {
        gameData.GetMagic(AttackScroll).Returns(new MagicData
        {
            Id = AttackScroll, Type1 = 4, Moral = (byte)SkillMoral.SiegeWeapon, Range = ScrollRadius, UseItem = AttackScrollItem,
        });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
        {
            [AttackScroll] = new() { Id = AttackScroll, BuffType = (byte)BuffType.Damage, Radius = ScrollRadius, Duration = 45, Attack = 130 },
        });
        gameData.GetItem(AttackScrollItem).Returns(new ItemData { Num = AttackScrollItem, Countable = 1 });
        gameData.GetCoefficient(Arg.Any<short>()).Returns(call => CreateBasicCoefficient(call.Arg<short>()));
    }

    private static UserSession CreatePlayer(SessionManager sessionManager, int characterId, AccountNation nation, float x, float z)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var player = sessionManager.CreateSession(client, characterId, characterId + 100);
        player.Name = $"Player{characterId}";
        player.Class = 204;
        player.Level = 70;
        player.Nation = nation;
        player.ZoneId = Delos;
        player.X = x;
        player.Z = z;
        player.Hp = 500;
        player.MaxHp = 500;
        player.Mp = 500;
        player.MaxMp = 500;
        sessionManager.Regions.AddToRegion(player);
        return player;
    }

    private static async Task Cast(ServiceProvider provider, UserSession caster, int skillId, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(caster.Client, packet);
    }
}
