using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MonsterStoneTests
{
    private const byte Moradon = 21;
    private const byte FirstNest = 81;
    private const byte SecondNest = 82;
    private const byte ThirdNest = 83;
    private const byte Prison = 92;
    private const int NestBoss = 8718;
    private const int NestMinion = 8709;
    private const int Draws = 400;
    private const int FirstFamily = 1;
    private const int LastFamily = 13;
    private const int EntranceNpcs = 3;
    private const int PlaceholderLevel = 83;
    private const int PlaceholderHp = 6000;
    private const int NoticeSlackSeconds = 1;
    private const ushort RoomSeconds = 30 * 60;
    private const int NestScrap = 810098000;
    private const int DropSlots = 7;
    private static readonly int[] Gates = [7032, 7033, 7034];

    [Theory]
    [InlineData(20, new short[] { 1 })]
    [InlineData(29, new short[] { 1 })]
    [InlineData(30, new short[] { 2 })]
    [InlineData(36, new short[] { 3 })]
    [InlineData(41, new short[] { 4 })]
    [InlineData(47, new short[] { 4, 5 })]
    [InlineData(56, new short[] { 6, 7, 8 })]
    [InlineData(61, new short[] { 8, 9 })]
    [InlineData(67, new short[] { 9, 10 })]
    [InlineData(71, new short[] { 10, 11, 12 })]
    [InlineData(75, new short[] { 13 })]
    [InlineData(83, new short[] { 13 })]
    public void TheMonsterStonePicksAFamilyOfTheCharactersLevelBand(int level, short[] families)
    {
        var random = new Random(level);
        var picks = Enumerable.Range(0, Draws)
            .Select(_ => MonsterStoneRules.Pick(MonsterStoneRules.UniversalStone, level, random)!.Value)
            .ToList();

        picks.Select(p => p.Family).Distinct().Should().BeEquivalentTo(families);
        picks.Should().OnlyContain(p => p.ZoneId == NestOf(p.Family));
        picks.Should().OnlyContain(p => p.Set == MonsterStoneRules.FamilySetBase + p.Family);
    }

    [Fact]
    public void TheMonsterStoneTakesNobodyBelowLevelTwenty()
    {
        MonsterStoneRules.Pick(MonsterStoneRules.UniversalStone, MonsterStoneRules.MinimumLevel - 1, new Random(1))
            .Should().BeNull();
    }

    [Theory]
    [InlineData(MonsterStoneRules.FirstNestStone, FirstNest, new short[] { 1, 2, 3, 4 })]
    [InlineData(MonsterStoneRules.SecondNestStone, SecondNest, new short[] { 5, 6, 7, 8, 9 })]
    [InlineData(MonsterStoneRules.ThirdNestStone, ThirdNest, new short[] { 10, 11, 12, 13 })]
    public void ANestStoneKeepsToItsOwnNestAtAnyLevel(int stone, byte zone, short[] families)
    {
        var random = new Random(stone);
        var picks = Enumerable.Range(0, Draws)
            .Select(_ => MonsterStoneRules.Pick(stone, 1, random)!.Value)
            .ToList();

        picks.Should().OnlyContain(p => p.ZoneId == zone);
        picks.Select(p => p.Family).Distinct().Should().BeEquivalentTo(families);
    }

    [Fact]
    public void OtherItemsAreNoMonsterStone()
    {
        MonsterStoneRules.IsStone(NestMinion).Should().BeFalse();
        MonsterStoneRules.Pick(NestMinion, PlaceholderLevel, new Random(1)).Should().BeNull();
    }

    [Fact]
    public void EveryFamilyHasItsSpawnSetWithOneBossAndTheEntranceNpcs()
    {
        var positions = SeedPositions();
        var npcs = SeedNpcs();
        var monsterIds = npcs.Keys.Where(k => k.IsMonster).Select(k => k.Id).ToHashSet();
        var bossIds = npcs.Values.Where(n => n.IsMonster && n.IsBoss).Select(n => n.Id).ToHashSet();
        var npcIds = npcs.Keys.Where(k => !k.IsMonster).Select(k => k.Id).ToHashSet();

        for (short family = FirstFamily; family <= LastFamily; family++)
        {
            var set = (short)(MonsterStoneRules.FamilySetBase + family);
            var rows = positions.Where(p => p.Room == set).ToList();
            rows.Should().NotBeEmpty($"family {family} needs its population");
            rows.Select(p => p.ZoneId).Distinct().Should().ContainSingle().Which.Should().Be(NestOf(family));

            var monsters = rows.Where(p => p.ActType < NpcPosData.NpcSpawnActTypeBase).ToList();
            monsters.Should().OnlyContain(p => monsterIds.Contains(p.NpcId), $"family {family} names monsters the seed has");
            monsters.Where(p => bossIds.Contains(p.NpcId)).Should().ContainSingle($"family {family} ends with one boss");

            rows.Where(p => p.ActType >= NpcPosData.NpcSpawnActTypeBase)
                .Should().HaveCount(EntranceNpcs)
                .And.OnlyContain(p => npcIds.Contains(p.NpcId));
        }
    }

    [Fact]
    public void EveryNestBossDropsNestScrap()
    {
        var npcs = SeedNpcs();
        var bosses = SeedPositions()
            .Where(p => p.Room > MonsterStoneRules.FamilySetBase && p.ActType < NpcPosData.NpcSpawnActTypeBase)
            .Select(p => p.NpcId)
            .Where(id => npcs[(id, true)].IsBoss)
            .ToHashSet();
        using var drops = JsonDocument.Parse(File.ReadAllText(Path.Combine(SeedDirectory(), "NpcItems.json")));
        var dropsById = drops.RootElement.EnumerateArray()
            .Where(row => row.GetProperty("IsMonster").GetBoolean())
            .ToDictionary(row => row.GetProperty("Index").GetInt32(), row => Enumerable.Range(1, DropSlots)
                .Select(slot => row.GetProperty($"Item{slot}").GetInt32())
                .ToHashSet());

        bosses.Should().HaveCount(LastFamily);
        bosses.Should().OnlyContain(id => dropsById[id].Contains(NestScrap));
    }

    [Fact]
    public void NoNestMonsterKeepsThePlaceholderStats()
    {
        var npcs = SeedNpcs();
        var nestMonsters = SeedPositions()
            .Where(p => p.Room > MonsterStoneRules.FamilySetBase && p.ActType < NpcPosData.NpcSpawnActTypeBase)
            .Select(p => p.NpcId)
            .Except(Gates)
            .Distinct()
            .Select(id => npcs[(id, true)]);

        nestMonsters.Should().NotContain(n => n.Level == PlaceholderLevel && n.Hp == PlaceholderHp);
    }

    [Fact]
    public async Task UsingTheStoneSpendsOneAndSendsThePlayerAloneIntoTheirNest()
    {
        var rig = new Rig();
        var session = rig.Player(level: 25);

        await rig.Service.UseAsync(session, MonsterStoneRules.UniversalStone);

        await rig.Items.Received(1).TryConsumeItemAsync(session, MonsterStoneRules.UniversalStone, 1);
        await rig.Entry.Received(1).EnterAloneAsync(
            session, FirstNest, (short)(MonsterStoneRules.FamilySetBase + 1), 0f, 0f, true);
        var reply = rig.Replies(session).Should().ContainSingle().Subject;
        reply.Result.Should().Be(MonsterStoneResult.Entered);
        reply.ItemId.Should().Be(MonsterStoneRules.UniversalStone);

        var timer = rig.Sent[session].Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_BIFROST).Subject;
        timer.ResetOffset();
        timer.ReadByte().Should().Be((byte)TempleSubOpcode.MonsterSquad);
        timer.ReadUShort().Should().Be(RoomSeconds);
    }

    [Theory]
    [InlineData(Moradon, 400, 0, true, MonsterStoneResult.NotEnoughHealth)]
    [InlineData(Prison, 1000, 0, true, MonsterStoneResult.CannotEnterHere)]
    [InlineData(SecondNest, 1000, 0, true, MonsterStoneResult.CannotEnterHere)]
    [InlineData(Moradon, 1000, 5, true, MonsterStoneResult.Failed)]
    [InlineData(Moradon, 1000, 0, false, MonsterStoneResult.Failed)]
    public async Task ARefusedStoneIsKept(byte zone, short hp, ushort room, bool carriesStone, MonsterStoneResult expected)
    {
        var rig = new Rig();
        var session = rig.Player(level: 60, zone: zone, hp: hp, room: room);
        rig.Items.CanUseItem(session, MonsterStoneRules.UniversalStone).Returns(carriesStone);

        await rig.Service.UseAsync(session, MonsterStoneRules.UniversalStone);

        rig.Replies(session).Should().ContainSingle().Which.Result.Should().Be(expected);
        await rig.Items.DidNotReceiveWithAnyArgs().TryConsumeItemAsync(default!, default);
        await rig.Entry.DidNotReceiveWithAnyArgs().EnterAloneAsync(default!, default, default, default, default, default);
    }

    [Fact]
    public async Task AnotherNestItemIsRefusedSoTheWindowGetsAnAnswer()
    {
        var rig = new Rig();
        var session = rig.Player(level: 60);

        await rig.Service.UseAsync(session, NestMinion);

        rig.Replies(session).Should().ContainSingle().Which.Result.Should().Be(MonsterStoneResult.Failed);
        await rig.Items.DidNotReceiveWithAnyArgs().TryConsumeItemAsync(default!, default);
    }

    [Fact]
    public async Task AnUnderLevelCharacterIsToldWhyAndKeepsTheStone()
    {
        var rig = new Rig();
        var session = rig.Player(level: MonsterStoneRules.MinimumLevel - 1);

        await rig.Service.UseAsync(session, MonsterStoneRules.UniversalStone);

        rig.Replies(session).Should().ContainSingle().Which.Result.Should().Be(MonsterStoneResult.Failed);
        rig.Sent[session].Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_CHAT);
        await rig.Items.DidNotReceiveWithAnyArgs().TryConsumeItemAsync(default!, default);
    }

    [Fact]
    public async Task KillingTheNestBossEndsTheRoomAfterTheGrace()
    {
        var rig = new Rig();
        var session = rig.Player(level: 25);
        var room = rig.Rooms.Open(FirstNest, MonsterStoneRules.FamilySetBase + 1, TimeSpan.FromMinutes(30), endsOnBossKill: true);
        rig.Rooms.Join(room, session);

        await rig.Service.OnNpcKilledAsync(new NpcInstance { NpcId = NestMinion, ZoneId = FirstNest, Room = room.Id });
        room.Finishing.Should().BeFalse();

        await rig.Service.OnNpcKilledAsync(new NpcInstance { NpcId = NestBoss, ZoneId = FirstNest, Room = room.Id });

        room.Finishing.Should().BeTrue();
        room.ExpiresAt.Should().BeBefore(DateTime.UtcNow.AddSeconds(MonsterStoneRules.FinishGraceSeconds + NoticeSlackSeconds));
        var finish = rig.Sent[session].Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_EVENT).Subject;
        finish.ResetOffset();
        finish.ReadByte().Should().Be((byte)TempleSubOpcode.TempleEventFinish);
        finish.ReadUShort().Should().Be(EventPacketWriter.NestFinishEvent);
        finish.ReadByte().Should().Be(EventPacketWriter.NestFinishResult);
        finish.ReadUInt().Should().Be((uint)MonsterStoneRules.FinishGraceSeconds);
    }

    [Fact]
    public async Task AQuestRoomDoesNotEndWithItsBoss()
    {
        var rig = new Rig();
        var room = rig.Rooms.Open(FirstNest, 1, TimeSpan.FromMinutes(30));
        var expires = room.ExpiresAt;

        await rig.Service.OnNpcKilledAsync(new NpcInstance { NpcId = NestBoss, ZoneId = FirstNest, Room = room.Id });

        room.Finishing.Should().BeFalse();
        room.ExpiresAt.Should().Be(expires);
    }

    private static byte NestOf(short family) => family switch
    {
        <= 4 => FirstNest,
        <= 9 => SecondNest,
        _ => ThirdNest,
    };

    private static string SeedDirectory([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Seed", "Data"));

    private static List<NpcPosData> SeedPositions() =>
        Directory.GetFiles(SeedDirectory(), "NpcPositions.zone*.json")
            .SelectMany(path => JsonSerializer.Deserialize<List<NpcPosData>>(File.ReadAllText(path))!)
            .ToList();

    private static Dictionary<(int Id, bool IsMonster), NpcData> SeedNpcs() =>
        JsonSerializer.Deserialize<List<NpcData>>(File.ReadAllText(Path.Combine(SeedDirectory(), "Npcs.json")))!
            .ToDictionary(n => (n.Id, n.IsMonster));

    private sealed class Rig
    {
        public SessionManager Sessions { get; } = new();
        public IGameDataService GameData { get; } = Substitute.For<IGameDataService>();
        public IMagicItemUsageService Items { get; } = Substitute.For<IMagicItemUsageService>();
        public IInstanceEntryService Entry { get; } = Substitute.For<IInstanceEntryService>();
        public InstanceRoomRegistry Rooms { get; }
        public MonsterStoneService Service { get; }
        public Dictionary<UserSession, List<Packet>> Sent { get; } = [];

        public Rig()
        {
            Rooms = new InstanceRoomRegistry(Sessions, Substitute.For<ILogger<InstanceRoomRegistry>>());
            GameData.NpcPositions.Returns(Enumerable.Range(FirstFamily, LastFamily)
                .Select(family => new NpcPosData
                {
                    ZoneId = NestOf((short)family),
                    NpcId = NestMinion,
                    Room = (short)(MonsterStoneRules.FamilySetBase + family),
                })
                .ToList());
            GameData.GetNpc(NestBoss, true).Returns(new NpcData { Id = NestBoss, IsMonster = true, IsBoss = true });
            GameData.GetNpc(NestMinion, true).Returns(new NpcData { Id = NestMinion, IsMonster = true });
            Items.CanUseItem(Arg.Any<UserSession>(), MonsterStoneRules.UniversalStone).Returns(true);
            Items.TryConsumeItemAsync(Arg.Any<UserSession>(), Arg.Any<int>(), Arg.Any<int>()).Returns(true);
            Service = new MonsterStoneService(
                Sessions, GameData, Items, Entry, Rooms, Substitute.For<ILogger<MonsterStoneService>>());
        }

        public UserSession Player(byte level, byte zone = Moradon, short hp = 1000, ushort room = 0)
        {
            var client = Substitute.For<IClient>();
            client.Id.Returns(Guid.NewGuid());
            var session = Sessions.CreateSession(client, characterId: 9000 + Sent.Count, accountId: 9500 + Sent.Count);
            var packets = new List<Packet>();
            Sent[session] = packets;
            client.SendPacket(Arg.Do<Packet>(packets.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            session.Level = level;
            session.ZoneId = zone;
            session.Hp = hp;
            session.MaxHp = 1000;
            session.Room = room;
            session.Nation = AccountNation.Karus;
            return session;
        }

        public List<(MonsterStoneResult Result, int ItemId)> Replies(UserSession session) =>
            Sent[session]
                .Where(p => p.GetOpcode() == (byte)GameOpcodes.GS_EVENT)
                .Select(p =>
                {
                    p.ResetOffset();
                    p.ReadByte().Should().Be((byte)TempleSubOpcode.MonsterStone);
                    var result = (MonsterStoneResult)p.ReadByte();
                    return (result, p.RemainingBytes >= sizeof(int) ? p.ReadInt() : 0);
                })
                .ToList();
    }
}
