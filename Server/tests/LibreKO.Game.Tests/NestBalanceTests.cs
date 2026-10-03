using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NestBalanceTests
{
    private const byte FirstNest = 81;
    private const byte SecondNest = 82;
    private const byte ThirdNest = 83;
    private const short FirstFamilySet = 101;
    private const short FourthFamilySet = 104;
    private const short LastFamilySet = 113;
    private const short QuestSet = 1;
    private const int PotionMerchant = 12117;
    private const int NestBoss = 8718;
    private const int NestMinion = 8709;
    private const int OverLevelledBoss = 65;

    [Theory]
    [InlineData(FirstNest, FirstFamilySet, 24)]
    [InlineData(FirstNest, FourthFamilySet, 48)]
    [InlineData(ThirdNest, LastFamilySet, 79)]
    [InlineData(SecondNest, FirstFamilySet, 0)]
    [InlineData(FirstNest, QuestSet, 0)]
    public void AFamilyFightsAtTheMiddleOfTheLevelsThatDrawIt(byte zone, short set, int level)
    {
        MonsterStoneRules.FamilyLevel(zone, set).Should().Be(level);
    }

    [Fact]
    public void EveryNestFamilyIsLedByItsBossAtItsOwnLevel()
    {
        var npcs = JsonSerializer.Deserialize<List<NpcData>>(File.ReadAllText(Path.Combine(SeedDirectory(), "Npcs.json")))!
            .Where(n => n.IsMonster)
            .ToDictionary(n => n.Id);
        var families = Directory.GetFiles(SeedDirectory(), "NpcPositions.zone*.json")
            .SelectMany(path => JsonSerializer.Deserialize<List<NpcPosData>>(File.ReadAllText(path))!)
            .Where(p => MonsterStoneRules.FamilyLevel((byte)p.ZoneId, p.Room) > 0
                        && p.ActType < NpcPosData.NpcSpawnActTypeBase
                        && npcs.TryGetValue(p.NpcId, out var npc) && npc.Attack1 > 0)
            .GroupBy(p => (Zone: (byte)p.ZoneId, Set: p.Room))
            .ToList();

        families.Should().HaveCount(LastFamilySet - FirstFamilySet + 1);
        foreach (var family in families)
        {
            var level = MonsterStoneRules.FamilyLevel(family.Key.Zone, family.Key.Set);
            var spawned = family.Select(pos =>
            {
                var data = npcs[pos.NpcId];
                var npc = NpcInstance.FromData(data, pos, 0);
                NestBalance.Apply(npc, level, data.IsBoss);
                return (Npc: npc, data.IsBoss);
            }).ToList();

            var boss = spawned.Should().ContainSingle(s => s.IsBoss).Subject.Npc;
            var minions = spawned.Where(s => !s.IsBoss).Select(s => s.Npc).ToList();
            boss.Level.Should().Be((short)(level + NestBalance.BossLevelLead));
            minions.Should().OnlyContain(m => m.Level == level);
            minions.Should().OnlyContain(m => m.MaxHp < boss.MaxHp && m.Attack1 < boss.Attack1,
                $"set {family.Key.Set}'s boss outlasts and outhits its minions");
        }
    }

    [Fact]
    public void AnOverLevelledBossIsBroughtDownToItsFamily()
    {
        var data = new NpcData
        {
            Id = NestBoss, IsMonster = true, IsBoss = true, Level = OverLevelledBoss,
            Hp = 7123, Attack1 = 1032, Ac = 468, HitRate = 140, EvadeRate = 120, Experience = 900_000, Money = 600,
        };
        var npc = NpcInstance.FromData(data, new NpcPosData { ZoneId = FirstNest, NpcId = NestBoss, Room = FirstFamilySet }, 0);
        var level = MonsterStoneRules.FamilyLevel(FirstNest, FirstFamilySet);

        NestBalance.Apply(npc, level, isBoss: true);

        npc.Level.Should().Be((short)(level + NestBalance.BossLevelLead));
        npc.Attack1.Should().BeLessThan(data.Attack1);
        npc.Hp.Should().Be(npc.MaxHp);
    }

    [Fact]
    public void GatesChestsAndOtherPlacesKeepTheirSeededStats()
    {
        var gate = NpcInstance.FromData(new NpcData { IsMonster = true, Level = 90, Hp = 500 }, new NpcPosData(), 0);
        var elsewhere = NpcInstance.FromData(new NpcData { IsMonster = true, Level = 90, Hp = 500, Attack1 = 900 }, new NpcPosData(), 0);

        NestBalance.Apply(gate, 24, isBoss: false);
        NestBalance.Apply(elsewhere, 0, isBoss: false);

        gate.Level.Should().Be(90);
        gate.MaxHp.Should().Be(500);
        elsewhere.Level.Should().Be(90);
        elsewhere.Attack1.Should().Be(900);
    }

    [Fact]
    public async Task ANestRoomSpawnsItsMerchantsForBothNationsAndItsMonstersAtTheFamilyLevel()
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var player = sessions.CreateSession(client, 1, 1);
        player.Nation = AccountNation.Karus;
        player.ZoneId = (byte)ZoneId.Moradon;

        var merchantRow = new NpcPosData { Index = 1, ZoneId = FirstNest, NpcId = PotionMerchant, ActType = 100, NumNPC = 1, Room = FirstFamilySet };
        var minionRow = new NpcPosData { Index = 2, ZoneId = FirstNest, NpcId = NestMinion, ActType = 1, NumNPC = 1, Room = FirstFamilySet };
        var gameData = Substitute.For<IGameDataService>();
        gameData.NpcPositions.Returns(new List<NpcPosData> { merchantRow, minionRow });
        gameData.GetSpawnProto(merchantRow).Returns(new NpcData
        {
            Id = PotionMerchant, NpcType = NpcData.TypeTradeMerchant, Group = (byte)EntityNation.ElMorad,
        });
        gameData.GetSpawnProto(minionRow).Returns(new NpcData
        {
            Id = NestMinion, IsMonster = true, Level = 70, Hp = 5389, Attack1 = 1259, Group = (byte)EntityNation.All,
        });
        var registry = new InstanceRoomRegistry(sessions, Substitute.For<ILogger<InstanceRoomRegistry>>());
        var service = new InstanceEntryService(
            sessions, gameData, Substitute.For<IMonsterAggressionPolicy>(), Substitute.For<IZoneTransitionService>(),
            registry, Substitute.For<ILogger<InstanceEntryService>>());

        await service.EnterAloneAsync(player, FirstNest, FirstFamilySet, 0f, 0f, endsOnBossKill: true);

        var room = registry.Rooms.Should().ContainSingle().Subject;
        room.Npcs.Single(n => n.NpcId == PotionMerchant).Nation.Should().Be(EntityNation.None);
        room.Npcs.Single(n => n.NpcId == NestMinion).Level.Should().Be((short)MonsterStoneRules.FamilyLevel(FirstNest, FirstFamilySet));
    }

    private static string SeedDirectory([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Seed", "Data"));
}
