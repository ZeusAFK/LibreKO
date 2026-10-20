using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class CharacterSealTests : GameTestBase
{
    private const int AccountA = 1;
    private const int AccountB = 2;
    private const int Scroll = 800111000;
    private const byte BagPos = 3;
    private const string Pin = "1234";

    private const short PlayerHp = 100;

    private static ServiceProvider Provider(Action<IServiceCollection>? configureServices = null) => CreateProvider(db =>
    {
        db.Accounts.Add(new Account { Id = AccountA, Login = "a", Password = "x", Nation = AccountNation.Karus });
        db.Accounts.Add(new Account { Id = AccountB, Login = "b", Password = "x", Nation = AccountNation.Karus });
        db.Characters.Add(new Character { Id = 10, AccountId = AccountA, Slot = 0, Name = "Zeus", Race = 1, Class = 101, Level = 60, MapId = 21 });
        db.Characters.Add(new Character { Id = 11, AccountId = AccountA, Slot = 1, Name = "Velkan", Race = 1, Class = 105, Level = 70, MapId = 21 });
        db.Characters.Add(new Character { Id = 20, AccountId = AccountB, Slot = 0, Name = "Buyer", Race = 1, Class = 101, Level = 60, MapId = 21 });
    }, gameData =>
    {
        gameData.GetItem(Scroll).Returns(new ItemData { Num = Scroll, Kind = CharacterSealService.ScrollKind });
        gameData.GetItem(InventoryConstants.CypherRing).Returns(new ItemData { Num = InventoryConstants.CypherRing, Kind = CharacterSealService.RingKind, Duration = 1 });
    }, configureServices: configureServices);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheRingAndTheSealedCharacterAreSavedTogether(bool saveFails)
    {
        var faults = new SealFaults { Failing = saveFails };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var session = Session(provider, 10, AccountA, "Zeus");
        var scroll = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        scroll.ItemId = Scroll;
        scroll.Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sealedCharacter = await db.Characters.AsNoTracking().SingleAsync(c => c.Id == 11);
        var sealer = await db.Characters.AsNoTracking().SingleAsync(c => c.Id == 10);
        sealedCharacter.Sealed.Should().Be(!saveFails, "a crash between the two writes would lose the character or keep the scroll");
        scroll.ItemId.Should().Be(saveFails ? Scroll : InventoryConstants.CypherRing);
        if (!saveFails)
            sealer.Items.Should().Equal(session.SerializeItems());
    }

    [Fact]
    public async Task ABusyPlayerCannotSeal()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        var scroll = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        scroll.ItemId = Scroll;
        scroll.Count = 1;
        session.Trade.MerchantState = MerchantMode.Selling;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        scroll.ItemId.Should().Be(Scroll, "a scroll on an open stall can be bought while the seal waits on the database");
    }

    [Fact]
    public async Task AScrollThatLeavesTheBagMidSealMakesNoRing()
    {
        UserSession? sealer = null;
        var sale = new DuringCharacterRead { Meanwhile = () => sealer!.Inventory[InventoryConstants.InventoryStart + BagPos].Clear() };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(sale)));
        sealer = Session(provider, 10, AccountA, "Zeus");
        var scroll = sealer.Inventory[InventoryConstants.InventoryStart + BagPos];
        scroll.ItemId = Scroll;
        scroll.Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(sealer, SealRequest("Velkan", Pin));

        scroll.IsEmpty.Should().BeTrue("the scroll went to a buyer, so no ring is made from it");
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking().SingleAsync(c => c.Id == 11))
            .Sealed.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnsealWhoseSaveFailsKeepsTheRingAndTheSeal()
    {
        var faults = new UnsealFaults();
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var (buyer, ring) = await SealedVelkanAsync(provider);

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(buyer, UnsealRequest(InventoryConstants.CypherRing, 2));

        ring.ItemId.Should().Be(InventoryConstants.CypherRing, "the ring and the character are saved together or not at all");
        ring.UniqueId.Should().Be(11);
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking().SingleAsync(c => c.Id == 11))
            .Sealed.Should().BeTrue();
    }

    [Fact]
    public async Task ABusyPlayerCannotUnseal()
    {
        using var provider = Provider();
        var (buyer, ring) = await SealedVelkanAsync(provider);
        buyer.Trade.MerchantState = MerchantMode.Selling;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(buyer, UnsealRequest(InventoryConstants.CypherRing, 2));

        ring.ItemId.Should().Be(InventoryConstants.CypherRing, "a ring on an open stall can be bought while the unseal waits on the database");
    }

    [Fact]
    public async Task ARingThatLeavesTheBagMidUnsealFreesNoOne()
    {
        UserSession? buyer = null;
        var armed = false;
        var sale = new DuringCharacterRead { Meanwhile = () => { if (armed) buyer!.Inventory[InventoryConstants.InventoryStart + BagPos].Clear(); } };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(sale)));
        (buyer, _) = await SealedVelkanAsync(provider);
        armed = true;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(buyer, UnsealRequest(InventoryConstants.CypherRing, 2));

        using var scope = provider.CreateScope();
        var velkan = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking().SingleAsync(c => c.Id == 11);
        velkan.Sealed.Should().BeTrue("the ring went to a buyer, so it frees no character");
        velkan.AccountId.Should().Be(AccountA);
    }

    [Theory]
    [InlineData(RingAt.Nowhere, false)]
    [InlineData(RingAt.Bag, true)]
    [InlineData(RingAt.PartnersOffer, true)]
    [InlineData(RingAt.AskedPartnersOffer, false)]
    public async Task ASealedCharactersSheetIsShownOnlyForARingThePlayerCanSee(RingAt where, bool shown)
    {
        using var provider = Provider();
        var (holder, ring) = await SealedVelkanAsync(provider);
        var viewer = Session(provider, 30, AccountB, "Viewer");
        switch (where)
        {
            case RingAt.Bag:
                InventoryJournal.CopyInto(ring, viewer.Inventory[InventoryConstants.InventoryStart]);
                break;
            case RingAt.PartnersOffer:
                holder.Trade.ExchangeItemList.Add(new ExchangeItem { ItemId = InventoryConstants.CypherRing, Count = 1, UniqueId = 11 });
                holder.Trade.ExchangeUser = viewer.CharacterId;
                viewer.Trade.ExchangeUser = holder.CharacterId;
                holder.Trade.ExchangeAccepted = viewer.Trade.ExchangeAccepted = true;
                break;
            case RingAt.AskedPartnersOffer:
                holder.Trade.ExchangeItemList.Add(new ExchangeItem { ItemId = InventoryConstants.CypherRing, Count = 1, UniqueId = 11 });
                holder.Trade.ExchangeUser = Stranger;
                holder.Trade.ExchangeAccepted = true;
                viewer.Trade.ExchangeUser = holder.CharacterId;
                break;
        }

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(viewer, SheetRequest(11));

        var reply = LastSent(viewer);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SealSub);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SheetSub);
        (reply.ReadByte() == CharacterSealPacketWriter.Succeeded).Should().Be(shown, "a sealed character's bag and gold are no one else's business");
    }

    public enum RingAt { Nowhere, Bag, PartnersOffer, AskedPartnersOffer }

    private const int Stranger = 40;

    private static Packet SheetRequest(int uniqueId)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(CharacterSealPacketWriter.SheetSub);
        packet.WriteInt(uniqueId);
        packet.ResetOffset();
        return packet;
    }

    private static async Task<(UserSession Buyer, ItemSlot Ring)> SealedVelkanAsync(ServiceProvider provider)
    {
        var sealer = Session(provider, 10, AccountA, "Zeus");
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;
        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(sealer, SealRequest("Velkan", Pin));
        var buyer = Session(provider, 20, AccountB, "Buyer");
        var ring = buyer.Inventory[InventoryConstants.InventoryStart + BagPos];
        ring.ItemId = InventoryConstants.CypherRing;
        ring.Count = 1;
        ring.UniqueId = 11;
        return (buyer, ring);
    }

    private sealed class UnsealFaults : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context?.ChangeTracker.Entries<Character>().Any(entry => !entry.Entity.Sealed
                && entry.State == EntityState.Modified && entry.Property(nameof(Character.Sealed)).IsModified) == true
                ? throw new DbUpdateException("the database went away")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class SealFaults : SaveChangesInterceptor
    {
        public bool Failing { get; init; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Failing && eventData.Context?.ChangeTracker.Entries<Character>().Any(entry => entry.Entity.Sealed && entry.State == EntityState.Modified) == true
                ? throw new DbUpdateException("the database went away")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class DuringCharacterRead : IQueryExpressionInterceptor
    {
        public Action Meanwhile { get; init; } = () => { };

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            if (queryExpression.Type.IsGenericType && queryExpression.Type.GetGenericArguments().Contains(typeof(Character))
                || queryExpression.Type == typeof(Character)
                || queryExpression is MethodCallExpression { Arguments: [EntityQueryRootExpression root, ..] } && root.EntityType.ClrType == typeof(Character))
                Meanwhile();
            return queryExpression;
        }
    }

    private static UserSession Session(ServiceProvider provider, int characterId, int accountId, string name)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId, accountId);
        session.Name = name;
        session.Nation = AccountNation.Karus;
        session.VipPassword = Pin;
        session.Hp = session.MaxHp = PlayerHp;
        for (int i = 0; i < session.Inventory.Length; i++) session.Inventory[i] = new ItemSlot();
        return session;
    }

    private static Packet SealRequest(string name, string code)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(CharacterSealPacketWriter.SealCharacterSub);
        packet.WriteInt(-1);
        packet.WriteByte(BagPos);
        packet.WriteInt(Scroll);
        packet.WriteString(name);
        packet.WriteString(code);
        packet.ResetOffset();
        return packet;
    }

    private static Packet UnsealRequest(int ringId, byte slot)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(CharacterSealPacketWriter.UnsealSub);
        packet.WriteInt(-1);
        packet.WriteByte(BagPos);
        packet.WriteInt(ringId);
        packet.WriteByte(slot);
        packet.ResetOffset();
        return packet;
    }

    private static Packet LastSent(UserSession session)
    {
        var packet = session.Client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call => (Packet)call.GetArguments()[0]!)
            .Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        packet.ResetOffset();
        return packet;
    }

    [Fact]
    public async Task SealingTurnsTheScrollIntoARingAndHidesTheCharacterFromSelect()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        var ring = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        ring.ItemId.Should().Be(InventoryConstants.CypherRing);
        ring.UniqueId.Should().Be(11);
        ring.IsTradable.Should().BeTrue();
        ring.IsPetLink.Should().BeFalse();
        var reply = LastSent(session);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SealSub);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SealCharacterSub);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.Succeeded);
        using var scope = provider.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetCharactersByAccount(AccountA);
        listed.Select(c => c.Name).Should().Equal("Zeus");
    }

    [Fact]
    public async Task ARentedScrollSealsNoCharacter()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        var scroll = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        scroll.ItemId = Scroll;
        scroll.Count = 1;
        scroll.ExpireInHours(24, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        scroll.ItemId.Should().Be(Scroll, "a rented ring would expire and leave its character sealed for good");
        using var scope = provider.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetCharactersByAccount(AccountA);
        listed.Select(c => c.Name).Should().Contain("Velkan");
    }

    [Fact]
    public async Task ASealedCharacterCannotBeSelectedByTheAccountThatSealedIt()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;
        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        using var scope = provider.CreateScope();
        var selected = await scope.ServiceProvider.GetRequiredService<IPreGameService>()
            .SelectCharacterAsync(AccountA, "a", "Velkan", 1);

        selected.CharacterId.Should().Be(0);
        selected.Packet.ReadByte().Should().NotBe((byte)SelectCharacterResult.Success);
    }

    [Fact]
    public async Task ASealedCharacterIsNotLoadedIntoTheGame()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;
        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.AccountId.Returns(AccountA);
        client.CharacterId.Returns(11);
        using var scope = provider.CreateScope();

        var loaded = await scope.ServiceProvider.GetRequiredService<IGameSessionInitializer>().InitializeAsync(client);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task AWrongVaultPasswordIsRefusedWithCode22()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", "9999"));

        var reply = LastSent(session);
        reply.ReadByte();
        reply.ReadByte();
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.Failed);
        reply.ReadByte().Should().Be((byte)ItemSealResult.WrongVaultPassword);
    }

    [Fact]
    public async Task UnsealingMovesTheCharacterIntoAnEmptySlotOfAnotherAccount()
    {
        using var provider = Provider();
        var sealer = Session(provider, 10, AccountA, "Zeus");
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;
        var seal = provider.GetRequiredService<ICharacterSealService>();
        await seal.HandleAsync(sealer, SealRequest("Velkan", Pin));

        var buyer = Session(provider, 20, AccountB, "Buyer");
        var ring = buyer.Inventory[InventoryConstants.InventoryStart + BagPos];
        ring.ItemId = InventoryConstants.CypherRing;
        ring.Count = 1;
        ring.UniqueId = 11;

        await seal.HandleAsync(buyer, UnsealRequest(InventoryConstants.CypherRing, 2));

        ring.IsEmpty.Should().BeTrue();
        using var scope = provider.CreateScope();
        var moved = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetById(11);
        moved!.AccountId.Should().Be(AccountB);
        moved.Slot.Should().Be(2);
        moved.Sealed.Should().BeFalse();
    }

    [Fact]
    public void ARingSummaryUsesThePetBlockShape()
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        CharacterSealPacketWriter.WriteSummary(packet, new CharacterSealPacketWriter.RingSummary("Velkan", 105, 70, 2500, 1));
        packet.ResetOffset();

        packet.ReadString().Should().Be("Velkan");
        packet.ReadByte().Should().Be(105);
        packet.ReadByte().Should().Be(70);
        packet.ReadUShort().Should().Be(2500);
        packet.ReadShort().Should().Be(1);
        packet.ReadByte().Should().Be(0);
    }

    [Fact]
    public void ARingOfferedInATradeCarriesItsUniqueIdAndSummary()
    {
        var ring = new CharacterSealPacketWriter.RingSummary("Velkan", 105, 70, 2500, 1, 2);
        var packet = ExchangePacketWriter.ItemOffered(ExchangePacketConstants.ExchangeOtherAdd, InventoryConstants.CypherRing, 1, 1, 11, ring);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(ExchangePacketConstants.ExchangeOtherAdd);
        packet.ReadInt().Should().Be(InventoryConstants.CypherRing);
        packet.ReadInt().Should().Be(1);
        packet.ReadShort().Should().Be(1);
        packet.ReadInt().Should().Be(11);
        packet.ReadString().Should().Be("Velkan");
        packet.ReadByte().Should().Be(105);
        packet.ReadByte().Should().Be(70);
        packet.ReadUShort().Should().Be(2500);
        packet.ReadShort().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AnOrdinaryTradedItemKeepsAZeroExtension()
    {
        var packet = ExchangePacketWriter.Completed(ExchangePacketConstants.ExchangeDone, 0,
            [new ExchangePacketWriter.TransferredItem(4, Scroll, 1, 1, 11)]);
        packet.ResetOffset();

        packet.ReadByte();
        packet.ReadByte().Should().Be(ExchangePacketWriter.Succeeded);
        packet.ReadInt();
        packet.ReadUShort().Should().Be(1);
        packet.ReadByte().Should().Be(4);
        packet.ReadInt().Should().Be(Scroll);
        packet.ReadUShort();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadInt().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }
}
