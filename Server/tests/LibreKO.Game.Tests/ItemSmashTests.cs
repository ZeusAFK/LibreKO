using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ItemSmashTests : GameTestBase
{
    private const int Narki = 31524;
    private const byte Moradon = 21;
    private const int UniqueSword = 120050605;
    private const int EventSword = 120050000;
    private const int ScrapReward = 810369000;
    private const int GemReward = 900823000;
    private const int UniqueGrade = 33;
    private const int UniqueBand = 3;
    private const int UniqueRolls = 3;
    private const byte UniqueType = 4;
    private const int StartingCoins = 1_000_000;

    private sealed record Player(UserSession Session, IClient Client, List<Packet> Sent, int Npc);

    private static ServiceProvider Provider()
    {
        var classes = new Dictionary<int, ItemSmashClassData>
        {
            [UniqueGrade] = new() { Id = 9, GradeClass = UniqueGrade, Band = UniqueBand, Rolls = UniqueRolls },
        };
        var rewards = new[]
        {
            new ItemSmashRewardData { Id = 3_000_000, ItemId = ScrapReward, Count = 2, Rate = 100 },
            new ItemSmashRewardData { Id = 3_000_001, ItemId = GemReward, Count = 1, Rate = 0 },
        };
        return CreateProvider(_ => { }, gameData =>
        {
            gameData.ItemSmashClassesByGrade.Returns(classes);
            gameData.ItemSmashRewardsByBand.Returns(rewards.ToLookup(r => r.Band));
            gameData.GetItem(UniqueSword).Returns(new ItemData
            {
                Num = UniqueSword, Name = "Unique Sword", Kind = 21, Slot = 1, Duration = 5000, ReqLevelMax = 100,
                ItemClass = UniqueGrade, GradeClass = UniqueGrade, Rarity = UniqueType,
            });
            gameData.GetItem(EventSword).Returns(new ItemData
            {
                Num = EventSword, Name = "Event Sword", Kind = 21, Slot = 1, Duration = 5000, ReqLevelMax = 100, ItemClass = 7, GradeClass = 7,
            });
            foreach (var id in new[] { ScrapReward, GemReward })
                gameData.GetItem(id).Returns(new ItemData { Num = id, Name = $"Reward {id}", Kind = 255, Countable = 1, Duration = 1, ReqLevelMax = 100 });
        });
    }

    private static Player Join(ServiceProvider provider, byte npcType = NpcData.TypeDisassembler, float npcX = 100)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId: 100, accountId: 1100);
        session.Name = "Smasher";
        session.Level = 70;
        session.Nation = AccountNation.Karus;
        session.ZoneId = Moradon;
        session.Hp = 100;
        session.X = 100;
        session.Z = 100;
        session.Money = StartingCoins;
        sessions.Regions.AddToRegion(session);
        var npc = sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = Narki, NpcType = npcType, Name = "Narki", Level = 50, ZoneId = Moradon, X = npcX, Z = 100, MaxHp = 100, Hp = 100,
        });
        return new Player(session, client, sent, npc.UniqueId);
    }

    private static void Hold(Player player, byte bagSlot, int itemId, ItemFlag flag = ItemFlag.Unsealed) =>
        player.Session.Inventory[InventoryConstants.SlotMax + bagSlot] =
            new ItemSlot { ItemId = itemId, Count = 1, Durability = 100, Flag = (byte)flag };

    private static async Task Smash(ServiceProvider provider, Player player, int itemId, byte bagSlot)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte((byte)ItemUpgradeSubOpcode.Smash);
        packet.WriteInt(itemId);
        packet.WriteByte(bagSlot);
        packet.WriteInt(player.Npc);
        packet.ResetOffset();
        await provider.GetRequiredService<IItemUpgradeService>().HandleUpgradeAsync(player.Client, packet);
    }

    private static Packet Reply(Player player)
    {
        var reply = player.Sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        reply.ResetOffset();
        reply.ReadByte().Should().Be(ItemSmashPacketWriter.SmashSub);
        return reply;
    }

    private static ItemSlot Bag(Player player, int bagSlot) => player.Session.Inventory[InventoryConstants.SlotMax + bagSlot];

    [Fact]
    public async Task AUniqueItemBreaksIntoItsRollsForTheHighFee()
    {
        using var provider = Provider();
        var player = Join(provider);
        Hold(player, 0, UniqueSword);

        await Smash(provider, player, UniqueSword, 0);

        var reply = Reply(player);
        reply.ReadShort().Should().Be(ItemSmashPacketWriter.Succeeded);
        reply.ReadInt().Should().Be(UniqueSword);
        reply.ReadByte().Should().Be(0);
        var count = reply.ReadShort();
        count.Should().Be(UniqueRolls);
        for (var i = 0; i < count; i++)
        {
            reply.ReadInt().Should().Be(ScrapReward);
            var slot = reply.ReadByte();
            reply.ReadUShort().Should().Be(2);
            Bag(player, slot).ItemId.Should().Be(ScrapReward);
        }
        Bag(player, 0).ItemId.Should().NotBe(UniqueSword);
        player.Session.Money.Should().Be(StartingCoins - ItemSmashService.HighFee);
    }

    [Fact]
    public async Task AnItemWithoutAClassRowCannotBeDisassembled()
    {
        using var provider = Provider();
        var player = Join(provider);
        Hold(player, 0, EventSword);

        await Smash(provider, player, EventSword, 0);

        Reply(player).ReadShort().Should().Be(ItemSmashPacketWriter.CannotDisassemble);
        Bag(player, 0).ItemId.Should().Be(EventSword);
        player.Session.Money.Should().Be(StartingCoins);
    }

    [Theory]
    [InlineData(ItemFlag.Bound)]
    [InlineData(ItemFlag.Sealed)]
    [InlineData(ItemFlag.Rented)]
    [InlineData(ItemFlag.Duplicate)]
    public async Task ProtectedItemsAreRefused(ItemFlag flag)
    {
        using var provider = Provider();
        var player = Join(provider);
        Hold(player, 0, UniqueSword, flag);

        await Smash(provider, player, UniqueSword, 0);

        Reply(player).ReadShort().Should().Be(ItemSmashPacketWriter.CannotDisassemble);
        Bag(player, 0).ItemId.Should().Be(UniqueSword);
    }

    [Fact]
    public async Task OnlyTheDisassemblerNearbyTakesItems()
    {
        using var provider = Provider();
        var player = Join(provider, npcX: 300);
        Hold(player, 0, UniqueSword);

        await Smash(provider, player, UniqueSword, 0);

        Reply(player).ReadShort().Should().Be(ItemSmashPacketWriter.WrongNpc);
        Bag(player, 0).ItemId.Should().Be(UniqueSword);
    }

    [Fact]
    public async Task ABagWithoutRoomForEveryRollIsRefused()
    {
        using var provider = Provider();
        var player = Join(provider);
        for (byte slot = 0; slot < InventoryConstants.HaveMax; slot++)
            Hold(player, slot, UniqueSword);

        await Smash(provider, player, UniqueSword, 0);

        Reply(player).ReadShort().Should().Be(ItemSmashPacketWriter.InventoryFull);
    }

    [Fact]
    public void RollsFollowTheRatesAndSkipZeroRates()
    {
        var rewards = new[]
        {
            new ItemSmashRewardData { Id = 1, ItemId = 10, Rate = 30 },
            new ItemSmashRewardData { Id = 2, ItemId = 20, Rate = 70 },
        };
        ItemSmashService.Pick(rewards, 0).ItemId.Should().Be(10);
        ItemSmashService.Pick(rewards, 29).ItemId.Should().Be(10);
        ItemSmashService.Pick(rewards, 30).ItemId.Should().Be(20);
        ItemSmashService.Pick(rewards, 99).ItemId.Should().Be(20);
    }
}
