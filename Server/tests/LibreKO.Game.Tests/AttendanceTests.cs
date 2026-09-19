using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class AttendanceTests : GameTestBase
{
    private const int DailySlots = 25;
    private const int BonusSlots = 3;
    private const int BonusFirstSlot = 101;
    private const int RewardItemId = 900145000;
    private const ushort RewardCount = 5;

    private static Dictionary<int, AttendanceRewardData> BuildRewardTable()
    {
        var table = new Dictionary<int, AttendanceRewardData>();
        for (var slot = 1; slot <= DailySlots; slot++)
            table[slot] = new AttendanceRewardData { Slot = slot, ItemId = RewardItemId, ItemCount = (short)RewardCount };
        for (var index = 0; index < BonusSlots; index++)
        {
            var slot = BonusFirstSlot + index;
            table[slot] = new AttendanceRewardData { Slot = slot, ItemId = RewardItemId, ItemCount = 1 };
        }
        return table;
    }

    private static ServiceProvider CreateAttendanceProvider() =>
        CreateProvider(_ => { }, gameData =>
        {
            gameData.AttendanceRewardTable.Returns(BuildRewardTable());
            gameData.GetItem(RewardItemId).Returns(new ItemData
            {
                Num = RewardItemId,
                Kind = 255,
                Countable = 1,
                Weight = 1,
                Duration = 0,
            });
        });

    private static (UserSession Session, List<Packet> Sent) CreateSession(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 900, accountId: 910);
        session.Name = "Attendee";
        session.Stats.MaxWeight = 10000;
        return (session, sent);
    }

    private static Packet BoardRequest()
    {
        var packet = new Packet(GameOpcodes.GS_EVENT_BOARD);
        packet.WriteByte((byte)EventBoardSubOpcode.AttendanceBoard);
        packet.WriteUInt(0);
        packet.ResetOffset();
        return packet;
    }

    private static Packet ClaimRequest(int slot, byte state)
    {
        var packet = new Packet(GameOpcodes.GS_EVENT_BOARD);
        packet.WriteByte((byte)EventBoardSubOpcode.AttendanceClaim);
        packet.WriteUInt(0);
        packet.WriteInt(slot);
        packet.WriteByte((byte)(state - 2));
        packet.ResetOffset();
        return packet;
    }

    private static (int[] Slots, byte[] States) ReadBoard(Packet packet, byte expectedSub)
    {
        packet.ResetOffset();
        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_EVENT_BOARD);
        packet.ReadByte().Should().Be(expectedSub);
        packet.ReadUInt().Should().Be(0u);
        packet.ReadUInt().Should().Be(1u);
        packet.ReadUInt().Should().Be(0u);

        var slots = new int[DailySlots + BonusSlots];
        var states = new byte[slots.Length];

        packet.ReadUShort().Should().Be(DailySlots);
        for (var i = 0; i < DailySlots; i++)
        {
            slots[i] = packet.ReadInt();
            states[i] = packet.ReadByte();
        }

        packet.ReadUShort().Should().Be(BonusSlots);
        for (var i = 0; i < BonusSlots; i++)
        {
            slots[DailySlots + i] = packet.ReadInt();
            states[DailySlots + i] = packet.ReadByte();
        }

        packet.ReadUInt().Should().Be(0u);
        packet.RemainingBytes.Should().Be(0);
        return (slots, states);
    }

    [Fact]
    public void AttendanceRewardSeed_CoversTwentyFiveDailyAndThreeCumulativeSlots()
    {
        var rows = new AttendanceRewardSeed().GetSeedData().ToList();

        rows.Should().HaveCount(DailySlots + BonusSlots);
        rows.Select(row => row.Slot).Should().OnlyHaveUniqueItems();
        rows.Where(row => AttendanceRewardData.IsDailySlot(row.Slot)).Should().HaveCount(DailySlots);
        rows.Where(row => AttendanceRewardData.IsBonusSlot(row.Slot)).Should().HaveCount(BonusSlots);
        rows.Should().OnlyContain(row => row.ItemId > 0 && row.ItemCount >= 1);
    }

    [Fact]
    public void BonusThresholds_UnlockAtFifteenTwentyAndTwentyFive()
    {
        AttendanceRewardData.BonusThreshold(101).Should().Be(15);
        AttendanceRewardData.BonusThreshold(102).Should().Be(20);
        AttendanceRewardData.BonusThreshold(103).Should().Be(25);
    }

    [Fact]
    public async Task BoardRequest_ChecksInAndMarksOnlyTodaysSlotClaimable()
    {
        using var provider = CreateAttendanceProvider();
        var (session, sent) = CreateSession(provider);
        var coordinator = provider.GetRequiredService<IAttendancePacketCoordinator>();

        await coordinator.HandleAsync(session.Client, BoardRequest());

        session.AttendanceDays.Should().Be(1);
        session.AttendanceCheckedOn.Should().Be(DateTime.UtcNow.Date);

        var (slots, states) = ReadBoard(sent.Single(), (byte)EventBoardSubOpcode.AttendanceBoard);

        for (var i = 0; i < DailySlots; i++)
            slots[i].Should().Be(i + 1);
        for (var i = 0; i < BonusSlots; i++)
            slots[DailySlots + i].Should().Be(BonusFirstSlot + i);

        states[0].Should().Be(3);
        states.Skip(1).Take(DailySlots - 1).Should().AllBeEquivalentTo<byte>(5);
        states.Skip(DailySlots).Should().AllBeEquivalentTo<byte>(5);
    }

    [Fact]
    public async Task BoardRequest_SameDayDoesNotAdvanceTheStreak()
    {
        using var provider = CreateAttendanceProvider();
        var (session, _) = CreateSession(provider);
        var coordinator = provider.GetRequiredService<IAttendancePacketCoordinator>();

        await coordinator.HandleAsync(session.Client, BoardRequest());
        await coordinator.HandleAsync(session.Client, BoardRequest());

        session.AttendanceDays.Should().Be(1);
    }

    [Fact]
    public async Task Claim_GrantsTheTableRewardAndMarksTheSlotAcquired()
    {
        using var provider = CreateAttendanceProvider();
        var (session, sent) = CreateSession(provider);
        var coordinator = provider.GetRequiredService<IAttendancePacketCoordinator>();

        await coordinator.HandleAsync(session.Client, BoardRequest());
        sent.Clear();

        await coordinator.HandleAsync(session.Client, ClaimRequest(1, 3));

        var granted = session.Inventory[InventoryConstants.InventoryStart];
        granted.ItemId.Should().Be(RewardItemId);
        granted.Count.Should().Be(RewardCount);

        var (_, states) = ReadBoard(sent.First(), (byte)EventBoardSubOpcode.AttendanceClaim);
        states[0].Should().Be(1);
    }

    [Fact]
    public async Task Claim_WithAFullInventory_ReportsThatTheInventoryIsFull()
    {
        using var provider = CreateAttendanceProvider();
        var (session, sent) = CreateSession(provider);
        var coordinator = provider.GetRequiredService<IAttendancePacketCoordinator>();
        await coordinator.HandleAsync(session.Client, BoardRequest());
        for (var i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
        {
            session.Inventory[i].ItemId = RewardItemId + 1;
            session.Inventory[i].Count = 1;
        }
        sent.Clear();

        await coordinator.HandleAsync(session.Client, ClaimRequest(1, 3));

        var packet = sent.Single();
        packet.ResetOffset();
        packet.ReadByte().Should().Be((byte)EventBoardSubOpcode.AttendanceClaim);
        packet.ReadUInt();
        packet.ReadUInt().Should().Be(AttendancePacketCoordinator.ResultInventoryFull);
        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(RewardItemId + 1);
    }

    [Fact]
    public async Task Claim_IsRefusedTwiceForTheSameSlot()
    {
        using var provider = CreateAttendanceProvider();
        var (session, sent) = CreateSession(provider);
        var coordinator = provider.GetRequiredService<IAttendancePacketCoordinator>();

        await coordinator.HandleAsync(session.Client, BoardRequest());
        await coordinator.HandleAsync(session.Client, ClaimRequest(1, 3));
        sent.Clear();

        await coordinator.HandleAsync(session.Client, ClaimRequest(1, 3));

        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(RewardCount);

        var failure = sent.Single();
        failure.ResetOffset();
        failure.ReadByte().Should().Be((byte)EventBoardSubOpcode.AttendanceClaim);
        failure.ReadUInt().Should().Be(0u);
        failure.ReadUInt().Should().Be(0u);
        failure.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task Claim_IsRefusedForASlotTheStreakHasNotReached()
    {
        using var provider = CreateAttendanceProvider();
        var (session, sent) = CreateSession(provider);
        var coordinator = provider.GetRequiredService<IAttendancePacketCoordinator>();

        await coordinator.HandleAsync(session.Client, BoardRequest());
        sent.Clear();

        await coordinator.HandleAsync(session.Client, ClaimRequest(25, 3));

        session.Inventory[InventoryConstants.InventoryStart].IsEmpty.Should().BeTrue();
        sent.Single().GetLength().Should().Be(1 + 4 + 4);
    }

    [Fact]
    public async Task MissedDays_ExpireAndTheCumulativeSlotsUnlockOnTheThreshold()
    {
        using var provider = CreateAttendanceProvider();
        var (session, sent) = CreateSession(provider);
        var coordinator = provider.GetRequiredService<IAttendancePacketCoordinator>();

        session.AttendanceDays = 15;
        session.AttendanceCheckedOn = DateTime.UtcNow.Date;

        await coordinator.HandleAsync(session.Client, BoardRequest());
        var (_, states) = ReadBoard(sent.Single(), (byte)EventBoardSubOpcode.AttendanceBoard);

        states.Take(14).Should().AllBeEquivalentTo<byte>(2);
        states[14].Should().Be(3);
        states[15].Should().Be(5);
        states[DailySlots].Should().Be(3);
        states[DailySlots + 1].Should().Be(5);
        states[DailySlots + 2].Should().Be(5);
    }

    [Fact]
    public async Task CumulativeSlot_IsClaimableOnceUnlocked()
    {
        using var provider = CreateAttendanceProvider();
        var (session, sent) = CreateSession(provider);
        var coordinator = provider.GetRequiredService<IAttendancePacketCoordinator>();

        session.AttendanceDays = 25;
        session.AttendanceCheckedOn = DateTime.UtcNow.Date;

        await coordinator.HandleAsync(session.Client, ClaimRequest(103, 3));

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(RewardItemId);
        var (_, states) = ReadBoard(sent.First(), (byte)EventBoardSubOpcode.AttendanceClaim);
        states[DailySlots + 2].Should().Be(1);
    }

    [Fact]
    public async Task UnknownEventBoardSubOpcode_IsIgnored()
    {
        using var provider = CreateAttendanceProvider();
        var (session, sent) = CreateSession(provider);
        var coordinator = provider.GetRequiredService<IAttendancePacketCoordinator>();

        var packet = new Packet(GameOpcodes.GS_EVENT_BOARD);
        packet.WriteByte(6);
        packet.ResetOffset();

        await coordinator.HandleAsync(session.Client, packet);

        sent.Should().BeEmpty();
    }
}
