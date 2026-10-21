using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ExpiredGearTests : GameTestBase
{
    private const int Ring = 700200;
    private const int Sword = 700201;
    private const int Bow = 700202;
    private const int Charm = 700203;
    private const int OtherCharm = 700204;
    private const int Costume = 700205;
    private const int CharmSkill = 900002;
    private const short Warrior = 101;
    private const long Now = 1_800_000_000;
    private const short ItemAc = 30;
    private const short ItemHp = 200;
    private const short ItemWeight = 5;
    private const short WeaponDamage = 100;
    private const int CostumeSlot = InventoryConstants.CospreStart + InventoryConstants.CosPosHelmet;
    private const int CharmSlot = InventoryConstants.Neck;
    private const int OtherCharmSlot = InventoryConstants.RightEar;
    private const int BrokenPlayer = 640;
    private const int HealthyPlayer = 641;

    [Theory]
    [InlineData(InventoryConstants.RightRing, Ring)]
    [InlineData(CostumeSlot, Costume)]
    public void AnExpiredWornItemWeighsButGivesNothing(int slot, int itemId)
    {
        var gameData = Data();
        var bare = Calculate(Inventory(), gameData);
        var worn = Calculate(Inventory((slot, itemId, Now + 1)), gameData);
        var expired = Calculate(Inventory((slot, itemId, Now)), gameData);

        worn.TotalAc.Should().BeGreaterThan(bare.TotalAc);
        expired.TotalAc.Should().Be(bare.TotalAc);
        expired.MaxHp.Should().Be(bare.MaxHp);
        expired.ItemWeight.Should().Be(worn.ItemWeight, "an expired item is still carried").And.BeGreaterThan(bare.ItemWeight);
    }

    [Theory]
    [InlineData(InventoryConstants.RightHand, Sword)]
    [InlineData(InventoryConstants.LeftHand, Bow)]
    public void AnExpiredWeaponAddsNoDamage(int slot, int weapon)
    {
        var gameData = Data();
        var bare = Calculate(Inventory(), gameData);
        var worn = Calculate(Inventory((slot, weapon, Now + 1)), gameData);
        var expired = Calculate(Inventory((slot, weapon, Now)), gameData);

        worn.TotalHit.Should().BeGreaterThan(bare.TotalHit);
        expired.TotalHit.Should().Be(bare.TotalHit);
    }

    [Fact]
    public async Task AWornItemThatRunsOutStopsCountingAtTheNextSweep()
    {
        using var provider = CreateProvider(_ => { }, Data);
        var (player, sent) = Player(provider);
        var charm = player.Inventory[CharmSlot];
        (charm.ItemId, charm.Count, charm.Durability) = (Charm, 1, 1);
        player.SetBuff(CharmSkill, CharmBuff(player));
        player.RecalculateStatsWithBuffs(provider.GetRequiredService<IGameDataService>());
        var wornAc = player.Stats.TotalAc;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        charm.ExpiresAt = now - 30;
        var expiry = Expiry(provider);

        await expiry.ProcessTickAsync(now - 60);
        await expiry.ProcessTickAsync(now);
        await expiry.ProcessTickAsync(now + 60);

        player.Inventory[CharmSlot].ItemId.Should().Be(Charm, "an expired worn item stays where it is");
        player.Stats.TotalAc.Should().BeLessThan(wornAc);
        player.ActiveBuffs.Should().NotContainKey(CharmSkill, "what the item gave on being worn ends with it");
        sent.Count(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_MOVE).Should().Be(1, "the client hears the new stats once, when the item runs out");
    }

    [Fact]
    public async Task AWornCostumeThatRunsOutStopsCountingToo()
    {
        using var provider = CreateProvider(_ => { }, Data);
        var (player, sent) = Player(provider);
        var costume = player.Inventory[CostumeSlot];
        (costume.ItemId, costume.Count, costume.Durability) = (Costume, 1, 1);
        player.RecalculateStatsWithBuffs(provider.GetRequiredService<IGameDataService>());
        var wornAc = player.Stats.TotalAc;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        costume.ExpiresAt = now - 30;
        var expiry = Expiry(provider);

        await expiry.ProcessTickAsync(now - 60);
        await expiry.ProcessTickAsync(now);

        player.Stats.TotalAc.Should().BeLessThan(wornAc);
        sent.Count(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_MOVE).Should().Be(1);
    }

    [Fact]
    public async Task AnExpiredItemDoesNotEndABuffItsTwinStillGives()
    {
        using var provider = CreateProvider(_ => { }, Data);
        var (player, _) = Player(provider);
        foreach (var slot in new[] { CharmSlot, OtherCharmSlot })
            (player.Inventory[slot].ItemId, player.Inventory[slot].Count, player.Inventory[slot].Durability) = (Charm, 1, 1);
        player.SetBuff(CharmSkill, CharmBuff(player));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        player.Inventory[CharmSlot].ExpiresAt = now - 30;
        var expiry = Expiry(provider);

        await expiry.ProcessTickAsync(now - 60);
        await expiry.ProcessTickAsync(now);

        player.ActiveBuffs.Should().ContainKey(CharmSkill, "the twin still in time keeps giving it");
    }

    [Fact]
    public async Task OnePlayersFailedSweepDoesNotCostTheOthersTheirs()
    {
        using var provider = CreateProvider(_ => { }, Data);
        var gameData = provider.GetRequiredService<IGameDataService>();
        var (broken, _) = Player(provider, BrokenPlayer);
        broken.Client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("the connection is gone")));
        var (player, _) = Player(provider, HealthyPlayer);
        foreach (var wearer in new[] { broken, player })
        {
            var costume = wearer.Inventory[CostumeSlot];
            (costume.ItemId, costume.Count, costume.Durability) = (Costume, 1, 1);
            wearer.RecalculateStatsWithBuffs(gameData);
        }
        var wornAc = player.Stats.TotalAc;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        broken.Inventory[CostumeSlot].ExpiresAt = player.Inventory[CostumeSlot].ExpiresAt = now - 30;
        var expiry = Expiry(provider);

        await expiry.ProcessTickAsync(now - 60);
        await expiry.ProcessTickAsync(now);

        player.Stats.TotalAc.Should().BeLessThan(wornAc);
    }

    [Fact]
    public async Task AnExpiredItemPutOnGivesNoBuff()
    {
        using var provider = CreateProvider(_ => { }, Data);
        var (player, _) = Player(provider);
        var charm = player.Inventory[CharmSlot];
        (charm.ItemId, charm.Count, charm.Durability) = (Charm, 1, 1);
        charm.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;
        var effects = provider.GetRequiredService<IItemEquipmentEffectService>();

        await effects.ApplyMoveEffectsAsync(player, ItemMoveDirection.InventoryToSlot, Charm, 0);
        player.ActiveBuffs.Should().NotContainKey(CharmSkill);

        charm.ExpiresAt = 0;
        await effects.ApplyMoveEffectsAsync(player, ItemMoveDirection.InventoryToSlot, Charm, 0);
        player.ActiveBuffs.Should().ContainKey(CharmSkill);
    }

    [Fact]
    public async Task AnExpiredItemDoesNotKeepABuffAnotherItemGave()
    {
        using var provider = CreateProvider(_ => { }, Data);
        var (player, _) = Player(provider);
        var other = player.Inventory[OtherCharmSlot];
        (other.ItemId, other.Count, other.Durability) = (OtherCharm, 1, 1);
        other.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;
        player.SetBuff(CharmSkill, CharmBuff(player));

        await provider.GetRequiredService<IItemEquipmentEffectService>().ApplyRemovalEffectsAsync(player, Charm);

        player.ActiveBuffs.Should().NotContainKey(CharmSkill);
    }

    private static ActiveBuff CharmBuff(UserSession player) => new()
    {
        MagicId = CharmSkill, CasterId = player.CharacterId, ExpireTicks = long.MaxValue, BuffType = BuffType.Damage, BonusAttack = 10,
    };

    private static ItemExpiryService Expiry(ServiceProvider provider) => new(
        provider.GetRequiredService<SessionManager>(),
        provider.GetRequiredService<IItemEquipmentEffectService>(),
        provider.GetRequiredService<IUserNotificationService>(),
        NullLogger<ItemExpiryService>.Instance);

    private static IGameDataService Data()
    {
        var gameData = Substitute.For<IGameDataService>();
        Data(gameData);
        return gameData;
    }

    private static void Data(IGameDataService gameData)
    {
        gameData.GetCoefficient(Warrior).Returns(CreateBasicCoefficient(Warrior));
        foreach (var armour in new[] { Ring, Charm, OtherCharm, Costume })
            gameData.GetItem(armour).Returns(new ItemData { Num = armour, Ac = ItemAc, MaxHpB = ItemHp, Weight = ItemWeight, Duration = 1 });
        gameData.GetItem(Sword).Returns(new ItemData { Num = Sword, Kind = (byte)ItemKind.SwordOneHand, Damage = WeaponDamage, Weight = ItemWeight });
        gameData.GetItem(Bow).Returns(new ItemData { Num = Bow, Kind = (byte)ItemKind.Bow, Damage = WeaponDamage, Weight = ItemWeight });
        foreach (var charm in new[] { Charm, OtherCharm })
            gameData.GetItemOps(charm).Returns([new ItemOpData { ItemId = charm, TriggerType = HealStrike.ItemOpOnEquip, SkillId = CharmSkill, TriggerRate = 100 }]);
        gameData.GetMagic(CharmSkill).Returns(new MagicData { Id = CharmSkill, Etc = 1 });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
        {
            [1] = new() { Id = 1, BuffType = (byte)BuffType.Damage, Attack = 10, Duration = 60 },
        });
    }

    private static DerivedStats Calculate(ItemSlot[] inventory, IGameDataService gameData) => AbilityCalculator.Calculate(
        level: 60, strength: 60, stamina: 60, dexterity: 60, intelligence: 60,
        classId: Warrior, CreateBasicCoefficient(Warrior), inventory, gameData, nowUnixSeconds: Now);

    private static ItemSlot[] Inventory(params (int Slot, int ItemId, long ExpiresAt)[] worn)
    {
        var inventory = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var slot = 0; slot < inventory.Length; slot++)
            inventory[slot] = new ItemSlot();
        foreach (var (slot, itemId, expiresAt) in worn)
            inventory[slot] = new ItemSlot { ItemId = itemId, Count = 1, Durability = 1, ExpiresAt = expiresAt };
        return inventory;
    }

    private static (UserSession Player, List<Packet> Sent) Player(ServiceProvider provider, int characterId = BrokenPlayer)
    {
        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var player = sessions.CreateSession(client, characterId, characterId);
        (player.Class, player.Level, player.Hp, player.ZoneId) = (Warrior, 60, 100, 1);
        (player.Strength, player.Stamina, player.Dexterity, player.Intelligence, player.Magic) = (60, 60, 60, 60, 60);
        sessions.Regions.AddToRegion(player);
        return (player, sent);
    }
}
