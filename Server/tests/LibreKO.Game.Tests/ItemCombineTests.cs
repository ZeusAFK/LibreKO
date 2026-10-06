using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ItemCombineTests : GameTestBase
{
    private const int Shozin = 19073;
    private const int Juel = 31402;
    private const int Raum = 128410901;
    private const int BlessedStone = 389227000;
    private const int Dredium = 389235000;
    private const int OldSkeletonBelt = 345000808;
    private const int NestScrap = 810098000;
    private const int ScrapOfSteel = 810099000;
    private const int SoftStone = 810101000;
    private const int SkeletonBelt = 340410115;
    private const byte Moradon = 21;
    private const short RaumRow = 1;
    private const short JuelRow = 17;
    private const int FailsEvenWithShadowPiece = -ItemCombineService.ShadowPieceBonus;

    private sealed record Player(UserSession Session, IClient Client, List<Packet> Sent, int Npc);

    private static ServiceProvider Provider(int rate)
    {
        var recipes = new[]
        {
            new ItemCombineRecipeData { Id = RaumRow, NpcId = Shozin, DisplayRow = RaumRow, ResultItemId = Raum, ResultCount = 1, SuccessRate = rate },
            new ItemCombineRecipeData { Id = 1001, NpcId = Juel, DisplayRow = JuelRow, ResultItemId = SkeletonBelt, ResultCount = 1, SuccessRate = rate },
        };
        var materials = new[]
        {
            new ItemCombineMaterialData { RecipeId = RaumRow, Position = 0, ItemId = BlessedStone, Count = 10 },
            new ItemCombineMaterialData { RecipeId = RaumRow, Position = 1, ItemId = Dredium, Count = 50 },
            new ItemCombineMaterialData { RecipeId = 1001, Position = 0, ItemId = OldSkeletonBelt, Count = 3 },
            new ItemCombineMaterialData { RecipeId = 1001, Position = 1, ItemId = NestScrap, Count = 5 },
            new ItemCombineMaterialData { RecipeId = 1001, Position = 2, ItemId = ScrapOfSteel, Count = 50 },
            new ItemCombineMaterialData { RecipeId = 1001, Position = 3, ItemId = SoftStone, Count = 1 },
        };
        return CreateProvider(_ => { }, gameData =>
        {
            gameData.ItemCombineRecipes.Returns(recipes);
            gameData.ItemCombineMaterialsByRecipe.Returns(materials.ToLookup(m => m.RecipeId));
            foreach (var id in new[] { BlessedStone, Dredium, NestScrap, ScrapOfSteel, SoftStone, ItemCombineService.ShadowPiece })
                gameData.GetItem(id).Returns(new ItemData { Num = id, Name = $"Material {id}", Kind = 255, Slot = 15, Countable = 1, Duration = 1, ReqLevelMax = 100 });
            foreach (var id in new[] { Raum, SkeletonBelt, OldSkeletonBelt })
                gameData.GetItem(id).Returns(new ItemData { Num = id, Name = $"Gear {id}", Kind = 21, Slot = 1, Countable = 0, Duration = 5000, ReqLevelMax = 100 });
        });
    }

    private static Player Join(ServiceProvider provider, int npcId, float npcX = 100)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId: 100, accountId: 1100);
        session.Name = "Crafter";
        session.Level = 70;
        session.Nation = AccountNation.Karus;
        session.ZoneId = Moradon;
        session.Hp = 100;
        session.X = 100;
        session.Z = 100;
        sessions.Regions.AddToRegion(session);
        var npc = sessions.Regions.SpawnNpc(new NpcInstance { NpcId = npcId, Name = "Craftsman", Level = 50, ZoneId = Moradon, X = npcX, Z = 100, MaxHp = 100, Hp = 100 });
        return new Player(session, client, sent, npc.UniqueId);
    }

    private static void Hold(Player player, byte bagSlot, int itemId, int count) =>
        player.Session.Inventory[InventoryConstants.SlotMax + bagSlot] = new ItemSlot { ItemId = itemId, Count = (ushort)count, Durability = 1 };

    private static Task Combine(ServiceProvider provider, Player player, params (byte Slot, int ItemId, int Count)[] materials) =>
        CombineWithShadow(provider, player, 0, 0, materials);

    private static async Task CombineWithShadow(ServiceProvider provider, Player player, int shadowItem, byte shadowSlot,
        params (byte Slot, int ItemId, int Count)[] materials)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte((byte)ItemUpgradeSubOpcode.Combine);
        packet.WriteInt(player.Npc);
        packet.WriteInt(shadowItem);
        packet.WriteByte(shadowSlot);
        packet.WriteByte((byte)materials.Length);
        foreach (var material in materials)
            packet.WriteByte(material.Slot);
        packet.WriteSByteString(string.Concat(materials.Select(m => $"{m.ItemId:D9}{m.Count:D3}")));
        packet.ResetOffset();
        await provider.GetRequiredService<IItemUpgradeService>().HandleUpgradeAsync(player.Client, packet);
    }

    private static Packet Reply(Player player)
    {
        var reply = player.Sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        reply.ResetOffset();
        reply.ReadByte().Should().Be((byte)ItemUpgradeSubOpcode.Combine);
        return reply;
    }

    private static ItemSlot Bag(Player player, int bagSlot) => player.Session.Inventory[InventoryConstants.SlotMax + bagSlot];

    [Fact]
    public async Task ARecipeThatSucceedsGivesTheItemBeforeTheReplyAndTakesTheMaterials()
    {
        using var provider = Provider(ItemCombineRecipeData.RateScale);
        var player = Join(provider, Shozin);
        Hold(player, 0, BlessedStone, 12);
        Hold(player, 1, Dredium, 50);

        await Combine(provider, player, (0, BlessedStone, 10), (1, Dredium, 50));

        var reply = Reply(player);
        reply.ReadByte().Should().Be(1);
        reply.ReadShort().Should().Be(RaumRow);
        reply.ReadInt().Should().Be(Raum);
        var slot = reply.ReadByte();
        Bag(player, slot).ItemId.Should().Be(Raum);
        slot.Should().NotBe((byte)1);
        Bag(player, 0).Count.Should().Be(2);
        Bag(player, 1).IsEmpty.Should().BeTrue();
        var granted = player.Sent.FindIndex(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
        granted.Should().BeLessThan(player.Sent.FindIndex(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE));
    }

    [Fact]
    public async Task AFailedCombinationLosesTheMaterialsAndTheShadowPiece()
    {
        using var provider = Provider(FailsEvenWithShadowPiece);
        var player = Join(provider, Shozin);
        Hold(player, 0, BlessedStone, 10);
        Hold(player, 1, Dredium, 50);
        Hold(player, 2, ItemCombineService.ShadowPiece, 1);

        await CombineWithShadow(provider, player, ItemCombineService.ShadowPiece, 3, (0, BlessedStone, 10), (1, Dredium, 50));
        Reply(player).ReadByte().Should().Be(3);

        await CombineWithShadow(provider, player, ItemCombineService.ShadowPiece, 2, (0, BlessedStone, 10), (1, Dredium, 50));

        var reply = Reply(player);
        reply.ReadByte().Should().Be(2);
        reply.ReadShort().Should().Be(RaumRow);
        Bag(player, 0).IsEmpty.Should().BeTrue();
        Bag(player, 1).IsEmpty.Should().BeTrue();
        Bag(player, 2).IsEmpty.Should().BeTrue();
        player.Session.Inventory.Should().NotContain(s => s.ItemId == Raum);
        var effect = player.Sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_NPC_EVENT);
        effect.ResetOffset();
        effect.ReadByte().Should().Be(1);
        effect.ReadByte().Should().Be(2);
        effect.ReadShort().Should().Be((short)Shozin);
        effect.ReadShort().Should().Be(RaumRow);
    }

    [Fact]
    public async Task ThreeIdenticalOldAccessoriesMakeJuelsNewOne()
    {
        using var provider = Provider(ItemCombineRecipeData.RateScale);
        var player = Join(provider, Juel);
        Hold(player, 0, OldSkeletonBelt, 1);
        Hold(player, 1, OldSkeletonBelt, 1);
        Hold(player, 2, OldSkeletonBelt, 1);
        Hold(player, 3, NestScrap, 5);
        Hold(player, 4, ScrapOfSteel, 50);
        Hold(player, 5, SoftStone, 1);

        await Combine(provider, player, (3, NestScrap, 5), (0, OldSkeletonBelt, 1), (1, OldSkeletonBelt, 1),
            (2, OldSkeletonBelt, 1), (4, ScrapOfSteel, 50), (5, SoftStone, 1));

        var reply = Reply(player);
        reply.ReadByte().Should().Be(1);
        reply.ReadShort().Should().Be(JuelRow);
        reply.ReadInt().Should().Be(SkeletonBelt);
    }

    [Fact]
    public async Task MaterialsThatAreNotARecipeAreTheWrongMaterial()
    {
        using var provider = Provider(ItemCombineRecipeData.RateScale);
        var player = Join(provider, Shozin);
        Hold(player, 0, BlessedStone, 10);
        Hold(player, 1, Dredium, 50);

        await Combine(provider, player, (0, BlessedStone, 9), (1, Dredium, 50));

        Reply(player).ReadByte().Should().Be(3);
        Bag(player, 0).Count.Should().Be(10);
    }

    [Fact]
    public async Task ACountAboveTheNamedStackIsRefused()
    {
        using var provider = Provider(ItemCombineRecipeData.RateScale);
        var player = Join(provider, Shozin);
        Hold(player, 0, BlessedStone, 6);
        Hold(player, 2, BlessedStone, 4);
        Hold(player, 1, Dredium, 50);

        await Combine(provider, player, (0, BlessedStone, 10), (1, Dredium, 50));

        Reply(player).ReadByte().Should().Be(3);
    }

    [Fact]
    public async Task ASealedMaterialIsRefused()
    {
        using var provider = Provider(ItemCombineRecipeData.RateScale);
        var player = Join(provider, Shozin);
        Hold(player, 0, BlessedStone, 10);
        Hold(player, 1, Dredium, 50);
        Bag(player, 1).Flag = (byte)ItemFlag.Sealed;

        await Combine(provider, player, (0, BlessedStone, 10), (1, Dredium, 50));

        Reply(player).ReadByte().Should().Be(3);
    }

    [Fact]
    public async Task ShozinsRecipesAreNotJuels()
    {
        using var provider = Provider(ItemCombineRecipeData.RateScale);
        var player = Join(provider, Juel);
        Hold(player, 0, BlessedStone, 10);
        Hold(player, 1, Dredium, 50);

        await Combine(provider, player, (0, BlessedStone, 10), (1, Dredium, 50));

        Reply(player).ReadByte().Should().Be(3);
    }

    [Fact]
    public async Task TheCraftsmanMustBeNearby()
    {
        using var provider = Provider(ItemCombineRecipeData.RateScale);
        var player = Join(provider, Shozin, npcX: 400);
        Hold(player, 0, BlessedStone, 10);
        Hold(player, 1, Dredium, 50);

        await Combine(provider, player, (0, BlessedStone, 10), (1, Dredium, 50));

        Reply(player).ReadByte().Should().Be(0);
        Bag(player, 0).Count.Should().Be(10);
    }

    [Fact]
    public async Task AGarbledRequestStillGetsAnAnswer()
    {
        using var provider = Provider(ItemCombineRecipeData.RateScale);
        var player = Join(provider, Shozin);
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte((byte)ItemUpgradeSubOpcode.Combine);
        packet.WriteInt(player.Npc);
        packet.WriteInt(0);
        packet.WriteByte(0);
        packet.WriteByte(1);
        packet.WriteByte(0);
        packet.WriteSByteString("38922700001");
        packet.ResetOffset();

        await provider.GetRequiredService<IItemUpgradeService>().HandleUpgradeAsync(player.Client, packet);

        Reply(player).ReadByte().Should().Be(3);
    }
}
