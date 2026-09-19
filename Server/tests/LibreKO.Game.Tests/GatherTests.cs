using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class GatherTests : GameTestBase
{
    private const int Mattock = 389132000;
    private const int GoldenMattock = 389135000;
    private const int FishingRod = 191346000;
    private const int GoldenFishingRod = 191347000;
    private const int Rainworm = 508226000;
    private const int ExpReward = 900001000;
    private const int GemReward = 389205000;
    private const int MysteriousOre = 399210000;
    private const int MysteriousGoldOre = 399200000;

    private static IEnumerable<MiningFishingItemData> PeacetimeMiningPool(
        IEnumerable<MiningFishingItemData> rows, GatherTool tool)
        => rows.Where(r => r.Type == GatherType.Mining
                        && r.UseItemType == tool
                        && r.WarStatus == GatherWarStatus.Peace);

    private const byte KindPickaxe = 61;
    private const byte KindFishingRod = 63;

    private const byte SubMiningStart = 1;
    private const byte SubMiningAttempt = 2;
    private const byte SubMiningStop = 3;
    private const byte SubBetting = 5;
    private const byte SubFishingStart = 6;
    private const byte SubFishingAttempt = 7;

    private const ushort ResultSuccess = 1;
    private const ushort ResultAlready = 2;
    private const ushort ResultNotArea = 3;
    private const ushort ResultNoTool = 5;
    private const ushort ResultNoEarthworm = 7;

    private const float MoradonMineX = 630f;
    private const float MoradonMineZ = 370f;
    private const float ElmoradPondX = 900f;
    private const float ElmoradPondZ = 1100f;

    private static void ConfigureItems(IGameDataService gameData)
    {
        gameData.GetItem(Mattock).Returns(new ItemData { Num = Mattock, Kind = KindPickaxe, Duration = 4000, Weight = 60 });
        gameData.GetItem(GoldenMattock).Returns(new ItemData { Num = GoldenMattock, Kind = KindPickaxe, Duration = 4000, Weight = 60 });
        gameData.GetItem(FishingRod).Returns(new ItemData { Num = FishingRod, Kind = KindFishingRod, Duration = 4000, Weight = 60 });
        gameData.GetItem(GoldenFishingRod).Returns(new ItemData { Num = GoldenFishingRod, Kind = KindFishingRod, Duration = 4000, Weight = 60 });
        gameData.GetItem(Rainworm).Returns(new ItemData { Num = Rainworm, Kind = 95, Duration = 1, Weight = 1, Countable = 1 });
        gameData.GetItem(GemReward).Returns(new ItemData { Num = GemReward, Kind = 255, Duration = 1, Weight = 1, Countable = 1 });
    }

    private static void ConfigurePool(IGameDataService gameData, params MiningFishingItemData[] rows)
    {
        gameData.MiningFishingItemsByPool.Returns(rows.ToLookup(x => (x.Type, x.UseItemType, x.WarStatus)));
    }

    private static MiningFishingItemData Row(GatherType type, GatherTool tool, int itemId, int rate) => new()
    {
        Index = itemId,
        Type = type,
        WarStatus = GatherWarStatus.Peace,
        UseItemType = tool,
        GiveItemNum = itemId,
        GiveItemCount = 1,
        SuccessRate = rate,
    };

    private static (UserSession Session, List<Packet> Sent) CreateGatherer(
        ServiceProvider provider, byte zoneId, float x, float z, int toolId, int toolSlot = InventoryConstants.RightHand)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 9100 + toolSlot, accountId: 9200);
        session.ZoneId = zoneId;
        session.X = x;
        session.Z = z;
        session.Hp = 100;
        session.Level = 40;
        session.Nation = AccountNation.Karus;
        session.Stats.MaxWeight = 10000;
        session.Inventory[toolSlot].ItemId = toolId;
        session.Inventory[toolSlot].Durability = 4000;
        session.Inventory[toolSlot].Count = 1;
        return (session, sent);
    }

    private static Packet Request(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_MINING);
        packet.WriteByte(sub);
        return packet;
    }

    private static (byte Sub, ushort Code) ReadHeader(Packet packet)
    {
        packet.ResetOffset();
        return (packet.ReadByte(), packet.ReadUShort());
    }

    [Fact]
    public async Task MiningStart_OutsideMiningArea_IsRejected()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, 100f, 100f, Mattock);

        await provider.GetRequiredService<IMiningPacketCoordinator>()
            .HandleAsync(session.Client, Request(SubMiningStart));

        session.IsMining.Should().BeFalse();
        sent.Should().ContainSingle();
        ReadHeader(sent[0]).Should().Be((SubMiningStart, ResultNotArea));
    }

    [Fact]
    public async Task MiningStart_InMiningAreaWithPickaxe_Succeeds()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);

        await provider.GetRequiredService<IMiningPacketCoordinator>()
            .HandleAsync(session.Client, Request(SubMiningStart));

        session.IsMining.Should().BeTrue();
        sent.Should().ContainSingle();
        var start = sent[0];
        start.ResetOffset();
        start.ReadByte().Should().Be(SubMiningStart);
        start.ReadUShort().Should().Be(ResultSuccess);
        start.ReadInt().Should().Be(session.CharacterId);
        start.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task MiningStart_AcceptsPickaxeInLeftHand()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, _) = CreateGatherer(
            provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock, InventoryConstants.LeftHand);

        await provider.GetRequiredService<IMiningPacketCoordinator>()
            .HandleAsync(session.Client, Request(SubMiningStart));

        session.IsMining.Should().BeTrue();
    }

    [Fact]
    public async Task MiningStart_WithoutPickaxe_IsRejected()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, FishingRod);

        await provider.GetRequiredService<IMiningPacketCoordinator>()
            .HandleAsync(session.Client, Request(SubMiningStart));

        session.IsMining.Should().BeFalse();
        ReadHeader(sent[0]).Should().Be((SubMiningStart, ResultNoTool));
    }

    [Fact]
    public async Task MiningStart_WhenAlreadyMining_ReportsAlready()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();

        await coordinator.HandleAsync(session.Client, Request(SubMiningStart));
        sent.Clear();
        await coordinator.HandleAsync(session.Client, Request(SubMiningStart));

        ReadHeader(sent[0]).Should().Be((SubMiningStart, ResultAlready));
    }

    [Fact]
    public async Task MiningAttempt_GrantsTableItemAndWearsBothWeaponSlots()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            ConfigureItems(gameData);
            ConfigurePool(gameData, Row(GatherType.Mining, GatherTool.Plain, GemReward, 100));
        });

        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        session.Inventory[InventoryConstants.LeftHand].ItemId = Mattock;
        session.Inventory[InventoryConstants.LeftHand].Durability = 4000;
        session.Inventory[InventoryConstants.LeftHand].Count = 1;

        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubMiningStart));
        session.LastGatherAttempt = DateTime.UtcNow.AddSeconds(-10);
        sent.Clear();

        await coordinator.HandleAsync(session.Client, Request(SubMiningAttempt));

        session.Inventory[InventoryConstants.RightHand].Durability.Should().BeInRange(3995, 3998);
        session.Inventory[InventoryConstants.LeftHand].Durability.Should()
            .Be(session.Inventory[InventoryConstants.RightHand].Durability);

        var bag = Enumerable
            .Range(InventoryConstants.InventoryStart, InventoryConstants.HaveMax)
            .Select(i => session.Inventory[i])
            .Where(slot => slot.ItemId == GemReward)
            .ToArray();
        bag.Should().ContainSingle();
        bag[0].Count.Should().Be(1);

        var result = sent.Last();
        result.ResetOffset();
        result.ReadByte().Should().Be(SubMiningAttempt);
        result.ReadUShort().Should().Be(ResultSuccess);
        result.ReadInt().Should().Be(session.CharacterId);
        result.ReadUShort().Should().Be(13081);
    }

    [Fact]
    public async Task MiningAttempt_ExpRow_AwardsExperienceAndExpEffect()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            ConfigureItems(gameData);
            ConfigurePool(gameData, Row(GatherType.Mining, GatherTool.Plain, ExpReward, 100));
        });

        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubMiningStart));
        session.LastGatherAttempt = DateTime.UtcNow.AddSeconds(-10);
        sent.Clear();

        await coordinator.HandleAsync(session.Client, Request(SubMiningAttempt));

        var result = sent.Last();
        result.ResetOffset();
        result.ReadByte().Should().Be(SubMiningAttempt);
        result.ReadUShort().Should().Be(ResultSuccess);
        result.ReadInt().Should().Be(session.CharacterId);
        result.ReadUShort().Should().Be(13082);
    }

    [Fact]
    public async Task MiningAttempt_GoldenMattock_UsesGoldenPool()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            ConfigureItems(gameData);
            ConfigurePool(gameData,
                Row(GatherType.Mining, GatherTool.Plain, ExpReward, 100),
                Row(GatherType.Mining, GatherTool.Golden, GemReward, 100));
        });

        var (session, _) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, GoldenMattock);
        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubMiningStart));
        session.LastGatherAttempt = DateTime.UtcNow.AddSeconds(-10);

        await coordinator.HandleAsync(session.Client, Request(SubMiningAttempt));

        Enumerable
            .Range(InventoryConstants.InventoryStart, InventoryConstants.HaveMax)
            .Select(i => session.Inventory[i])
            .Should().Contain(slot => slot.ItemId == GemReward);
    }

    [Fact]
    public async Task MiningAttempt_WalkingOutOfTheArea_StopsMining()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            ConfigureItems(gameData);
            ConfigurePool(gameData, Row(GatherType.Mining, GatherTool.Plain, ExpReward, 100));
        });

        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubMiningStart));
        session.LastGatherAttempt = DateTime.UtcNow.AddSeconds(-10);
        session.X = 100f;
        sent.Clear();

        await coordinator.HandleAsync(session.Client, Request(SubMiningAttempt));

        session.IsMining.Should().BeFalse();
        ReadHeader(sent[0]).Should().Be((SubMiningAttempt, ResultNotArea));
        ReadHeader(sent.Last()).Should().Be((SubMiningStop, ResultAlready));
    }

    [Fact]
    public async Task MiningStart_OutsideAreaAndWithoutPickaxe_ReportsTheToolFirst()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, 100f, 100f, FishingRod);

        await provider.GetRequiredService<IMiningPacketCoordinator>()
            .HandleAsync(session.Client, Request(SubMiningStart));

        ReadHeader(sent[0]).Should().Be((SubMiningStart, ResultNoTool));
    }

    [Fact]
    public async Task MiningAttempt_WithNoRoomForTheReward_SpendsNothing()
    {
        const int filler = 700900;

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            ConfigureItems(gameData);
            gameData.GetItem(filler).Returns(new ItemData { Num = filler, Kind = 255, Duration = 1, Weight = 1 });
            ConfigurePool(gameData, Row(GatherType.Mining, GatherTool.Plain, GemReward, 100));
        });

        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        for (var i = 0; i < InventoryConstants.HaveMax; i++)
        {
            var slot = session.Inventory[InventoryConstants.InventoryStart + i];
            slot.ItemId = filler;
            slot.Count = 1;
            slot.Durability = 1;
        }

        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubMiningStart));
        session.LastGatherAttempt = DateTime.UtcNow.AddSeconds(-10);
        sent.Clear();

        await coordinator.HandleAsync(session.Client, Request(SubMiningAttempt));

        session.IsMining.Should().BeTrue();
        session.Inventory[InventoryConstants.RightHand].Durability.Should().Be(4000);
        sent.Should().ContainSingle();
        ReadHeader(sent[0]).Should().Be((SubMiningAttempt, 6));
    }

    [Fact]
    public async Task FishingAttempt_WithNoRoomForTheReward_KeepsBait()
    {
        const int filler = 700901;

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            ConfigureItems(gameData);
            gameData.GetItem(filler).Returns(new ItemData { Num = filler, Kind = 255, Duration = 1, Weight = 1 });
            ConfigurePool(gameData, Row(GatherType.Fishing, GatherTool.Plain, GemReward, 100));
        });

        var (session, _) = CreateGatherer(provider, BattleZoneManager.ZONE_ELMORAD, ElmoradPondX, ElmoradPondZ, FishingRod);
        session.Inventory[InventoryConstants.InventoryStart].ItemId = Rainworm;
        session.Inventory[InventoryConstants.InventoryStart].Count = 3;
        for (var i = 1; i < InventoryConstants.HaveMax; i++)
        {
            var slot = session.Inventory[InventoryConstants.InventoryStart + i];
            slot.ItemId = filler;
            slot.Count = 1;
            slot.Durability = 1;
        }

        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubFishingStart));
        session.LastGatherAttempt = DateTime.UtcNow.AddSeconds(-10);

        await coordinator.HandleAsync(session.Client, Request(SubFishingAttempt));

        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(3);
        session.Inventory[InventoryConstants.RightHand].Durability.Should().Be(4000);
    }

    [Fact]
    public async Task FishingStart_WithoutBait_IsRejected()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_ELMORAD, ElmoradPondX, ElmoradPondZ, FishingRod);

        await provider.GetRequiredService<IMiningPacketCoordinator>()
            .HandleAsync(session.Client, Request(SubFishingStart));

        session.IsFishing.Should().BeFalse();
        ReadHeader(sent[0]).Should().Be((SubFishingStart, ResultNoEarthworm));
    }

    [Fact]
    public async Task FishingStart_GoldenRodNeedsNoBait()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, _) = CreateGatherer(provider, BattleZoneManager.ZONE_ELMORAD, ElmoradPondX, ElmoradPondZ, GoldenFishingRod);

        await provider.GetRequiredService<IMiningPacketCoordinator>()
            .HandleAsync(session.Client, Request(SubFishingStart));

        session.IsFishing.Should().BeTrue();
    }

    [Fact]
    public async Task FishingAttempt_GrantsItemConsumesBaitAndWearsRod()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            ConfigureItems(gameData);
            ConfigurePool(gameData, Row(GatherType.Fishing, GatherTool.Plain, GemReward, 100));
        });

        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_ELMORAD, ElmoradPondX, ElmoradPondZ, FishingRod);
        session.Inventory[InventoryConstants.InventoryStart].ItemId = Rainworm;
        session.Inventory[InventoryConstants.InventoryStart].Count = 3;

        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubFishingStart));
        session.LastGatherAttempt = DateTime.UtcNow.AddSeconds(-10);
        sent.Clear();

        await coordinator.HandleAsync(session.Client, Request(SubFishingAttempt));

        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(2);
        session.Inventory[InventoryConstants.RightHand].Durability.Should().BeInRange(3995, 3998);

        var result = sent.Last();
        result.ResetOffset();
        result.ReadByte().Should().Be(SubFishingAttempt);
        result.ReadUShort().Should().Be(ResultSuccess);
        result.ReadInt().Should().Be(session.CharacterId);
        result.ReadUShort().Should().Be(30730);
    }

    [Fact]
    public async Task FishingAttempt_GoldenRodKeepsBait()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            ConfigureItems(gameData);
            ConfigurePool(gameData, Row(GatherType.Fishing, GatherTool.Golden, GemReward, 100));
        });

        var (session, _) = CreateGatherer(provider, BattleZoneManager.ZONE_ELMORAD, ElmoradPondX, ElmoradPondZ, GoldenFishingRod);
        session.Inventory[InventoryConstants.InventoryStart].ItemId = Rainworm;
        session.Inventory[InventoryConstants.InventoryStart].Count = 3;

        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubFishingStart));
        session.LastGatherAttempt = DateTime.UtcNow.AddSeconds(-10);

        await coordinator.HandleAsync(session.Client, Request(SubFishingAttempt));

        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(3);
    }

    [Fact]
    public async Task GatherAttempt_BeforeTheDelayElapses_ReportsPreparing()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            ConfigureItems(gameData);
            ConfigurePool(gameData, Row(GatherType.Mining, GatherTool.Plain, ExpReward, 100));
        });

        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubMiningStart));
        sent.Clear();

        await coordinator.HandleAsync(session.Client, Request(SubMiningAttempt));

        session.IsMining.Should().BeTrue();
        sent.Should().ContainSingle();
        ReadHeader(sent[0]).Should().Be((SubMiningAttempt, 4));
    }

    [Fact]
    public async Task StopGathering_SendsRegionStopThenSelfAck()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();
        await coordinator.HandleAsync(session.Client, Request(SubMiningStart));
        sent.Clear();

        await coordinator.HandleAsync(session.Client, Request(SubMiningStop));

        session.IsMining.Should().BeFalse();
        sent.Should().HaveCount(2);

        var broadcast = sent[0];
        broadcast.ResetOffset();
        broadcast.ReadByte().Should().Be(SubMiningStop);
        broadcast.ReadUShort().Should().Be(ResultSuccess);
        broadcast.ReadInt().Should().Be(session.CharacterId);

        var ack = sent[1];
        ack.ResetOffset();
        ack.ReadByte().Should().Be(SubMiningStop);
        ack.ReadUShort().Should().Be(ResultAlready);
        ack.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task StopGatheringAsync_ClearsBothFlags()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, _) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        session.IsMining = true;
        session.IsFishing = true;

        await provider.GetRequiredService<IMiningPacketCoordinator>().StopGatheringAsync(session);

        session.IsGathering.Should().BeFalse();
    }

    [Fact]
    public async Task BettingGame_WithoutTheStake_ReportsNoCoins()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        session.Money = 100;

        await provider.GetRequiredService<IMiningPacketCoordinator>()
            .HandleAsync(session.Client, Request(SubBetting));

        session.Money.Should().Be(100);
        var response = sent.Last();
        response.ResetOffset();
        response.ReadByte().Should().Be(SubBetting);
        response.ReadUShort().Should().Be(4);
        response.ReadInt().Should().Be(session.CharacterId);
        response.ReadUShort().Should().Be(0);
        response.ReadByte().Should().Be(0);
        response.ReadByte().Should().Be(0);
    }

    [Fact]
    public async Task BettingGame_PaysTenThousandOnAWinAndKeepsTheStakeOtherwise()
    {
        using var provider = CreateProvider(_ => { }, ConfigureItems);
        var (session, sent) = CreateGatherer(provider, BattleZoneManager.ZONE_MORADON, MoradonMineX, MoradonMineZ, Mattock);
        var coordinator = provider.GetRequiredService<IMiningPacketCoordinator>();

        for (var round = 0; round < 40; round++)
        {
            session.Money = 5000;
            sent.Clear();
            await coordinator.HandleAsync(session.Client, Request(SubBetting));

            var response = sent.Last();
            response.ResetOffset();
            response.ReadByte();
            var code = response.ReadUShort();
            response.ReadInt();
            response.ReadUShort();
            var playerRoll = response.ReadByte();
            var npcRoll = response.ReadByte();

            playerRoll.Should().BeInRange(1, 5);
            npcRoll.Should().BeInRange(1, 5);

            switch (code)
            {
                case 1:
                    playerRoll.Should().BeGreaterThan(npcRoll);
                    session.Money.Should().Be(10000);
                    break;
                case 2:
                    playerRoll.Should().Be(npcRoll);
                    session.Money.Should().Be(0);
                    break;
                case 3:
                    playerRoll.Should().BeLessThan(npcRoll);
                    session.Money.Should().Be(0);
                    break;
                default:
                    throw new Xunit.Sdk.XunitException($"unexpected betting result {code}");
            }
        }
    }

    [Fact]
    public void MiningFishingItemSeed_CoversEveryPoolTheHandlerCanSelect()
    {
        var rows = new MiningFishingItemSeed().GetSeedData().ToList();

        rows.Should().HaveCount(241);
        rows.Select(row => row.Index).Should().OnlyHaveUniqueItems();
        rows.Should().OnlyContain(row => row.SuccessRate > 0 && row.GiveItemNum > 0);

        var ours = rows.Where(row => row.Index >= 900000).ToArray();
        ours.Should().HaveCount(4, "only the two ore drops the exchange consumes are ours");
        ours.Should().OnlyContain(row => row.GiveItemNum == MysteriousOre
                                      || row.GiveItemNum == MysteriousGoldOre);
        foreach (var tool in new[] { GatherTool.Plain, GatherTool.Golden })
        {
            PeacetimeMiningPool(rows, tool).Should().Contain(row => row.GiveItemNum == MysteriousOre);
            PeacetimeMiningPool(rows, tool).Should().Contain(row => row.GiveItemNum == MysteriousGoldOre);
        }

        var pools = rows.ToLookup(row => (row.Type, row.UseItemType, row.WarStatus));
        foreach (var type in new[] { GatherType.Mining, GatherType.Fishing })
        {
            foreach (var tool in new[] { GatherTool.Plain, GatherTool.Golden })
            {
                foreach (var war in new[] { GatherWarStatus.Peace, GatherWarStatus.Loser, GatherWarStatus.Winner })
                {
                    pools[(type, tool, war)].Should().NotBeEmpty(
                        $"{type}/{tool}/{war} is reachable and must have rewards");
                }
            }
        }
    }
}
