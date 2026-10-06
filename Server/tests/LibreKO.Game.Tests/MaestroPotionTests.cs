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

public class MaestroPotionTests : GameTestBase
{
    private const int HealthSkill = 490076;
    private const int ManaSkill = 490084;
    private const int HealthPotion = 810117000;
    private const int ManaPotion = 810118000;
    private const int HealthPrice = 4900;
    private const int ManaPrice = 10500;
    private const int HealthRestored = 720;
    private const int ManaRestored = 1920;
    private const int EnoughCoins = 1_000_000;
    private const int UnderTheFloor = 99_999;

    private static ServiceProvider Provider() => CreateProvider(
        _ => { },
        gameData =>
        {
            gameData.GetMagic(HealthSkill).Returns(new MagicData { Id = HealthSkill, Type1 = 3, Moral = 1, Range = 25, ItemGroup = 9 });
            gameData.GetMagic(ManaSkill).Returns(new MagicData { Id = ManaSkill, Type1 = 3, Moral = 1, Range = 25, ItemGroup = 9 });
            gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
            {
                [HealthSkill] = new() { Id = HealthSkill, DirectType = (byte)MagicDirectType.HealthPurchase, FirstDamage = HealthRestored, TimeDamage = 3500 },
                [ManaSkill] = new() { Id = ManaSkill, DirectType = (byte)MagicDirectType.ManaPurchase, FirstDamage = ManaRestored, TimeDamage = 5400 },
            });
            gameData.GetItem(HealthPotion).Returns(new ItemData { Num = HealthPotion, Name = "HP Maestro Potion", Kind = 255, Slot = 17, BuyPrice = HealthPrice });
            gameData.GetItem(ManaPotion).Returns(new ItemData { Num = ManaPotion, Name = "MP Maestro Potion", Kind = 255, Slot = 17, BuyPrice = ManaPrice });
        });

    private static (UserSession Player, IClient Client) Player(ServiceProvider provider, int coins, int carried)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var player = sessions.CreateSession(client, characterId: 700, accountId: 800);
        player.Name = "Drinker";
        player.Class = 105;
        player.Level = 60;
        player.Nation = AccountNation.Karus;
        player.ZoneId = 21;
        player.X = 100;
        player.Z = 100;
        player.MaxHp = 5000;
        player.Hp = 1000;
        player.MaxMp = 5000;
        player.Mp = 1000;
        player.Money = coins;
        if (carried != 0)
        {
            player.Inventory[InventoryConstants.InventoryStart].ItemId = carried;
            player.Inventory[InventoryConstants.InventoryStart].Count = 1;
            player.Inventory[InventoryConstants.InventoryStart].Durability = 1;
        }
        sessions.Regions.AddToRegion(player);
        return (player, client);
    }

    private static async Task Drink(ServiceProvider provider, IClient client, UserSession player, int skillId)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(player.CharacterId);
        packet.WriteInt(player.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);
        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(client, packet);
    }

    [Fact]
    public async Task TheHealthPotionHealsAndChargesItsPrice()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, EnoughCoins, HealthPotion);

        await Drink(provider, client, player, HealthSkill);

        player.Hp.Should().Be(1000 + HealthRestored);
        player.Money.Should().Be(EnoughCoins - HealthPrice, "each use costs the potion's price in Noah");
        player.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(HealthPotion, "the potion is never used up");
    }

    [Fact]
    public async Task TheManaPotionRestoresManaAndChargesItsPrice()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, EnoughCoins, ManaPotion);

        await Drink(provider, client, player, ManaSkill);

        player.Mp.Should().Be(1000 + ManaRestored);
        player.Money.Should().Be(EnoughCoins - ManaPrice);
    }

    [Fact]
    public async Task BelowOneHundredThousandNoahThePotionRefuses()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, UnderTheFloor, HealthPotion);

        await Drink(provider, client, player, HealthSkill);

        player.Hp.Should().Be(1000);
        player.Money.Should().Be(UnderTheFloor);
    }

    [Fact]
    public async Task WithoutThePotionTheSkillRefuses()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, EnoughCoins, 0);

        await Drink(provider, client, player, ManaSkill);

        player.Mp.Should().Be(1000);
        player.Money.Should().Be(EnoughCoins);
    }
}
