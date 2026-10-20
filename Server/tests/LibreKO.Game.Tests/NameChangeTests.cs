using System.Linq.Expressions;
using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NameChangeTests : GameTestBase
{
    private const int ScrollOfIdentity = 800032000;
    private const int ClanNameScroll = 800086000;
    private const byte CharacterRename = 0;
    private const byte ClanRename = 16;
    private const byte Renamed = 3;
    private const byte ClanRenamed = 16;
    private const int PlayerId = 610;
    private const int OtherId = 611;
    private const short ClanId = 61;
    private const byte ChiefFame = 1;
    private const short AliveHp = 100;
    private const string OldName = "Before";
    private const string NewName = "After";
    private const string OldClan = "Wolves";
    private const string NewClan = "Lions";

    [Fact]
    public async Task ARenameSavesTheNameWithTheBagThatNoLongerHoldsTheScroll()
    {
        using var provider = Provider();
        var (player, sent) = Player(provider, ScrollOfIdentity);

        await Rename(provider, player, CharacterRename, NewName);

        player.Name.Should().Be(NewName);
        Scroll(player).IsEmpty.Should().BeTrue();
        var stored = await StoredAsync(provider);
        stored.Name.Should().Be(NewName);
        stored.Items.Should().Equal(player.SerializeItems(), "the scroll and the name are saved together");
        LastResult(sent, GameOpcodes.GS_NAME_CHANGE).Should().Be(Renamed);
    }

    [Fact]
    public async Task AScrollInAMagicBagPaysForARename()
    {
        using var provider = Provider();
        var (player, _) = Player(provider, ScrollOfIdentity);
        InventoryJournal.CopyInto(Scroll(player), player.Inventory[InventoryConstants.MagicBagStart]);
        Scroll(player).Clear();

        await Rename(provider, player, CharacterRename, NewName);

        player.Name.Should().Be(NewName, "the scroll counts wherever the item check finds it");
        player.Inventory[InventoryConstants.MagicBagStart].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task APlayerAtAStallCannotRename()
    {
        using var provider = Provider();
        var (player, _) = Player(provider, ScrollOfIdentity);
        player.Trade.IsSellingMerchantPreparing = true;

        await Rename(provider, player, CharacterRename, NewName);

        player.Name.Should().Be(OldName, "a scroll on a stall can be bought while the rename waits on the database");
        Scroll(player).ItemId.Should().Be(ScrollOfIdentity);
    }

    [Fact]
    public async Task TheKingKeepsTheirName()
    {
        using var provider = Provider(king: OldName);
        var (player, _) = Player(provider, ScrollOfIdentity);

        await Rename(provider, player, CharacterRename, NewName);

        player.Name.Should().Be(OldName, "the throne is held by name");
        Scroll(player).ItemId.Should().Be(ScrollOfIdentity);
    }

    [Fact]
    public async Task ANameAnotherCharacterHoldsIsNotTaken()
    {
        using var provider = Provider(otherName: NewName);
        var (player, _) = Player(provider, ScrollOfIdentity);

        await Rename(provider, player, CharacterRename, NewName);

        player.Name.Should().Be(OldName);
        Scroll(player).ItemId.Should().Be(ScrollOfIdentity);
    }

    [Fact]
    public async Task AScrollThatLeavesTheBagMidRenameRenamesNothing()
    {
        UserSession? renamer = null;
        var sale = new DuringQuery { Meanwhile = () => Scroll(renamer!).Clear() };
        using var provider = Provider(configure: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(sale)));
        (renamer, _) = Player(provider, ScrollOfIdentity);

        await Rename(provider, renamer, CharacterRename, NewName);

        renamer.Name.Should().Be(OldName, "the scroll went to a buyer, so it paid for no rename");
        (await StoredAsync(provider)).Name.Should().Be(OldName);
    }

    [Fact]
    public async Task ARenameWhoseSaveFailsKeepsTheNameAndTheScroll()
    {
        var faults = new SaveFaults();
        using var provider = Provider(configure: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var (player, _) = Player(provider, ScrollOfIdentity);

        await Rename(provider, player, CharacterRename, NewName);

        player.Name.Should().Be(OldName);
        Scroll(player).ItemId.Should().Be(ScrollOfIdentity);
        (await StoredAsync(provider)).Name.Should().Be(OldName);
    }

    [Fact]
    public async Task AClanRenameSavesTheNameWithTheChiefsBag()
    {
        using var provider = Provider();
        var (chief, sent) = Chief(provider);

        await Rename(provider, chief, ClanRename, NewClan);

        provider.GetRequiredService<SessionManager>().Knights.GetClan(ClanId)!.Name.Should().Be(NewClan);
        Scroll(chief).IsEmpty.Should().BeTrue();
        (await StoredClanAsync(provider)).Name.Should().Be(NewClan);
        (await StoredAsync(provider)).Items.Should().Equal(chief.SerializeItems());
        LastResult(sent, GameOpcodes.GS_NAME_CHANGE, skip: 1).Should().Be(ClanRenamed);
    }

    [Fact]
    public async Task ATradingChiefCannotRenameTheClan()
    {
        using var provider = Provider();
        var (chief, _) = Chief(provider);
        chief.Trade.ExchangeUser = OtherId;

        await Rename(provider, chief, ClanRename, NewClan);

        provider.GetRequiredService<SessionManager>().Knights.GetClan(ClanId)!.Name.Should().Be(OldClan);
        Scroll(chief).ItemId.Should().Be(ClanNameScroll);
    }

    [Fact]
    public async Task AClanRenameWhoseSaveFailsKeepsTheNameAndTheScroll()
    {
        var faults = new SaveFaults();
        using var provider = Provider(configure: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var (chief, _) = Chief(provider);

        await Rename(provider, chief, ClanRename, NewClan);

        provider.GetRequiredService<SessionManager>().Knights.GetClan(ClanId)!.Name.Should().Be(OldClan);
        Scroll(chief).ItemId.Should().Be(ClanNameScroll);
        (await StoredClanAsync(provider)).Name.Should().Be(OldClan);
    }

    [Fact]
    public async Task AClanNameOnlyTheDatabaseKnowsIsNotTaken()
    {
        using var provider = Provider(otherClan: NewClan);
        var (chief, _) = Chief(provider);

        await Rename(provider, chief, ClanRename, NewClan);

        provider.GetRequiredService<SessionManager>().Knights.GetClan(ClanId)!.Name.Should().Be(OldClan);
        Scroll(chief).ItemId.Should().Be(ClanNameScroll);
    }

    private static ServiceProvider Provider(
        string? king = null, string? otherName = null, string? otherClan = null, Action<IServiceCollection>? configure = null) =>
        CreateProvider(db =>
        {
            db.Knights.Add(new KnightsEntity { Id = ClanId, Name = OldClan, Chief = OldName, Nation = (byte)AccountNation.Karus, Flag = 1, Members = 1 });
            if (otherClan != null)
                db.Knights.Add(new KnightsEntity { Id = ClanId + 1, Name = otherClan, Chief = "Other", Nation = (byte)AccountNation.Karus, Flag = 1, Members = 1 });
            db.Characters.Add(new Character { Id = PlayerId, AccountId = PlayerId, Name = OldName, Items = new byte[InventoryConstants.InventoryTotal * 8] });
            if (otherName != null)
                db.Characters.Add(new Character { Id = OtherId, AccountId = OtherId, Name = otherName });
            db.SaveChanges();
        }, gameData =>
        {
            foreach (var scroll in new[] { ScrollOfIdentity, ClanNameScroll })
                gameData.GetItem(scroll).Returns(new ItemData { Num = scroll, Kind = 255, Slot = 15, Duration = 1, ReqLevelMax = 100 });
            gameData.KingSystemTable.Returns(king == null
                ? new Dictionary<byte, KingSystemData>()
                : new Dictionary<byte, KingSystemData> { [(byte)AccountNation.Karus] = new() { Nation = (byte)AccountNation.Karus, KingName = king } });
        }, configureServices: configure);

    private static (UserSession Session, List<Packet> Sent) Player(ServiceProvider provider, int scroll)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, PlayerId, PlayerId);
        session.Name = OldName;
        session.Nation = AccountNation.Karus;
        session.Level = 60;
        session.Hp = AliveHp;
        session.ZoneId = 21;
        var slot = Scroll(session);
        slot.ItemId = scroll;
        slot.Count = 1;
        slot.Durability = 1;
        sessions.Regions.AddToRegion(session);
        return (session, sent);
    }

    private static (UserSession Session, List<Packet> Sent) Chief(ServiceProvider provider)
    {
        var sessions = provider.GetRequiredService<SessionManager>();
        sessions.Knights.AddClan(ClanId, new KnightsEntity { Id = ClanId, Name = OldClan, Chief = OldName, Nation = (byte)AccountNation.Karus, Flag = 1, Members = 1 });
        var (chief, sent) = Player(provider, ClanNameScroll);
        chief.KnightsId = ClanId;
        chief.KnightsFame = ChiefFame;
        return (chief, sent);
    }

    private static ItemSlot Scroll(UserSession session) => session.Inventory[InventoryConstants.InventoryStart];

    private static Task Rename(ServiceProvider provider, UserSession session, byte sub, string name)
    {
        var packet = new Packet(GameOpcodes.GS_NAME_CHANGE);
        packet.WriteByte(sub);
        packet.WriteString(name);
        packet.ResetOffset();
        return provider.GetRequiredService<IMiscPacketCoordinator>().HandleNameChangeAsync(session.Client, packet);
    }

    private static byte LastResult(List<Packet> sent, GameOpcodes opcode, int skip = 0)
    {
        var reply = sent.Last(packet => packet.GetOpcode() == (byte)opcode);
        reply.ResetOffset();
        for (var i = 0; i < skip; i++)
            reply.ReadByte();
        return reply.ReadByte();
    }

    private static async Task<Character> StoredAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking().SingleAsync(c => c.Id == PlayerId);
    }

    private static async Task<KnightsEntity> StoredClanAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Knights.AsNoTracking().SingleAsync(k => k.Id == ClanId);
    }

    private sealed class SaveFaults : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context?.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified
                && entry.Entity is Character or KnightsEntity && entry.Property(nameof(Character.Name)).IsModified) == true
                ? throw new DbUpdateException("the database went away")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class DuringQuery : IQueryExpressionInterceptor
    {
        public Action Meanwhile { get; init; } = () => { };

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            if (queryExpression is MethodCallExpression { Arguments: [EntityQueryRootExpression root, ..] }
                && root.EntityType.ClrType == typeof(Character) && queryExpression.Type == typeof(bool))
                Meanwhile();
            return queryExpression;
        }
    }
}
