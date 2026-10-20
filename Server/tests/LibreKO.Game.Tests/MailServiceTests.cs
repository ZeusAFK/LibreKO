using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class MailServiceTests : GameTestBase
{
    private const int AliceId = 1;
    private const int BobId = 2;
    private const int Apple = 810418000;
    private const int Sword = 110110001;
    private const long RentalEnds = 4_102_444_800;
    private const int SealedCharacter = 4242;
    private const ushort Held = 3;
    private const ushort Sent = 5;

    private static ServiceProvider Provider() => CreateProvider(
        db => db.Characters.AddRange(
            new Character { Id = AliceId, AccountId = 1, Name = "Alice" },
            new Character { Id = BobId, AccountId = 2, Name = "Bob" }),
        gameData =>
        {
            gameData.GetItem(Apple).Returns(new ItemData { Num = Apple, Name = "Apples of Moradon", Countable = 1, Duration = 1, Weight = 1 });
            gameData.GetItem(Sword).Returns(new ItemData { Num = Sword, Name = "Sword", Countable = 0, Duration = 5000, Weight = 1 });
            gameData.GetItem(InventoryConstants.CypherRing).Returns(new ItemData
            {
                Num = InventoryConstants.CypherRing, Name = "Cypher Ring", Countable = 0, Duration = 1, Weight = 1,
            });
        });

    private static void FillBagLeaving(UserSession session, int freeSlots)
    {
        for (var i = 0; i < InventoryConstants.HaveMax - freeSlots; i++)
        {
            var slot = session.Inventory[InventoryConstants.InventoryStart + i];
            slot.ItemId = Sword;
            slot.Count = 1;
        }
    }

    private static void EmptyBag(UserSession session)
    {
        for (var i = 0; i < InventoryConstants.HaveMax; i++)
            session.Inventory[InventoryConstants.InventoryStart + i].Clear();
    }

    private static int CountInBag(UserSession session, int itemId) =>
        session.Inventory.Skip(InventoryConstants.InventoryStart).Take(InventoryConstants.HaveMax)
            .Where(s => s.ItemId == itemId).Sum(s => (int)s.Count);

    private static async Task<int> FirstMailIdAsync(IMailService mail, UserSession session, List<Packet> sent)
    {
        await mail.SendInboxAsync(session);
        var inbox = MailPacket(sent, MailPacketWriter.SubList);
        inbox.ReadUShort();
        return inbox.ReadInt();
    }

    private static (UserSession Session, List<Packet> Sent) Online(ServiceProvider provider, int characterId, string name)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => sent.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId, characterId);
        session.Name = name;
        session.Level = 50;
        session.Hp = 100;
        return (session, sent);
    }

    private static Packet MailPacket(List<Packet> sent, byte sub)
    {
        var packet = sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAIL && p.GetData()[0] == sub);
        packet.ResetOffset();
        packet.ReadByte();
        return packet;
    }

    [Fact]
    public async Task SendAsync_TakesGoldAndItems_StoresMail_AndNotifiesAnOnlineRecipient()
    {
        using var provider = Provider();
        var (alice, aliceSent) = Online(provider, AliceId, "Alice");
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        alice.Money = 10_000;
        var slot = alice.Inventory[InventoryConstants.InventoryStart];
        slot.ItemId = Apple;
        slot.Count = 5;
        var mail = provider.GetRequiredService<IMailService>();

        await mail.SendAsync(alice, "Bob", "Apples", "Enjoy them.", 2_500, [new MailItemPick((byte)InventoryConstants.InventoryStart, 3)]);

        alice.Money.Should().Be(7_500);
        slot.Count.Should().Be(2);
        var result = MailPacket(aliceSent, MailPacketWriter.SubSend);
        result.ReadByte().Should().Be(MailPacketWriter.Succeeded);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Mails.Include(m => m.Attachments).SingleAsync();
        stored.RecipientCharacterId.Should().Be(BobId);
        stored.SenderName.Should().Be("Alice");
        stored.Body.Should().Be("Enjoy them.");
        stored.Attachments.Select(a => (a.Kind, a.ItemId, a.Count)).Should().BeEquivalentTo(
        [
            (MailAttachmentKind.Item, Apple, 3),
            (MailAttachmentKind.Gold, InventoryConstants.ItemGold, 2_500),
        ]);

        var unread = MailPacket(bobSent, MailPacketWriter.SubUnread);
        unread.ReadUShort().Should().Be(1);
        bobSent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_NOTICE);
    }

    [Fact]
    public async Task AMailedItemArrivesAsItWasSent()
    {
        using var provider = Provider();
        var (alice, _) = Online(provider, AliceId, "Alice");
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        var sword = alice.Inventory[InventoryConstants.InventoryStart];
        sword.ItemId = Sword;
        sword.Count = 1;
        sword.Durability = 0;
        sword.Flag = (byte)ItemFlag.NotBound;
        sword.ExpiresAt = RentalEnds;
        var ring = alice.Inventory[InventoryConstants.InventoryStart + 1];
        ring.ItemId = InventoryConstants.CypherRing;
        ring.Count = 1;
        ring.UniqueId = SealedCharacter;
        var mail = provider.GetRequiredService<IMailService>();

        await mail.SendAsync(alice, "Bob", "Kit", "For you.", 0,
        [
            new MailItemPick((byte)InventoryConstants.InventoryStart, 1),
            new MailItemPick((byte)(InventoryConstants.InventoryStart + 1), 1),
        ]);
        EmptyBag(bob);
        await mail.ClaimAsync(bob, await FirstMailIdAsync(mail, bob, bobSent));

        var bag = bob.Inventory.Skip(InventoryConstants.InventoryStart).Take(InventoryConstants.HaveMax).ToList();
        var got = bag.Single(slot => slot.ItemId == Sword);
        got.Durability.Should().Be(0, "the post does not repair a broken sword");
        got.Flag.Should().Be((byte)ItemFlag.NotBound);
        got.ExpiresAt.Should().Be(RentalEnds);
        bag.Single(slot => slot.ItemId == InventoryConstants.CypherRing).UniqueId.Should().Be(SealedCharacter);
    }

    [Fact]
    public async Task AMailedTimedStackDoesNotJoinAPermanentOne()
    {
        using var provider = Provider();
        var (alice, _) = Online(provider, AliceId, "Alice");
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        var apples = alice.Inventory[InventoryConstants.InventoryStart];
        apples.ItemId = Apple;
        apples.Count = Sent;
        apples.ExpiresAt = RentalEnds;
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendAsync(alice, "Bob", "Apples", "Fresh.", 0, [new MailItemPick((byte)InventoryConstants.InventoryStart, Sent)]);
        EmptyBag(bob);
        var own = bob.Inventory[InventoryConstants.InventoryStart];
        own.ItemId = Apple;
        own.Count = Held;

        await mail.ClaimAsync(bob, await FirstMailIdAsync(mail, bob, bobSent));

        own.Count.Should().Be(Held);
        bob.Inventory.Single(slot => slot.ItemId == Apple && slot.ExpiresAt == RentalEnds).Count.Should().Be(Sent);
    }

    [Fact]
    public async Task SendAsync_RefusesUnknownRecipient_SelfAndMissingGold()
    {
        using var provider = Provider();
        var (alice, aliceSent) = Online(provider, AliceId, "Alice");
        alice.Money = 100;
        var mail = provider.GetRequiredService<IMailService>();

        await mail.SendAsync(alice, "Nobody", "Hi", "", 0, []);
        MailPacket(aliceSent, MailPacketWriter.SubSend).ReadByte().Should().Be(MailPacketWriter.Failed);

        await mail.SendAsync(alice, "alice", "Hi", "", 0, []);
        MailPacket(aliceSent, MailPacketWriter.SubSend).ReadByte().Should().Be(MailPacketWriter.Failed);

        await mail.SendAsync(alice, "Bob", "Hi", "", 500, []);
        MailPacket(aliceSent, MailPacketWriter.SubSend).ReadByte().Should().Be(MailPacketWriter.Failed);

        alice.Money.Should().Be(100);
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Mails.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_RefusesBoundItems()
    {
        using var provider = Provider();
        var (alice, aliceSent) = Online(provider, AliceId, "Alice");
        var slot = alice.Inventory[InventoryConstants.InventoryStart];
        slot.ItemId = Apple;
        slot.Count = 5;
        slot.Flag = (byte)ItemFlag.Bound;
        var mail = provider.GetRequiredService<IMailService>();

        await mail.SendAsync(alice, "Bob", "Apples", "", 0, [new MailItemPick((byte)InventoryConstants.InventoryStart, 3)]);

        MailPacket(aliceSent, MailPacketWriter.SubSend).ReadByte().Should().Be(MailPacketWriter.Failed);
        slot.Count.Should().Be(5);
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Mails.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ClaimAsync_DeliversAttachments_ThenDeleteSucceeds()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        bob.Money = 10;
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Gift", "A gift.",
        [
            new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 4, 1),
            new MailAttachmentDraft(MailAttachmentKind.Gold, InventoryConstants.ItemGold, 990),
        ]);

        await mail.SendInboxAsync(bob);
        var inbox = MailPacket(bobSent, MailPacketWriter.SubList);
        inbox.ReadUShort().Should().Be(1);
        var mailId = inbox.ReadInt();
        inbox.ReadSByteString().Should().Be(MailLimits.SystemSenderName);
        inbox.ReadSByteString().Should().Be("Gift");
        inbox.ReadByte().Should().Be(0);
        inbox.ReadByte().Should().Be(MailPacketWriter.AttachmentsPending);
        inbox.ReadLong();
        inbox.ReadByte().Should().Be(2);

        await mail.DeleteAsync(bob, mailId);
        MailPacket(bobSent, MailPacketWriter.SubDelete).ReadByte().Should().Be(MailPacketWriter.Failed);

        await mail.ClaimAsync(bob, mailId);
        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Succeeded);
        bob.Money.Should().Be(1_000);
        bob.Inventory.Skip(InventoryConstants.InventoryStart).Where(s => s.ItemId == Apple).Sum(s => (int)s.Count).Should().Be(4);

        await mail.ClaimAsync(bob, mailId);
        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Failed);
        bob.Money.Should().Be(1_000);

        await mail.DeleteAsync(bob, mailId);
        MailPacket(bobSent, MailPacketWriter.SubDelete).ReadByte().Should().Be(MailPacketWriter.Succeeded);
        await mail.SendInboxAsync(bob);
        MailPacket(bobSent, MailPacketWriter.SubList).ReadUShort().Should().Be(0);
    }

    [Fact]
    public async Task ClaimAsync_IsRefusedWhileTrading()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your items.",
        [
            new MailAttachmentDraft(MailAttachmentKind.Gold, InventoryConstants.ItemGold, 1_000),
            new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 4),
        ]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);
        bob.Trade.ExchangeUser = 9_999;

        await mail.ClaimAsync(bob, mailId);

        bob.Money.Should().Be(0);
        CountInBag(bob, Apple).Should().Be(0);
        bob.Trade.ExchangeUser = -1;
        await mail.ClaimAsync(bob, mailId);
        bob.Money.Should().Be(1_000, "the attachments wait in the mail until the trade is over");
        CountInBag(bob, Apple).Should().Be(4);
    }

    [Fact]
    public async Task ClaimAsync_GoldStopsAtTheCoinCap_AndTheRestWaitsForTheNextClaim()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        bob.Money = ExchangePacketConstants.CoinMax - 100;
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Refund", "Your gold.",
            [new MailAttachmentDraft(MailAttachmentKind.Gold, InventoryConstants.ItemGold, 1_000)]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAsync(bob, mailId);

        bob.Money.Should().Be(ExchangePacketConstants.CoinMax, "a claim never pushes the purse past the cap");
        ClaimMessage(bobSent).Should().Be("An attachment is still waiting. Make room in your purse and claim again.");

        bob.Money = 0;
        await mail.ClaimAsync(bob, mailId);

        bob.Money.Should().Be(900, "the part that did not fit stays in the mail");
    }

    [Fact]
    public async Task ClaimAttachmentAsync_NamesOnlyTheRoomTheChosenAttachmentNeeds()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        bob.Money = ExchangePacketConstants.CoinMax;
        FillBagLeaving(bob, 0);
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your things.",
        [
            new MailAttachmentDraft(MailAttachmentKind.Gold, InventoryConstants.ItemGold, 1_000),
            new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 4),
        ]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAttachmentAsync(bob, mailId, 1);

        ClaimMessage(bobSent).Should().Be("Not enough room in your inventory.", "only the apples were asked for");

        EmptyBag(bob);
        await mail.ClaimAttachmentAsync(bob, mailId, 1);

        ClaimMessage(bobSent).Should().Be("Attachment claimed.", "the gold was not asked for, so there is nothing to make room for");
    }

    [Fact]
    public async Task ClaimAttachmentAsync_SaysWhenTheAttachmentCouldNotBeDelivered()
    {
        const int retiredItem = 123_456_789;
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your things.",
        [
            new MailAttachmentDraft(MailAttachmentKind.Item, retiredItem, 1),
            new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 4),
        ]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAttachmentAsync(bob, mailId, 0);

        ClaimMessage(bobSent).Should().Be("An attachment could not be delivered.");
    }

    [Theory]
    [InlineData(true, "Attachment claimed. An attachment could not be delivered.")]
    [InlineData(false, "Not enough room in your inventory. An attachment could not be delivered.")]
    public async Task ClaimAsync_NamesADroppedAttachmentNextToWhatHappenedToTheRest(bool room, string expected)
    {
        const int retiredItem = 123_456_789;
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        if (!room)
            FillBagLeaving(bob, 0);
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your things.",
        [
            new MailAttachmentDraft(MailAttachmentKind.Item, retiredItem, 1),
            new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 4),
        ]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAsync(bob, mailId);

        ClaimMessage(bobSent).Should().Be(expected);
    }

    [Fact]
    public async Task ClaimAsync_ReadsEachItemOnce_SoAReloadMidClaimCannotLoseIt()
    {
        var apple = new ItemData { Num = Apple, Name = "Apples of Moradon", Countable = 1, Duration = 1, Weight = 1 };
        using var provider = CreateProvider(
            db => db.Characters.AddRange(
                new Character { Id = AliceId, AccountId = 1, Name = "Alice" },
                new Character { Id = BobId, AccountId = 2, Name = "Bob" }),
            // The first read finds the apples; the item table is reloaded without them right after.
            gameData => gameData.GetItem(Apple).Returns(apple, (ItemData?)null));
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your apples.", [new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 4)]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAsync(bob, mailId);

        CountInBag(bob, Apple).Should().Be(4, "the claim acts on the one read it made");
        ClaimMessage(bobSent).Should().Be("Attachment claimed.");
    }

    private static string ClaimMessage(List<Packet> sent)
    {
        var claim = MailPacket(sent, MailPacketWriter.SubClaim);
        claim.ReadByte();
        claim.ReadInt();
        return claim.ReadSByteString();
    }

    [Fact]
    public async Task ClaimAsync_DeliversWhatFits_AndKeepsTheRestForTheNextClaim()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        FillBagLeaving(bob, 2);
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your items.",
        [
            new MailAttachmentDraft(MailAttachmentKind.Item, Sword, 3),
            new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 4),
        ]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAsync(bob, mailId);

        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Succeeded);
        CountInBag(bob, Apple).Should().Be(0);
        CountInBag(bob, Sword).Should().Be(InventoryConstants.HaveMax);

        EmptyBag(bob);
        await mail.ClaimAsync(bob, mailId);

        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Succeeded);
        CountInBag(bob, Sword).Should().Be(1);
        CountInBag(bob, Apple).Should().Be(4);

        await mail.ClaimAsync(bob, mailId);
        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Failed);
    }

    [Fact]
    public async Task ClaimAttachmentAsync_FillsExistingStack_AndRetainsOnlyTheUndeliveredRemainder()
    {
        const int stackRoom = 9;
        const int attached = 20;
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        FillBagLeaving(bob, 0);
        var stack = bob.Inventory[InventoryConstants.InventoryStart];
        stack.ItemId = Apple;
        stack.Count = InventoryConstants.MaxStackCount - stackRoom;
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Apples", "", [new MailAttachmentDraft(MailAttachmentKind.Item, Apple, attached)]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAttachmentAsync(bob, mailId, 0);

        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Succeeded);
        stack.Count.Should().Be(InventoryConstants.MaxStackCount);
        using (var scope = provider.CreateScope())
        {
            var attachment = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MailAttachments.SingleAsync(a => a.MailId == mailId);
            attachment.ClaimedCount.Should().Be(stackRoom);
            attachment.Remaining.Should().Be(attached - stackRoom);
        }

        await mail.ClaimAsync(bob, mailId);
        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Failed);
        stack.Count.Should().Be(InventoryConstants.MaxStackCount);

        bob.Inventory[InventoryConstants.InventoryStart + 1].Clear();
        await mail.ClaimAsync(bob, mailId);
        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Succeeded);
        bob.Inventory[InventoryConstants.InventoryStart + 1].Count.Should().Be(attached - stackRoom);

        await mail.ClaimAsync(bob, mailId);
        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Failed);
        CountInBag(bob, Apple).Should().Be(InventoryConstants.MaxStackCount - stackRoom + attached);
    }

    [Fact]
    public async Task ClaimAsync_WithAFullBag_DeliversNothing_AndKeepsTheMailPending()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        FillBagLeaving(bob, 0);
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your items.", [new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 4)]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAsync(bob, mailId);

        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Failed);
        await mail.SendInboxAsync(bob);
        var inbox = MailPacket(bobSent, MailPacketWriter.SubList);
        inbox.ReadUShort();
        inbox.ReadInt();
        inbox.ReadSByteString();
        inbox.ReadSByteString();
        inbox.ReadByte();
        inbox.ReadByte().Should().Be(MailPacketWriter.AttachmentsPending);
    }

    [Fact]
    public async Task SendInboxAsync_WritesHowManyOfEachAttachmentAreClaimed()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        FillBagLeaving(bob, 1);
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your items.", [new MailAttachmentDraft(MailAttachmentKind.Item, Sword, 3)]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAsync(bob, mailId);
        await mail.SendInboxAsync(bob);

        var inbox = MailPacket(bobSent, MailPacketWriter.SubList);
        inbox.ReadUShort().Should().Be(1);
        inbox.ReadInt();
        inbox.ReadSByteString();
        inbox.ReadSByteString();
        inbox.ReadByte();
        inbox.ReadByte().Should().Be(MailPacketWriter.AttachmentsPending);
        inbox.ReadLong();
        inbox.ReadByte().Should().Be(1);
        inbox.ReadByte().Should().Be((byte)MailAttachmentKind.Item);
        inbox.ReadInt().Should().Be(Sword);
        inbox.ReadInt().Should().Be(3);
        inbox.ReadInt().Should().Be(1);
    }

    [Fact]
    public async Task ClaimAttachmentAsync_ClaimsOnlyThatAttachment_ThenClaimAsyncTakesTheRest()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your items.",
        [
            new MailAttachmentDraft(MailAttachmentKind.Item, Sword, 2),
            new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 4),
        ]);
        var mailId = await FirstMailIdAsync(mail, bob, bobSent);

        await mail.ClaimAttachmentAsync(bob, mailId, 1);

        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Succeeded);
        CountInBag(bob, Apple).Should().Be(4);
        CountInBag(bob, Sword).Should().Be(0);

        await mail.ClaimAsync(bob, mailId);
        CountInBag(bob, Sword).Should().Be(2);
        await mail.ClaimAttachmentAsync(bob, mailId, 0);
        MailPacket(bobSent, MailPacketWriter.SubClaim).ReadByte().Should().Be(MailPacketWriter.Failed);
    }

    [Fact]
    public async Task SendInboxAsync_EndsEachMailWithItsKind()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Store", "Your items.", [new MailAttachmentDraft(MailAttachmentKind.Item, Apple, 1)], MailKind.Store);

        await mail.SendInboxAsync(bob);

        var inbox = MailPacket(bobSent, MailPacketWriter.SubList);
        inbox.ReadUShort().Should().Be(1);
        inbox.ReadInt();
        inbox.ReadSByteString();
        inbox.ReadSByteString();
        inbox.ReadByte();
        inbox.ReadByte();
        inbox.ReadLong();
        inbox.ReadByte().Should().Be(1);
        inbox.ReadByte();
        inbox.ReadInt();
        inbox.ReadInt();
        inbox.ReadInt();
        inbox.ReadByte().Should().Be((byte)MailKind.Store);
    }

    [Fact]
    public async Task ReadAsync_ReturnsBody_AndSendsTheUnreadCountOnlyOnTheFirstRead()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "Welcome", "Hello Bob.", []);
        await mail.SendInboxAsync(bob);
        var inbox = MailPacket(bobSent, MailPacketWriter.SubList);
        inbox.ReadUShort();
        var mailId = inbox.ReadInt();
        var unreadBefore = UnreadPackets(bobSent);

        await mail.ReadAsync(bob, mailId);
        var read = MailPacket(bobSent, MailPacketWriter.SubRead);
        read.ReadByte().Should().Be(MailPacketWriter.Succeeded);
        read.ReadInt().Should().Be(mailId);
        read.ReadSByteString().Should().Be("Hello Bob.");
        UnreadPackets(bobSent).Should().Be(unreadBefore + 1);
        MailPacket(bobSent, MailPacketWriter.SubUnread).ReadUShort().Should().Be(0);
        bobSent.Last().GetData()[0].Should().Be(MailPacketWriter.SubUnread);

        await mail.ReadAsync(bob, mailId);
        UnreadPackets(bobSent).Should().Be(unreadBefore + 1, "a mail already read does not change the count");
    }

    [Fact]
    public async Task ReadingAnUnreadMailSendsTheNewUnreadCount()
    {
        using var provider = Provider();
        var (bob, bobSent) = Online(provider, BobId, "Bob");
        var mail = provider.GetRequiredService<IMailService>();
        await mail.SendSystemMailAsync(BobId, "One", "First.", []);
        await mail.SendSystemMailAsync(BobId, "Two", "Second.", []);
        await mail.SendInboxAsync(bob);
        var inbox = MailPacket(bobSent, MailPacketWriter.SubList);
        inbox.ReadUShort();
        var mailId = inbox.ReadInt();
        bobSent.Clear();

        await mail.ReadAsync(bob, mailId);
        MailPacket(bobSent, MailPacketWriter.SubUnread).ReadUShort().Should().Be(1);

        bobSent.Clear();
        await mail.ReadAsync(bob, mailId);
        bobSent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAIL && p.GetData()[0] == MailPacketWriter.SubUnread);
    }

    private static int UnreadPackets(List<Packet> sent) =>
        sent.Count(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAIL && p.GetData()[0] == MailPacketWriter.SubUnread);
}
