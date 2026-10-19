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

public class LootCoinTests : GameTestBase
{
    private const byte Moradon = 21;
    private const short Warrior = 101;
    private const int SuperNoah = 504003;
    private const int HyperNoah = 504004;
    private const int CospreItem = 900000000;

    private static readonly (int Skill, int Item, short Uses, short Percent)[] NoahScrolls =
    [
        (SuperNoah, 810050000, 1, 150),
        (HyperNoah, 810051000, 10, 200),
    ];

    [Fact]
    public async Task AKilledMonsterLeavesItsCoinsInTheLootBox()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (killer, sent) = CreatePlayer(sessionManager, 210, level: 40, x: 100, z: 200);

        var npc = new NpcInstance
        {
            UniqueId = 10042,
            NpcId = 750,
            ZoneId = Moradon,
            X = 100,
            Z = 200,
            GoldDrop = 1000,
            IsMonster = true,
        };
        npc.DamageMap[killer.CharacterId] = 10;
        npc.TopDamagerCharId = killer.CharacterId;

        await provider.GetRequiredService<ICombatRewardService>().AwardNpcKillAsync(npc, killer);

        killer.Money.Should().Be(0);
        var drop = sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_DROP);
        drop.ResetOffset();
        drop.ReadInt().Should().Be(npc.UniqueId);
        var bundle = sessionManager.Regions.GetBundle(drop.ReadInt());
        bundle.Should().NotBeNull();
        bundle!.Items.Should().ContainSingle();
        bundle.Items[0].ItemId.Should().Be(InventoryConstants.ItemGold);
        bundle.Items[0].Count.Should().BeInRange(700, 1000);
    }

    [Fact]
    public async Task ASoloLooterTakesTheCoinsWithTheirPremiumBonus()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            gameData.GetPremiumProperty(Arg.Any<byte>(), PremiumPropertyType.Noah).Returns(50));
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (looter, sent) = CreatePlayer(sessionManager, 211, level: 40, x: 100, z: 100);
        looter.Money = 1000;
        var bundle = CoinBox(sessionManager, looter, coins: 200);

        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(looter.Client, TakeCoins(bundle));

        looter.Money.Should().Be(1300);
        sessionManager.Regions.GetBundle(bundle.BundleId).Should().BeNull();
        var reply = sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_GET);
        reply.ResetOffset();
        reply.ReadByte().Should().Be(ItemGetPacketWriter.ResultSuccess);
        reply.ReadInt().Should().Be(bundle.BundleId);
        reply.ReadByte().Should().Be(ItemGetPacketWriter.PositionGold);
        reply.ReadInt().Should().Be(InventoryConstants.ItemGold);
        reply.ReadUShort().Should().Be(200);
        reply.ReadInt().Should().Be(1300);
    }

    [Fact]
    public async Task PartyCoinsAreSharedByLevelAmongMembersInRange()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (leader, leaderSent) = CreatePlayer(sessionManager, 300, level: 30, x: 100, z: 100);
        var (member, memberSent) = CreatePlayer(sessionManager, 301, level: 10, x: 110, z: 100);
        var (far, farSent) = CreatePlayer(sessionManager, 302, level: 20, x: 400, z: 400);

        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        party.MemberIds[2] = (short)far.CharacterId;
        leader.PartyIndex = member.PartyIndex = far.PartyIndex = party.Index;
        leader.IsPartyLeader = true;

        var bundle = CoinBox(sessionManager, leader, coins: 600);
        bundle.OwnerPartyIndex = party.Index;

        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(leader.Client, TakeCoins(bundle));

        leader.Money.Should().Be(300);
        member.Money.Should().Be(100);
        far.Money.Should().Be(0);
        farSent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_GET);
        sessionManager.Regions.GetBundle(bundle.BundleId).Should().BeNull();

        foreach (var (sent, money) in new[] { (leaderSent, 300), (memberSent, 100) })
        {
            var share = sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_GET);
            share.ResetOffset();
            share.ReadByte().Should().Be(ItemGetPacketWriter.ResultPartyCoins);
            share.ReadInt().Should().Be(bundle.BundleId);
            share.ReadByte().Should().Be(ItemGetPacketWriter.PositionGold);
            share.ReadInt().Should().Be(InventoryConstants.ItemGold);
            share.ReadInt().Should().Be(money);
            share.RemainingBytes.Should().Be(0);
        }
    }

    [Fact]
    public async Task ATradingPlayerCannotPickUpLoot()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (looter, _) = CreatePlayer(sessionManager, 218, level: 40, x: 100, z: 100);
        looter.Money = 1000;
        looter.Trade.ExchangeUser = 9_999;
        var bundle = CoinBox(sessionManager, looter, coins: 200);

        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(looter.Client, TakeCoins(bundle));

        looter.Money.Should().Be(1000, "a trading player's purse takes nothing in");
        sessionManager.Regions.GetBundle(bundle.BundleId).Should().NotBeNull();
    }

    [Theory]
    [InlineData(SuperNoah, 1300)]
    [InlineData(HyperNoah, 1400)]
    public async Task ANoahScrollRaisesTheCoinsALooterPicksUp(int scroll, int money)
    {
        using var provider = CreateProvider(_ => { }, NoahScrollData);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (looter, sent) = CreatePlayer(sessionManager, 212, level: 40, x: 100, z: 100);
        looter.Money = 1000;
        await ReadScrollAsync(provider, looter, scroll);
        var bundle = CoinBox(sessionManager, looter, coins: 200);

        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(looter.Client, TakeCoins(bundle));

        looter.Money.Should().Be(money);
        var reply = sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_GET);
        reply.ResetOffset();
        reply.ReadByte().Should().Be(ItemGetPacketWriter.ResultSuccess);
        reply.ReadInt().Should().Be(bundle.BundleId);
        reply.ReadByte().Should().Be(ItemGetPacketWriter.PositionGold);
        reply.ReadInt().Should().Be(InventoryConstants.ItemGold);
        reply.ReadUShort().Should().Be(200);
        reply.ReadInt().Should().Be(money);
    }

    [Fact]
    public async Task AHyperNoahScrollDoublesTheCoinsThePremiumBonusRaised()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            NoahScrollData(gameData);
            gameData.GetPremiumProperty(Arg.Any<byte>(), PremiumPropertyType.Noah).Returns(50);
        });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (looter, _) = CreatePlayer(sessionManager, 213, level: 40, x: 100, z: 100);
        await ReadScrollAsync(provider, looter, HyperNoah);
        var bundle = CoinBox(sessionManager, looter, coins: 200);

        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(looter.Client, TakeCoins(bundle));

        looter.Money.Should().Be(600);
    }

    [Fact]
    public async Task AHyperNoahScrollAndASetCoinBonusAddUp()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            NoahScrollData(gameData);
            gameData.GetItem(CospreItem).Returns(new ItemData { Num = CospreItem, Kind = (byte)ItemKind.Cospre });
            gameData.GetSetItem(CospreItem).Returns(new SetItemData { SetIndex = CospreItem, CoinBonusPercent = 30 });
        });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (looter, _) = CreatePlayer(sessionManager, 214, level: 40, x: 100, z: 100);
        looter.Inventory[InventoryConstants.CospreStart] = new ItemSlot { ItemId = CospreItem, Count = 1 };
        await ReadScrollAsync(provider, looter, HyperNoah);
        looter.Stats.ItemCoinBonusPercent.Should().Be(30);
        var bundle = CoinBox(sessionManager, looter, coins: 200);

        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(looter.Client, TakeCoins(bundle));

        looter.Money.Should().Be(460);
    }

    [Fact]
    public async Task OnlyThePartyMemberWithAHyperNoahScrollGetsMoreOfTheSharedCoins()
    {
        using var provider = CreateProvider(_ => { }, NoahScrollData);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (leader, _) = CreatePlayer(sessionManager, 310, level: 30, x: 100, z: 100);
        var (member, _) = CreatePlayer(sessionManager, 311, level: 10, x: 110, z: 100);
        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        leader.PartyIndex = member.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        await ReadScrollAsync(provider, member, HyperNoah);

        var bundle = CoinBox(sessionManager, leader, coins: 400);
        bundle.OwnerPartyIndex = party.Index;
        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(leader.Client, TakeCoins(bundle));

        leader.Money.Should().Be(300);
        member.Money.Should().Be(200);
    }

    [Fact]
    public async Task TheCoinBonusEndsWithTheNoahScroll()
    {
        using var provider = CreateProvider(_ => { }, NoahScrollData);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (looter, _) = CreatePlayer(sessionManager, 215, level: 40, x: 100, z: 100);
        await ReadScrollAsync(provider, looter, HyperNoah);
        await provider.GetRequiredService<IMagicExecutionService>().CancelAsync(looter, HyperNoah);
        var bundle = CoinBox(sessionManager, looter, coins: 200);

        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(looter.Client, TakeCoins(bundle));

        looter.Money.Should().Be(200);
    }

    [Fact]
    public async Task ANoahScrollKeepsItsCoinBonusAfterRelogging()
    {
        using var provider = CreateProvider(_ => { }, NoahScrollData);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var gameData = provider.GetRequiredService<IGameDataService>();
        var (before, _) = CreatePlayer(sessionManager, 216, level: 40, x: 100, z: 100);
        await ReadScrollAsync(provider, before, HyperNoah);
        var saved = before.SerializeSavedMagic();
        sessionManager.RemoveSession(before);

        var (looter, _) = CreatePlayer(sessionManager, 217, level: 40, x: 100, z: 100);
        looter.LoadSavedMagic(saved, gameData);
        looter.RecalculateStatsWithBuffs(gameData);
        var bundle = CoinBox(sessionManager, looter, coins: 200);

        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(looter.Client, TakeCoins(bundle));

        looter.Money.Should().Be(400);
    }

    private static void NoahScrollData(IGameDataService gameData)
    {
        gameData.GetCoefficient(Arg.Any<short>()).Returns(CreateBasicCoefficient(Warrior));
        gameData.MagicType4Table.Returns(NoahScrolls.ToDictionary(
            scroll => scroll.Skill,
            scroll => new MagicType4Data
            {
                Id = scroll.Skill, BuffType = (byte)BuffType.NoahBonus, Duration = 3600, ExpPct = scroll.Percent,
            }));
        foreach (var scroll in NoahScrolls)
        {
            gameData.GetMagic(scroll.Skill).Returns(new MagicData
            {
                Id = scroll.Skill, Moral = 1, ItemGroup = 9, UseItem = scroll.Item, SuccessRate = 100,
                Type1 = (byte)MagicSkillType.Buff,
            });
            gameData.GetItem(scroll.Item).Returns(new ItemData
            {
                Num = scroll.Item, Kind = (byte)ItemKind.PowerUpStore, Duration = scroll.Uses, ReqLevelMax = 60,
            });
        }
    }

    private static async Task ReadScrollAsync(ServiceProvider provider, UserSession player, int skill)
    {
        var scroll = NoahScrolls.Single(entry => entry.Skill == skill);
        player.Class = Warrior;
        player.Inventory[InventoryConstants.InventoryStart] = new ItemSlot
        {
            ItemId = scroll.Item, Count = 1, Durability = scroll.Uses,
        };
        var magic = provider.GetRequiredService<IGameDataService>().GetMagic(skill)!;
        await provider.GetRequiredService<IMagicExecutionService>()
            .ExecuteAsync(player, magic, skill, player.CharacterId, new int[7]);
        player.ActiveBuffs.Should().ContainKey(skill);
    }

    private static LootBundle CoinBox(SessionManager sessionManager, UserSession owner, ushort coins)
    {
        var bundle = sessionManager.Regions.CreateBundle(owner.X, owner.Z, owner.Y);
        bundle.OwnerCharId = owner.CharacterId;
        bundle.Items.Add(new LootItem { ItemId = InventoryConstants.ItemGold, Count = coins });
        return bundle;
    }

    private static Packet TakeCoins(LootBundle bundle)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_GET);
        packet.WriteInt(bundle.BundleId);
        packet.WriteInt(InventoryConstants.ItemGold);
        packet.WriteUShort(0);
        return packet;
    }

    private static (UserSession Player, List<Packet> Sent) CreatePlayer(
        SessionManager sessionManager, int characterId, byte level, float x, float z)
    {
        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var player = sessionManager.CreateSession(client, characterId, characterId + 100);
        player.Name = $"Player{characterId}";
        player.Level = level;
        player.Nation = AccountNation.Karus;
        player.ZoneId = Moradon;
        player.X = x;
        player.Z = z;
        player.Hp = 500;
        player.MaxHp = 500;
        sessionManager.Regions.AddToRegion(player);
        return (player, sent);
    }
}
