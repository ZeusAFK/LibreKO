using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class SlaveMerchantTests : GameTestBase
{
    private const int Scroll = 379080000;
    private const int Sword = 110110001;
    private const int OwnerId = 2001;
    private const int BuyerId = 2002;
    private const int FirstCrowdBuyerId = 2010;
    private const int CrowdSize = 6;
    private const int AccountOffset = 1000;
    private const int ScrollPrice = 2000;
    private const int StackSlot = InventoryConstants.InventoryStart;
    private const int CertificateBagPosition = 3;
    private const byte SweptTo = (byte)ZoneId.ElMoradCamp1;
    private const byte MonsterType = 7;
    private const int BuyerPurse = 1_000_000;
    private const int LinkedSerial = 77;
    private const int AltId = 2003;
    private const int InstanceRoom = 4;
    private const long RentalEnds = 4_102_444_800;
    private const ushort Kept = 3;
    private const ushort Owned = 4;
    private const ushort PickedUp = 3;

    [Fact]
    public async Task ACertificateAddsItsHoursAndIsUsedUp()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        GiveCertificate(owner, SlaveMerchantService.OneDayCertificate);

        await Service(provider).RedeemAsync(owner, CertificateBagPosition, SlaveMerchantService.OneDayCertificate);

        owner.Inventory[InventoryConstants.InventoryStart + CertificateBagPosition].IsEmpty.Should().BeTrue();
        var reply = Last(sent, SlaveMerchantSub.Redeem);
        reply.ReadByte().Should().Be((byte)SlaveMerchantRedeemResult.Added);
        reply.ReadUShort().Should().Be(SlaveMerchantService.OneDayHours);
        (await HoursOf(provider, OwnerId)).Should().Be(SlaveMerchantService.OneDayHours);
        (await StoredItems(provider, OwnerId)).Should().Equal(owner.SerializeItems());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ABusyOwnerRedeemsNoCertificate(bool atAnOpenStall)
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        GiveCertificate(owner, SlaveMerchantService.OneDayCertificate);
        if (atAnOpenStall)
            owner.Trade.MerchantState = MerchantMode.Selling;
        else
            owner.Trade.ExchangeUser = BuyerId;

        await provider.GetRequiredService<ISlaveMerchantPacketCoordinator>().HandleAsync(owner.Client, RedeemRequest());

        owner.Inventory[InventoryConstants.InventoryStart + CertificateBagPosition].ItemId.Should().Be(SlaveMerchantService.OneDayCertificate);
        (await HoursOf(provider, OwnerId)).Should().Be(0);
        Last(sent, SlaveMerchantSub.Redeem).ReadByte().Should().Be((byte)SlaveMerchantRedeemResult.Failed, "the client is told the certificate was not used");
    }

    [Fact]
    public async Task APlayerSettingUpAStallRedeemsACertificate()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        GiveCertificate(owner, SlaveMerchantService.OneDayCertificate);
        owner.Trade.IsSellingMerchantPreparing = true;

        await provider.GetRequiredService<ISlaveMerchantPacketCoordinator>().HandleAsync(owner.Client, RedeemRequest());

        (await HoursOf(provider, OwnerId)).Should().Be(SlaveMerchantService.OneDayHours, "the stall panel itself asks for a certificate");
        Last(sent, SlaveMerchantSub.Redeem).ReadByte().Should().Be((byte)SlaveMerchantRedeemResult.Added);
    }

    private static Packet RedeemRequest()
    {
        var redeem = new Packet(GameOpcodes.GS_SLAVE_MERCHANT);
        redeem.WriteByte((byte)SlaveMerchantSub.Redeem);
        redeem.WriteByte(CertificateBagPosition);
        redeem.WriteInt(SlaveMerchantService.OneDayCertificate);
        redeem.ResetOffset();
        return redeem;
    }

    [Fact]
    public async Task ACertificateThatIsNotInTheBagSlotAddsNothing()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");

        await Service(provider).RedeemAsync(owner, CertificateBagPosition, SlaveMerchantService.ThirtyDayCertificate);

        Last(sent, SlaveMerchantSub.Redeem).ReadByte().Should().Be((byte)SlaveMerchantRedeemResult.Failed);
        (await HoursOf(provider, OwnerId)).Should().Be(0);
    }

    [Fact]
    public async Task ACertificateWhoseSaveFailsStaysInTheBagAndAddsNoHours()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        GiveCertificate(owner, SlaveMerchantService.ThirtyDayCertificate);
        faults.Fails = _ => true;

        await Service(provider).RedeemAsync(owner, CertificateBagPosition, SlaveMerchantService.ThirtyDayCertificate);

        owner.Inventory[InventoryConstants.InventoryStart + CertificateBagPosition].ItemId.Should().Be(SlaveMerchantService.ThirtyDayCertificate);
        owner.Inventory[InventoryConstants.InventoryStart + CertificateBagPosition].Count.Should().Be(1);
        Last(sent, SlaveMerchantSub.Redeem).ReadByte().Should().Be((byte)SlaveMerchantRedeemResult.Failed);
        faults.Fails = _ => false;
        (await HoursOf(provider, OwnerId)).Should().Be(0);
    }

    [Fact]
    public async Task AStallHandedToASlaveTakesTheItemsAndAnHourAndFreesTheOwner()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "TraderHero");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 10);

        await Insert(provider, owner, toSlave: true);

        owner.Inventory[StackSlot].Count.Should().Be(6);
        owner.Trade.IsMerchanting.Should().BeFalse();
        Last(sent, SlaveMerchantSub.Deploy).ReadByte().Should().Be((byte)SlaveMerchantDeployResult.Deployed);
        var slave = Slave(provider);
        slave.Name.Should().Be("[S]TraderHero");
        slave.Trade.IsMerchanting.Should().BeTrue();
        slave.Trade.MerchantItems[0].Count.Should().Be(4);
        slave.Trade.MerchantItems[0].Price.Should().Be(ScrollPrice);
        (await HoursOf(provider, OwnerId)).Should().Be(2);
        (await Records(provider)).Single().IsActive.Should().BeTrue();
        (await StoredItems(provider, OwnerId)).Should().Equal(owner.SerializeItems());
    }

    [Fact]
    public async Task ADeployWhoseSaveFailsLeavesTheBagTheHoursAndTheWorldAsTheyWere()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 10);
        faults.Fails = _ => true;

        await Insert(provider, owner, toSlave: true);

        faults.Fails = _ => false;
        Last(sent, SlaveMerchantSub.Deploy).ReadByte().Should().Be((byte)SlaveMerchantDeployResult.Failed);
        owner.Inventory[StackSlot].ItemId.Should().Be(Scroll);
        owner.Inventory[StackSlot].Count.Should().Be(10);
        Slaves(provider).Should().BeEmpty();
        (await Records(provider)).Should().BeEmpty();
        (await HoursOf(provider, OwnerId)).Should().Be(3);
    }

    [Fact]
    public async Task WithoutSlaveMerchantTimeTheItemsStayInTheBag()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        Stage(owner, Scroll, StackSlot, count: 4, stock: 10);

        await Insert(provider, owner, toSlave: true);

        Last(sent, SlaveMerchantSub.Deploy).ReadByte().Should().Be((byte)SlaveMerchantDeployResult.NoTime);
        owner.Inventory[StackSlot].Count.Should().Be(10);
        Slaves(provider).Should().BeEmpty();
        (await Records(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task ASecondSlaveWaitsUntilTheFirstIsCollected()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 10);
        await Insert(provider, owner, toSlave: true);

        Stage(owner, Scroll, StackSlot, count: 2, stock: 6);
        await Insert(provider, owner, toSlave: true);

        Last(sent, SlaveMerchantSub.Deploy).ReadByte().Should().Be((byte)SlaveMerchantDeployResult.AlreadyOpen);
        owner.Inventory[StackSlot].Count.Should().Be(6);
        Slaves(provider).Should().ContainSingle();
    }

    [Fact]
    public async Task AnItemThatLeftItsBagSlotIsNotHandedOver()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 10);
        owner.Inventory[StackSlot].Count = 3;

        await Insert(provider, owner, toSlave: true);

        Last(sent, SlaveMerchantSub.Deploy).ReadByte().Should().Be((byte)SlaveMerchantDeployResult.ItemsMoved);
        owner.Inventory[StackSlot].Count.Should().Be(3);
        Slaves(provider).Should().BeEmpty();
        (await HoursOf(provider, OwnerId)).Should().Be(5);
    }

    [Fact]
    public async Task ZoneSweepsDeathsAndStallClosesLeaveASlaveSelling()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Sweeper");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 10);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);
        var zone = slave.ZoneId;

        await provider.GetRequiredService<IZoneTransitionService>().ChangeZoneAsync(slave, SweptTo, 0f, 0f);
        await provider.GetRequiredService<INpcAiDeathService>().HandlePlayerKilledByNpcAsync(slave, new NpcInstance { NpcType = MonsterType });
        await provider.GetRequiredService<ICombatLifecycleService>().HandlePlayerDeathAsync(slave, killer: null);
        await provider.GetRequiredService<IMerchantLifecycleService>().CloseAsync(slave, MerchantInOut.StallClosed);

        slave.ZoneId.Should().Be(zone, "an event sweep would close the stall and lose what it holds");
        slave.IsWarping.Should().BeFalse();
        slave.KillerNpcType.Should().Be(0, "a monster cannot kill a slave");
        slave.Trade.IsMerchanting.Should().BeTrue();
        slave.Trade.MerchantItems[0].Count.Should().Be(4);
        slave.Deaths.Should().Be(0);
    }

    [Fact]
    public async Task AHandOverNeedsAStallBeingSetUp()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Rusher");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 10);
        owner.Trade.MerchantState = MerchantMode.Selling;

        await Insert(provider, owner, toSlave: true);

        Slaves(provider).Should().BeEmpty("an open stall's items are already on sale");
        owner.Inventory[StackSlot].Count.Should().Be(10);
    }

    [Fact]
    public async Task ABusyOwnerCollectsNothing()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Juggler");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 10);
        await Insert(provider, owner, toSlave: true);
        owner.Trade.ExchangeUser = BuyerId;
        var collect = new Packet(GameOpcodes.GS_SLAVE_MERCHANT);
        collect.WriteByte((byte)SlaveMerchantSub.Collect);
        collect.ResetOffset();

        await provider.GetRequiredService<ISlaveMerchantPacketCoordinator>().HandleAsync(owner.Client, collect);

        Slaves(provider).Should().ContainSingle("a trade or a stall counts on the bag staying as it is");
        Last(sent, SlaveMerchantSub.Collect).ReadByte().Should().Be((byte)SlaveMerchantCollectResult.Failed);
    }

    [Fact]
    public async Task SalesPayTheSlaveAndCollectingPaysTheOwnerAndBringsTheRestBack()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        owner.Money = 10_000;
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 5, stock: 5);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);

        await Buy(provider, slave, count: 2);

        var sold = Last(sent, SlaveMerchantSub.Sold);
        sold.ReadInt().Should().Be(Scroll);
        sold.ReadUShort().Should().Be(2);
        sold.ReadUInt().Should().Be(2 * ScrollPrice);
        (await Records(provider)).Single().EarnedNoah.Should().Be(2 * ScrollPrice);

        await Service(provider).CollectAsync(owner);

        owner.Money.Should().Be(10_000 + 2 * ScrollPrice);
        owner.Inventory[StackSlot].ItemId.Should().Be(Scroll);
        owner.Inventory[StackSlot].Count.Should().Be(3);
        Slaves(provider).Should().BeEmpty();
        (await Records(provider)).Should().BeEmpty();
        var collected = Last(sent, SlaveMerchantSub.Collect);
        collected.ReadByte().Should().Be((byte)SlaveMerchantCollectResult.Collected);
        collected.ReadUInt().Should().Be(2 * ScrollPrice);
        collected.ReadByte().Should().Be(1);
        collected.ReadByte().Should().Be(0);
        (await StoredMoney(provider, OwnerId)).Should().Be(owner.Money);
        (await StoredItems(provider, OwnerId)).Should().Equal(owner.SerializeItems());
    }

    [Fact]
    public async Task ATimedItemCollectedFromASlaveDoesNotJoinAPermanentStack()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 5, stock: 5);
        owner.Inventory[StackSlot].ExpiresAt = RentalEnds;
        await Insert(provider, owner, toSlave: true);
        owner.Inventory[StackSlot].ItemId = Scroll;
        owner.Inventory[StackSlot].Count = Kept;
        owner.Inventory[StackSlot].Durability = 1;

        await Service(provider).CollectAsync(owner);

        owner.Inventory[StackSlot].Count.Should().Be(Kept);
        owner.Inventory[StackSlot].ExpiresAt.Should().Be(0);
        owner.Inventory.Single(slot => slot.ItemId == Scroll && slot.ExpiresAt == RentalEnds).Count.Should().Be(5);
    }

    [Fact]
    public async Task ASaleIsSavedWithTheBuyerSoACrashCannotDuplicateCoinsOrItems()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 5, stock: 5);
        await Insert(provider, owner, toSlave: true);

        await Buy(provider, Slave(provider), count: 2);

        var buyer = provider.GetRequiredService<SessionManager>().GetByCharacterId(BuyerId)!;
        buyer.Money.Should().Be(BuyerPurse - 2 * ScrollPrice);
        (await StoredMoney(provider, BuyerId)).Should().Be(buyer.Money);
        (await StoredItems(provider, BuyerId)).Should().Equal(buyer.SerializeItems());
        var record = (await Records(provider)).Single();
        record.EarnedNoah.Should().Be(2 * ScrollPrice);
        record.ItemsJson.Should().Contain("\"Count\":3");
    }

    [Fact]
    public async Task ASaleWhoseSaveFailsIsUndoneForTheBuyerAndTheSlave()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 5, stock: 5);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);
        faults.Fails = _ => true;

        var buyerSent = await Buy(provider, slave, count: 2);

        faults.Fails = _ => false;
        var buyer = provider.GetRequiredService<SessionManager>().GetByCharacterId(BuyerId)!;
        buyer.Money.Should().Be(BuyerPurse);
        buyer.Inventory.Skip(InventoryConstants.SlotMax).Take(InventoryConstants.HaveMax).Should().OnlyContain(slot => slot.IsEmpty);
        slave.Money.Should().Be(0);
        slave.Trade.MerchantItems[0].Count.Should().Be(5);
        slave.Inventory[slave.Trade.MerchantItems[0].OriginalSlot].Count.Should().Be(5);
        slave.Trade.IsMerchanting.Should().BeTrue();
        sent.Should().NotContain(p => IsSlavePacket(p, SlaveMerchantSub.Sold));
        buyerSent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MERCHANT);
        var record = (await Records(provider)).Single();
        record.EarnedNoah.Should().Be(0);
        record.ItemsJson.Should().Contain("\"Count\":5");
    }

    [Fact]
    public async Task AFailedSaleKeepsWhatTheBuyerPickedUpMeanwhile()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 5, stock: 5);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);
        var (buyer, _) = Player(provider, BuyerId, "Buyer");
        var stack = buyer.Inventory[InventoryConstants.SlotMax];
        (stack.ItemId, stack.Count, stack.Durability) = (Scroll, Owned, 1);
        var pickedUp = false;
        faults.Fails = _ =>
        {
            if (!pickedUp)
                stack.Count += PickedUp;
            pickedUp = true;
            return true;
        };

        await Buy(provider, slave, count: 2);

        faults.Fails = _ => false;
        stack.Count.Should().Be(Owned + PickedUp, "the undo takes back the scrolls bought and keeps the ones picked up");
        slave.Trade.MerchantItems[0].Count.Should().Be(5);
    }

    [Fact]
    public async Task SalesAndACollectRunningTogetherNeverCreateOrLoseCoinsOrItems()
    {
        const ushort stock = 10;
        for (var round = 0; round < 8; round++)
        {
            using var provider = Provider();
            var (owner, _) = Player(provider, OwnerId, "Trader");
            await Service(provider).GiveHoursAsync(owner.Name, 5);
            Stage(owner, Scroll, StackSlot, count: stock, stock: stock);
            await Insert(provider, owner, toSlave: true);
            var slave = Slave(provider);
            var buyers = Enumerable.Range(FirstCrowdBuyerId, CrowdSize)
                .Select(id => Player(provider, id, $"Buyer{id}").Session)
                .ToList();
            foreach (var buyer in buyers)
            {
                buyer.Money = BuyerPurse;
                buyer.Trade.LookAt(slave.CharacterId, slave.Trade.StallGeneration);
            }

            var steps = buyers.Select(buyer => (Func<Task>)(() => BuyAs(provider, buyer, Scroll, 1))).ToList();
            steps.Insert(round % (steps.Count + 1), () => Service(provider).CollectAsync(owner));
            await Task.WhenAll(steps.Select(Task.Run));
            await Service(provider).CollectAsync(owner);

            var bought = buyers.Sum(buyer => CountOf(buyer, Scroll));
            CountOf(owner, Scroll).Should().Be(stock - bought, $"round {round}");
            buyers.Sum(buyer => (long)(BuyerPurse - buyer.Money)).Should().Be((long)bought * ScrollPrice);
            owner.Money.Should().Be(bought * ScrollPrice);
            Slaves(provider).Should().BeEmpty();
            (await Records(provider)).Should().BeEmpty();
            foreach (var buyer in buyers.Where(buyer => CountOf(buyer, Scroll) > 0))
            {
                (await StoredMoney(provider, buyer.CharacterId)).Should().Be(buyer.Money);
                (await StoredItems(provider, buyer.CharacterId)).Should().Equal(buyer.SerializeItems());
            }
        }
    }

    [Fact]
    public async Task TheOwnerCanNeitherOpenNorBuyFromTheirOwnSlave()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        owner.Money = 1_000_000;
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);

        var open = new Packet(GameOpcodes.GS_MERCHANT);
        open.WriteInt(slave.CharacterId);
        open.ResetOffset();
        await provider.GetRequiredService<IMerchantLifecycleService>().RequestStallAsync(owner, open, MerchantSubOpcode.SellingStallRequest);
        owner.Trade.MerchantTargetUserId.Should().Be(-1);

        owner.Trade.LookAt(slave.CharacterId, slave.Trade.StallGeneration);
        await BuyAs(provider, owner, Scroll, 1);

        owner.Money.Should().Be(1_000_000);
        slave.Trade.MerchantItems[0].Count.Should().Be(4);
        sent.Should().NotContain(p => IsSlavePacket(p, SlaveMerchantSub.Sold));
    }

    [Fact]
    public async Task AnotherCharacterOfTheOwnersAccountCannotBuyFromTheSlave()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);
        provider.GetRequiredService<SessionManager>().RemoveSession(owner);
        var (alt, _) = Player(provider, AltId, "TraderAlt", accountId: OwnerId + AccountOffset);
        alt.Money = BuyerPurse;

        var open = new Packet(GameOpcodes.GS_MERCHANT);
        open.WriteInt(slave.CharacterId);
        open.ResetOffset();
        await provider.GetRequiredService<IMerchantLifecycleService>().RequestStallAsync(alt, open, MerchantSubOpcode.SellingStallRequest);
        alt.Trade.MerchantTargetUserId.Should().Be(-1);

        alt.Trade.LookAt(slave.CharacterId, slave.Trade.StallGeneration);
        await BuyAs(provider, alt, Scroll, 1);

        alt.Money.Should().Be(BuyerPurse);
        slave.Trade.MerchantItems[0].Count.Should().Be(4);
    }

    [Fact]
    public async Task ASlaveCanTakeOverAStallWhereverARegularStallOpens()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        owner.Room = InstanceRoom;
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);

        await Insert(provider, owner, toSlave: true);

        Last(sent, SlaveMerchantSub.Deploy).ReadByte().Should().Be((byte)SlaveMerchantDeployResult.Deployed);
        Slave(provider).Trade.IsMerchanting.Should().BeTrue();
    }

    [Fact]
    public async Task SalesMadeWhileTheOwnerIsAwayArriveAsOneLetterAtTheirNextLogin()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 5, stock: 5);
        await Insert(provider, owner, toSlave: true);
        provider.GetRequiredService<SessionManager>().RemoveSession(owner);

        await Buy(provider, Slave(provider), count: 2);
        await Buy(provider, Slave(provider), count: 1);
        await Service(provider).ReportAwayAsync(owner);
        await Service(provider).ReportAwayAsync(owner);

        var letter = (await Letters(provider, OwnerId)).Single();
        letter.SenderName.Should().Be(MailLimits.SystemSenderName);
        letter.Subject.Should().Be(SlaveMerchantService.AwayLetterSubject);
        letter.Body.Should().Contain("Scroll x3 for 6,000 coins");
        letter.Body.Should().NotContain("It closed");
        (await Records(provider)).Single().AwayReportJson.Should().BeEmpty();
    }

    [Fact]
    public async Task ASlaveThatSoldOutWhileTheOwnerWasAwaySaysSoInTheLetter()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 2, stock: 2);
        await Insert(provider, owner, toSlave: true);
        provider.GetRequiredService<SessionManager>().RemoveSession(owner);

        await Buy(provider, Slave(provider), count: 2);
        await Service(provider).ReportAwayAsync(owner);

        var letter = (await Letters(provider, OwnerId)).Single();
        letter.Body.Should().Contain("Scroll x2 for 4,000 coins");
        letter.Body.Should().Contain("everything was sold");
    }

    [Fact]
    public async Task AnOwnerWhoWatchedTheSalesGetsNoLetter()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 2, stock: 2);
        await Insert(provider, owner, toSlave: true);

        await Buy(provider, Slave(provider), count: 2);
        await Service(provider).ReportAwayAsync(owner);

        (await Letters(provider, OwnerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ALetterThatCannotBeSavedArrivesAtTheNextLoginInstead()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        provider.GetRequiredService<SessionManager>().RemoveSession(owner);
        await Buy(provider, Slave(provider), count: 1);
        faults.Fails = _ => true;

        await Service(provider).ReportAwayAsync(owner);

        faults.Fails = _ => false;
        (await Letters(provider, OwnerId)).Should().BeEmpty();

        await Service(provider).ReportAwayAsync(owner);

        (await Letters(provider, OwnerId)).Should().ContainSingle();
    }

    [Fact]
    public async Task TheHourRunningWhenTheServerStoppedGoesOnAfterTheRestart()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        var stoppedAt = DateTime.UtcNow.AddHours(-3);
        var leftWhenStopped = TimeSpan.FromMinutes(40);
        using (var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.SlaveMerchants.SingleAsync();
            record.HeartbeatAt = stoppedAt;
            record.PaidUntil = stoppedAt + leftWhenStopped;
            await db.SaveChangesAsync();
        }

        var sessions = provider.GetRequiredService<SessionManager>();
        sessions.RemoveSession(Slave(provider));
        var restarted = new SlaveMerchantService(
            sessions,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IGameDataService>(),
            provider.GetRequiredService<IUserNotificationService>(),
            provider.GetRequiredService<ICharacterStatePersister>(),
            provider.GetRequiredService<IMailService>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SlaveMerchantService>>());
        await restarted.LoadActiveAsync();

        Slave(provider).PaidUntil.Should().BeCloseTo(DateTime.UtcNow + leftWhenStopped, TimeSpan.FromMinutes(1));
        (await HoursOf(provider, OwnerId)).Should().Be(2);
    }

    [Fact]
    public async Task EveryTickRecordsThatTheSlaveIsStillSelling()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        var beat = DateTime.UtcNow.AddMinutes(10);

        await Service(provider).TickAsync(beat);

        (await Records(provider)).Single().HeartbeatAt.Should().Be(beat);
    }

    [Fact]
    public async Task ItemsThatDoNotFitStayWithTheSlaveUntilThereIsRoom()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Sword, StackSlot, count: 1, stock: 1);
        await Insert(provider, owner, toSlave: true);
        FillBag(owner);

        await Service(provider).CollectAsync(owner);

        var kept = Last(sent, SlaveMerchantSub.Collect);
        kept.ReadByte().Should().Be((byte)SlaveMerchantCollectResult.Collected);
        kept.ReadUInt();
        kept.ReadByte().Should().Be(0);
        kept.ReadByte().Should().Be(1);
        var record = (await Records(provider)).Single();
        record.IsActive.Should().BeFalse();
        record.ItemsJson.Should().Contain(Sword.ToString());

        owner.Inventory[StackSlot].Clear();
        await Service(provider).CollectAsync(owner);

        owner.Inventory[StackSlot].ItemId.Should().Be(Sword);
        (await Records(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task ACollectThatCannotCloseTheSlaveLeavesItSelling()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        await Buy(provider, Slave(provider), count: 1);
        var moneyBefore = owner.Money;
        faults.Fails = _ => true;

        await Service(provider).CollectAsync(owner);

        faults.Fails = _ => false;
        Last(sent, SlaveMerchantSub.Collect).ReadByte().Should().Be((byte)SlaveMerchantCollectResult.Failed);
        Slave(provider).Trade.IsMerchanting.Should().BeTrue();
        owner.Money.Should().Be(moneyBefore);
        owner.Inventory[StackSlot].IsEmpty.Should().BeTrue();
        var record = (await Records(provider)).Single();
        record.IsActive.Should().BeTrue();
        record.EarnedNoah.Should().Be(ScrollPrice);
    }

    [Fact]
    public async Task ACollectWhoseSaveFailsKeepsEverythingWithTheSlaveForTheNextTry()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        await Buy(provider, Slave(provider), count: 1);
        faults.Fails = context => context.ChangeTracker.Entries<Character>().Any();

        await Service(provider).CollectAsync(owner);

        faults.Fails = _ => false;
        Last(sent, SlaveMerchantSub.Collect).ReadByte().Should().Be((byte)SlaveMerchantCollectResult.Failed);
        Slaves(provider).Should().BeEmpty();
        owner.Money.Should().Be(0);
        owner.Inventory[StackSlot].IsEmpty.Should().BeTrue();
        var record = (await Records(provider)).Single();
        record.IsActive.Should().BeFalse();
        record.EarnedNoah.Should().Be(ScrollPrice);
        record.ItemsJson.Should().Contain("\"Count\":3");

        await Service(provider).CollectAsync(owner);

        owner.Money.Should().Be(ScrollPrice);
        owner.Inventory[StackSlot].Count.Should().Be(3);
        (await Records(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task CoinsAboveTheCoinLimitStayWithTheSlave()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 2, stock: 2);
        await Insert(provider, owner, toSlave: true);
        await Buy(provider, Slave(provider), count: 2);
        owner.Money = ExchangePacketConstants.CoinMax - 1000;

        await Service(provider).CollectAsync(owner);

        owner.Money.Should().Be(ExchangePacketConstants.CoinMax);
        var collected = Last(sent, SlaveMerchantSub.Collect);
        collected.ReadByte().Should().Be((byte)SlaveMerchantCollectResult.Collected);
        collected.ReadUInt().Should().Be(1000);
        collected.ReadByte();
        collected.ReadByte();
        collected.ReadUInt().Should().Be(2 * ScrollPrice - 1000);
        (await Records(provider)).Single().EarnedNoah.Should().Be(2 * ScrollPrice - 1000);
    }

    [Fact]
    public async Task EveryHourOfSellingSpendsAnHourAndTheSlaveClosesWhenNoneAreLeft()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 2);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);

        await Service(provider).TickAsync(slave.PaidUntil.AddSeconds(1));

        (await HoursOf(provider, OwnerId)).Should().Be(0);
        Slaves(provider).Should().ContainSingle();
        Last(sent, SlaveMerchantSub.Time).ReadUShort().Should().Be(0);

        await Service(provider).TickAsync(slave.PaidUntil.AddSeconds(1));

        Slaves(provider).Should().BeEmpty();
        Last(sent, SlaveMerchantSub.Closed).ReadByte().Should().Be((byte)SlaveMerchantClosedReason.OutOfTime);
        var record = (await Records(provider)).Single();
        record.IsActive.Should().BeFalse();
        record.ItemsJson.Should().Contain(Scroll.ToString());
    }

    [Fact]
    public async Task AnHourThatCannotBeSavedIsChargedOnTheNextTick()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);
        var paidUntil = slave.PaidUntil;
        faults.Fails = _ => true;

        await Service(provider).TickAsync(paidUntil.AddSeconds(1));

        faults.Fails = _ => false;
        slave.PaidUntil.Should().Be(paidUntil);
        (await HoursOf(provider, OwnerId)).Should().Be(2);

        await Service(provider).TickAsync(paidUntil.AddSeconds(1));

        slave.PaidUntil.Should().Be(paidUntil.AddHours(1));
        (await HoursOf(provider, OwnerId)).Should().Be(1);
    }

    [Fact]
    public async Task SellingEverythingClosesTheSlaveAndKeepsTheCoins()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 2);
        Stage(owner, Scroll, StackSlot, count: 2, stock: 2);
        await Insert(provider, owner, toSlave: true);

        await Buy(provider, Slave(provider), count: 2);

        Slaves(provider).Should().BeEmpty();
        Last(sent, SlaveMerchantSub.Closed).ReadByte().Should().Be((byte)SlaveMerchantClosedReason.SoldOut);
        var record = (await Records(provider)).Single();
        record.IsActive.Should().BeFalse();
        record.EarnedNoah.Should().Be(2 * ScrollPrice);
    }

    [Fact]
    public async Task ASoldOutSlaveThatCouldNotCloseClosesOnTheNextTick()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 2);
        Stage(owner, Scroll, StackSlot, count: 2, stock: 2);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);
        faults.Fails = context => context.ChangeTracker.Entries<SlaveMerchantData>().Any(entry => !entry.Entity.IsActive);

        await Buy(provider, slave, count: 2);

        faults.Fails = _ => false;
        Slaves(provider).Should().ContainSingle();
        (await Records(provider)).Single().EarnedNoah.Should().Be(2 * ScrollPrice);

        await Service(provider).TickAsync(DateTime.UtcNow);

        Slaves(provider).Should().BeEmpty();
        Last(sent, SlaveMerchantSub.Closed).ReadByte().Should().Be((byte)SlaveMerchantClosedReason.SoldOut);
        (await Records(provider)).Single().IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task TheStatusShowsWhatTheSlaveSellsItsCoinsAndTheHoursLeft()
    {
        using var provider = Provider();
        var (owner, sent) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 4);
        Stage(owner, Scroll, StackSlot, count: 5, stock: 5);
        await Insert(provider, owner, toSlave: true);
        await Buy(provider, Slave(provider), count: 1);

        await Service(provider).SendStatusAsync(owner);

        var status = Last(sent, SlaveMerchantSub.Status);
        status.ReadByte().Should().Be(1);
        status.ReadUShort().Should().Be(3);
        status.ReadUInt().Should().Be(ScrollPrice);
        status.ReadByte().Should().Be((byte)MerchantPacketConstants.StallSlots);
        status.ReadInt().Should().Be(Scroll);
        status.ReadUShort().Should().Be(4);
        status.ReadInt().Should().Be(ScrollPrice);
    }

    [Fact]
    public async Task ASlaveThatWasSellingComesBackAfterARestart()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        await Buy(provider, Slave(provider), count: 1);
        var sessions = provider.GetRequiredService<SessionManager>();
        sessions.RemoveSession(Slave(provider));

        var restarted = new SlaveMerchantService(
            sessions,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IGameDataService>(),
            provider.GetRequiredService<IUserNotificationService>(),
            provider.GetRequiredService<ICharacterStatePersister>(),
            provider.GetRequiredService<IMailService>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SlaveMerchantService>>());
        (await restarted.LoadActiveAsync()).Should().Be(1);

        var slave = Slave(provider);
        slave.Name.Should().Be("[S]Trader");
        slave.Money.Should().Be(ScrollPrice);
        slave.Trade.MerchantItems[0].Count.Should().Be(3);
        slave.Trade.IsMerchanting.Should().BeTrue();
    }

    [Fact]
    public async Task ATimedItemASlaveKeptFromAnOlderRecordIsNotSold()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);
        var entry = slave.Trade.MerchantItems[0];
        entry.ExpiresAt = RentalEnds;
        slave.Inventory[entry.OriginalSlot].ExpiresAt = RentalEnds;

        await Buy(provider, slave, count: 1);

        provider.GetRequiredService<SessionManager>().GetByCharacterId(BuyerId)!.Money
            .Should().Be(BuyerPurse, "a timed item cannot change hands, whenever it was listed");
        entry.Count.Should().Be(4);
    }

    [Fact]
    public async Task DeletingTheOwnerTakesTheSlaveItsGoodsAndItsHoursWithIt()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        await Buy(provider, Slave(provider), count: 1);
        provider.GetRequiredService<SessionManager>().RemoveSession(owner);

        var reply = await DeleteOwner(provider);

        reply.ReadShort().Should().Be((short)DeleteCharacterResult.Succeeded);
        Slaves(provider).Should().BeEmpty();
        (await Records(provider)).Should().BeEmpty();
        (await HoursOf(provider, OwnerId)).Should().Be(0);
        (await StoredCharacter(provider, OwnerId)).DeletionTime.Should().NotBeNull();
    }

    [Fact]
    public async Task AnOwnerWhoseDeletionCannotBeSavedKeepsTheSlave()
    {
        var faults = new SaveFaults();
        using var provider = Provider(faults);
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        provider.GetRequiredService<SessionManager>().RemoveSession(owner);
        faults.Fails = _ => true;

        var reply = await DeleteOwner(provider);

        faults.Fails = _ => false;
        reply.ReadShort().Should().NotBe((short)DeleteCharacterResult.Succeeded);
        Slave(provider).Trade.IsMerchanting.Should().BeTrue();
        (await Records(provider)).Should().ContainSingle();
        (await HoursOf(provider, OwnerId)).Should().Be(4);
        (await StoredCharacter(provider, OwnerId)).DeletionTime.Should().BeNull();
    }

    [Fact]
    public async Task NeitherEnemiesNorMonstersCanAttackAMerchantBot()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        var (enemy, _) = Player(provider, BuyerId, "Enemy");
        foreach (var (session, nation) in new[] { (owner, AccountNation.Karus), (enemy, AccountNation.ElMorad) })
        {
            sessions.Regions.RemoveFromRegion(session);
            session.ZoneId = BattleZoneManager.ZONE_RONARK_LAND;
            session.X = 1000;
            session.Z = 1000;
            session.Nation = nation;
            sessions.Regions.AddToRegion(session);
        }

        PvpRules.CanAttackPlayer(enemy, owner).Should().BeTrue();
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);
        var slave = Slave(provider);
        provider.GetRequiredService<SessionManager>().RemoveSession(owner);

        PvpRules.CanAttackPlayer(enemy, slave).Should().BeFalse();
        var monster = provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = slave.ZoneId,
            X = slave.X,
            Z = slave.Z,
            SpawnX = slave.X,
            SpawnZ = slave.Z,
            SearchRange = 30,
            IsMonster = true,
            IsAggressive = true,
            Hp = 100,
            MaxHp = 100,
        });
        var targeting = provider.GetRequiredService<INpcAiTargetingService>();
        targeting.FindAggressiveTarget(monster).Should().BeSameAs(enemy);

        provider.GetRequiredService<SessionManager>().RemoveSession(enemy);
        targeting.FindAggressiveTarget(monster).Should().BeNull();
    }

    [Fact]
    public async Task ASlaveSaleCountsForTheOwnerInTheDailyRankAndInTheMarketPrice()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 5);
        Stage(owner, Scroll, StackSlot, count: 4, stock: 4);
        await Insert(provider, owner, toSlave: true);

        await Buy(provider, Slave(provider), count: 2);

        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DailyRankCounters.AsNoTracking().SingleAsync()).Should().Match<DailyRankCounter>(counter =>
            counter.CharacterId == OwnerId && counter.MerchantGold == 2 * ScrollPrice);
        var price = await db.MarketPriceDays.AsNoTracking().SingleAsync();
        price.ItemId.Should().Be(Scroll);
        price.Quantity.Should().Be(2);
        price.MinPrice.Should().Be(ScrollPrice);
    }

    [Fact]
    public async Task ABoughtItemKeepsItsSerialFromAnyStall()
    {
        using var provider = Provider();
        var (seller, _) = Player(provider, OwnerId, "Trader");
        var (buyer, _) = Player(provider, BuyerId, "Buyer");
        Stage(seller, InventoryConstants.CypherRing, StackSlot, count: 1, stock: 1);
        seller.Inventory[StackSlot].UniqueId = LinkedSerial;
        seller.Trade.MerchantItems[0].UniqueId = LinkedSerial;
        seller.Trade.MerchantState = MerchantMode.Selling;
        buyer.Money = BuyerPurse;
        buyer.Trade.LookAt(seller.CharacterId, seller.Trade.StallGeneration);

        await BuyAs(provider, buyer, InventoryConstants.CypherRing, 1);

        var bought = buyer.Inventory.Single(slot => slot.ItemId == InventoryConstants.CypherRing);
        bought.UniqueId.Should().Be(LinkedSerial);
        seller.Inventory[StackSlot].IsEmpty.Should().BeTrue();
        seller.Money.Should().Be(ScrollPrice);
    }

    [Fact]
    public async Task ARingHandedToASlaveSellsWithItsSerial()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        await Service(provider).GiveHoursAsync(owner.Name, 3);
        Stage(owner, InventoryConstants.CypherRing, StackSlot, count: 1, stock: 1);
        owner.Inventory[StackSlot].UniqueId = LinkedSerial;
        await Insert(provider, owner, toSlave: true);

        await Buy(provider, Slave(provider), 1);

        var buyer = provider.GetRequiredService<SessionManager>().GetByCharacterId(BuyerId)!;
        buyer.Inventory.Single(slot => slot.ItemId == InventoryConstants.CypherRing).UniqueId.Should().Be(LinkedSerial);
    }

    [Fact]
    public async Task AStallWhoseSellerNoLongerHoldsTheItemSellsNothing()
    {
        using var provider = Provider();
        var (seller, _) = Player(provider, OwnerId, "Trader");
        var (buyer, _) = Player(provider, BuyerId, "Buyer");
        Stage(seller, Scroll, StackSlot, count: 4, stock: 4);
        seller.Trade.MerchantState = MerchantMode.Selling;
        seller.Inventory[StackSlot].Count = 1;
        buyer.Money = BuyerPurse;
        buyer.Trade.LookAt(seller.CharacterId, seller.Trade.StallGeneration);

        await BuyAs(provider, buyer, Scroll, 2);

        buyer.Money.Should().Be(BuyerPurse);
        CountOf(buyer, Scroll).Should().Be(0);
        seller.Inventory[StackSlot].Count.Should().Be(1);
        seller.Money.Should().Be(0);
    }

    [Fact]
    public async Task GameMastersGiveAndTakeSlaveMerchantTime()
    {
        using var provider = Provider();
        var (owner, _) = Player(provider, OwnerId, "Trader");
        var (gm, _) = Player(provider, BuyerId, "Master");
        gm.IsGM = true;

        await provider.GetRequiredService<IAdminPacketCoordinator>().HandleGmCommandAsync(gm, "+giveslave Trader 5");
        (await HoursOf(provider, OwnerId)).Should().Be(5);

        await provider.GetRequiredService<IAdminPacketCoordinator>().HandleGmCommandAsync(gm, "+giveslave Trader -9");
        (await HoursOf(provider, OwnerId)).Should().Be(0);
    }

    private static ServiceProvider Provider(SaveFaults? faults = null) =>
        CreateProvider(
            db => db.Characters.AddRange(new[] { (OwnerId, "Trader"), (BuyerId, "Buyer"), (AltId, "TraderAlt") }
                .Concat(Enumerable.Range(FirstCrowdBuyerId, CrowdSize).Select(id => (id, $"Buyer{id}")))
                .Select(entry => new Character
                {
                    Id = entry.Item1, AccountId = entry.Item1 + AccountOffset, Name = entry.Item2, Level = 80, Class = 101,
                })),
            gameData =>
            {
                gameData.GetItem(Scroll).Returns(new ItemData { Num = Scroll, Name = "Scroll", Countable = 1, Duration = 1, Weight = 1 });
                gameData.GetItem(Sword).Returns(new ItemData { Num = Sword, Name = "Sword", Countable = 0, Duration = 5000, Weight = 10 });
                gameData.GetItem(InventoryConstants.CypherRing).Returns(
                    new ItemData { Num = InventoryConstants.CypherRing, Name = "Cypher Ring", Countable = 0, Duration = 1, Weight = 1 });
            },
            configureServices: services =>
            {
                if (faults != null)
                    services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults));
            });

    private static ISlaveMerchantService Service(ServiceProvider provider) => provider.GetRequiredService<ISlaveMerchantService>();

    private static SlaveMerchantBotSession Slave(ServiceProvider provider) => Slaves(provider).Single();

    private static List<SlaveMerchantBotSession> Slaves(ServiceProvider provider) =>
        provider.GetRequiredService<SessionManager>().GetAll().OfType<SlaveMerchantBotSession>().ToList();

    private static async Task<List<SlaveMerchantData>> Records(ServiceProvider provider)
    {
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SlaveMerchants.AsNoTracking().ToListAsync();
    }

    private static async Task<int> HoursOf(ServiceProvider provider, int characterId)
    {
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var time = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SlaveMerchantTimes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CharacterId == characterId);
        return time?.Hours ?? 0;
    }

    private static async Task<List<Mail>> Letters(ServiceProvider provider, int characterId)
    {
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Mails.AsNoTracking()
            .Where(mail => mail.RecipientCharacterId == characterId)
            .ToListAsync();
    }

    private static async Task<Character> StoredCharacter(ServiceProvider provider, int characterId)
    {
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking()
            .SingleAsync(c => c.Id == characterId);
    }

    private static async Task<int> StoredMoney(ServiceProvider provider, int characterId) =>
        (await StoredCharacter(provider, characterId)).Money;

    private static async Task<byte[]> StoredItems(ServiceProvider provider, int characterId) =>
        (await StoredCharacter(provider, characterId)).Items;

    private static async Task<Packet> DeleteOwner(ServiceProvider provider)
    {
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var reply = await scope.ServiceProvider.GetRequiredService<IPreGameService>()
            .DeleteCharacterAsync(OwnerId + AccountOffset, 0, "Trader", "1");
        reply.ResetOffset();
        return reply;
    }

    private static void Stage(UserSession owner, int itemId, int slot, ushort count, ushort stock)
    {
        owner.Inventory[slot].ItemId = itemId;
        owner.Inventory[slot].Count = stock;
        owner.Inventory[slot].Durability = 1;
        owner.Trade.IsSellingMerchantPreparing = true;
        owner.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = itemId,
            Count = count,
            Price = ScrollPrice,
            Durability = 1,
            OriginalSlot = (byte)slot,
        };
    }

    private static void GiveCertificate(UserSession owner, int certificate)
    {
        owner.Inventory[InventoryConstants.InventoryStart + CertificateBagPosition].ItemId = certificate;
        owner.Inventory[InventoryConstants.InventoryStart + CertificateBagPosition].Count = 1;
    }

    private static void FillBag(UserSession owner)
    {
        for (var slot = InventoryConstants.InventoryStart; slot < InventoryConstants.CospreStart; slot++)
        {
            owner.Inventory[slot].ItemId = Scroll + 1000 * slot;
            owner.Inventory[slot].Count = 1;
        }
    }

    private static int CountOf(UserSession session, int itemId) =>
        session.Inventory.Where(slot => slot.ItemId == itemId).Sum(slot => slot.Count);

    private static async Task Insert(ServiceProvider provider, UserSession owner, bool toSlave)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteString("Scrolls");
        packet.WriteByte((byte)(toSlave ? 1 : 0));
        packet.ResetOffset();
        await provider.GetRequiredService<IMerchantLifecycleService>().InsertAsync(owner, packet);
    }

    private static async Task<List<Packet>> Buy(ServiceProvider provider, SlaveMerchantBotSession slave, ushort count)
    {
        var sessions = provider.GetRequiredService<SessionManager>();
        var sent = new List<Packet>();
        var buyer = sessions.GetByCharacterId(BuyerId);
        if (buyer == null)
            (buyer, sent) = Player(provider, BuyerId, "Buyer");
        buyer.Money = BuyerPurse;
        buyer.Trade.LookAt(slave.CharacterId, slave.Trade.StallGeneration);
        await BuyAs(provider, buyer, slave.Trade.MerchantItems[0].ItemId, count);
        return sent;
    }

    private static async Task BuyAs(ServiceProvider provider, UserSession buyer, int itemId, ushort count)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteInt(itemId);
        packet.WriteUShort(count);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.ResetOffset();
        await provider.GetRequiredService<IMerchantListingService>().BuyItemAsync(buyer, packet);
    }

    private static bool IsSlavePacket(Packet packet, SlaveMerchantSub sub)
    {
        if (packet.GetOpcode() != (byte)GameOpcodes.GS_SLAVE_MERCHANT)
            return false;
        packet.ResetOffset();
        return packet.ReadByte() == (byte)sub;
    }

    private static Packet Last(List<Packet> sent, SlaveMerchantSub sub)
    {
        var reply = sent.Last(p => IsSlavePacket(p, sub));
        reply.ResetOffset();
        reply.ReadByte();
        return reply;
    }

    private static (UserSession Session, List<Packet> Sent) Player(ServiceProvider provider, int characterId, string name, int? accountId = null)
    {
        var sessions = provider.GetRequiredService<SessionManager>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => { lock (sent) sent.Add(packet); }), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = sessions.CreateSession(client, characterId, accountId ?? characterId + AccountOffset);
        session.Name = name;
        session.ZoneId = 21;
        session.Level = 80;
        session.MaxHp = 5000;
        session.Hp = 5000;
        for (var i = 0; i < session.Inventory.Length; i++)
            session.Inventory[i] = new ItemSlot();
        sessions.Regions.AddToRegion(session);
        return (session, sent);
    }

    private sealed class SaveFaults : SaveChangesInterceptor
    {
        public Func<DbContext, bool> Fails { get; set; } = _ => false;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Check(eventData);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(eventData);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Check(DbContextEventData eventData)
        {
            if (eventData.Context is { } context && Fails(context))
                throw new DbUpdateException("the database went away");
        }
    }
}
