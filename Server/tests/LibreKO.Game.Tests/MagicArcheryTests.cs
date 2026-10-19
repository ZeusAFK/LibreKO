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

public class MagicArcheryTests : GameTestBase
{
    private const int Shot = 107003;
    private const int Volley = 107040;
    private const int VolleyArrows = 3;
    private const int Bow = 160210001;
    private const int Arrow = 391010000;
    private const ushort Quiver = 50;
    private const int WormHp = 100_000;
    private const short Archer = 107;

    [Fact]
    public async Task AnArrowThatWasNeverLoosedDoesNotHit()
    {
        using var provider = Provider();
        var (archer, client) = ArcherWith(provider, bow: true);
        var worm = Worm(provider);

        await Send(provider, client, archer, MagicProcessOpcode.Effecting, Shot, worm.UniqueId);

        worm.Hp.Should().Be(WormHp);
    }

    [Fact]
    public async Task ALoosedArrowHitsOnce()
    {
        using var provider = Provider();
        var (archer, client) = ArcherWith(provider, bow: true);
        var worm = Worm(provider);

        await Send(provider, client, archer, MagicProcessOpcode.Flying, Shot, worm.UniqueId);
        await Send(provider, client, archer, MagicProcessOpcode.Effecting, Shot, worm.UniqueId);
        var afterOneHit = worm.Hp;
        await Send(provider, client, archer, MagicProcessOpcode.Effecting, Shot, worm.UniqueId);

        afterOneHit.Should().BeLessThan(WormHp);
        worm.Hp.Should().Be(afterOneHit, "one arrow was loosed, so one arrow lands");
        Arrows(archer).Should().Be(Quiver - 1);
    }

    [Fact]
    public async Task AVolleyHitsOncePerArrow()
    {
        using var provider = Provider();
        var (archer, client) = ArcherWith(provider, bow: true);
        var worm = Worm(provider);

        await Send(provider, client, archer, MagicProcessOpcode.Flying, Volley, worm.UniqueId);
        var hits = new List<int>();
        for (var arrow = 0; arrow <= VolleyArrows; arrow++)
        {
            var before = worm.Hp;
            await Send(provider, client, archer, MagicProcessOpcode.Effecting, Volley, worm.UniqueId);
            hits.Add(before - worm.Hp);
        }

        hits.Take(VolleyArrows).Should().OnlyContain(damage => damage > 0);
        hits[VolleyArrows].Should().Be(0, "a fourth hit is not part of a three-arrow volley");
    }

    [Fact]
    public async Task ArcheryNeedsABow()
    {
        using var provider = Provider();
        var (archer, client) = ArcherWith(provider, bow: false);
        var worm = Worm(provider);

        await Send(provider, client, archer, MagicProcessOpcode.Flying, Shot, worm.UniqueId);
        await Send(provider, client, archer, MagicProcessOpcode.Effecting, Shot, worm.UniqueId);

        Arrows(archer).Should().Be(Quiver);
        worm.Hp.Should().Be(WormHp);
    }

    private static ServiceProvider Provider() => CreateProvider(_ => { }, gameData =>
    {
        foreach (var (id, arrows) in new[] { (Shot, 1), (Volley, VolleyArrows) })
            gameData.GetMagic(id).Returns(new MagicData { Id = id, Type1 = (byte)MagicSkillType.Ranged, Moral = 7, Range = 40, UseItem = Arrow });
        gameData.MagicType2Table.Returns(new Dictionary<int, MagicType2Data>
        {
            [Shot] = new() { Id = Shot, HitType = 1, HitRate = 101, AddDamage = 100, NeedArrow = 1 },
            [Volley] = new() { Id = Volley, HitType = 1, HitRate = 101, AddDamage = 100, NeedArrow = VolleyArrows },
        });
        gameData.GetItem(Bow).Returns(new ItemData { Num = Bow, Kind = 70, Slot = 4, Delay = 150, Range = 350, Duration = 83 });
        gameData.GetItem(Arrow).Returns(new ItemData { Num = Arrow, Kind = 120, Countable = 1, Duration = 1, ReqLevelMax = 83 });
    });

    private static (UserSession Archer, IClient Client) ArcherWith(ServiceProvider provider, bool bow)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        var archer = sessions.CreateSession(client, characterId: 760, accountId: 860);
        archer.Name = "Archer";
        archer.Class = Archer;
        archer.Level = 60;
        archer.Nation = AccountNation.Karus;
        archer.ZoneId = 21;
        archer.X = 100;
        archer.Z = 100;
        archer.Hp = archer.MaxHp = 500;
        archer.Mp = archer.MaxMp = 500;
        archer.AttackAmount = 100;
        archer.Stats.TotalHit = 100;
        if (bow)
        {
            archer.Inventory[InventoryConstants.LeftHand].ItemId = Bow;
            archer.Inventory[InventoryConstants.LeftHand].Count = 1;
            archer.Inventory[InventoryConstants.LeftHand].Durability = 83;
        }
        archer.Inventory[InventoryConstants.InventoryStart].ItemId = Arrow;
        archer.Inventory[InventoryConstants.InventoryStart].Count = Quiver;
        archer.Inventory[InventoryConstants.InventoryStart].Durability = 1;
        sessions.Regions.AddToRegion(archer);
        return (archer, client);
    }

    private static NpcInstance Worm(ServiceProvider provider) =>
        provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true, NpcId = 750, Name = "Worm", ZoneId = 21, X = 110, Z = 100, SpawnX = 110,
            SpawnZ = 100, Hp = WormHp, MaxHp = WormHp, Ac = 5, EvadeRate = 1,
        });

    private static int Arrows(UserSession archer) => archer.Inventory[InventoryConstants.InventoryStart].Count;

    private static async Task Send(
        ServiceProvider provider, IClient client, UserSession archer, MagicProcessOpcode stage, int skillId, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)stage);
        packet.WriteInt(skillId);
        packet.WriteInt(archer.CharacterId);
        packet.WriteInt(targetId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);
        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(client, packet);
    }
}
