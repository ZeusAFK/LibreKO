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
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class BeautyAppearanceTests : GameTestBase
{
    private const string ShopperAccount = "beauty";
    private const string OtherAccount = "other";
    private const string Shopper = "Shopper";
    private const string Alt = "Alt";
    private const string Stranger = "Stranger";
    private const byte FirstSlot = 0;
    private const byte SecondSlot = 1;
    private const byte ApplySubOpcode = 1;
    private const byte LegacySubOpcode = 0;
    private const byte UnknownSubOpcode = 2;
    private const byte OneLetterName = 1;
    private const byte TwoLetterName = 2;
    private const byte Letter = (byte)'A';
    private const short ElMoradPriest = 206;
    private const byte OldFace = 1;
    private const int OldHair = 0x02_22_11_00;
    private const byte NewFace = 0;
    private const int NewHair = 0x03_5A_38_20;
    private const int Money = 3000;
    private const short Hp = 100;
    private const float X = 800;
    private const float Z = 400;
    private const int PeerId = 999;
    private const int InOutPacketsPerRestyle = 2;
    private const int CouponSlot = InventoryConstants.InventoryStart;
    private const ushort Coupons = 2;
    private const ushort LastCoupon = 1;
    private const short CouponDurability = 1;
    private const int CouponSerial = 4242;
    private const int WarehousedItem = 389010000;
    private const int OtherNpc = NpcData.MakeupArtist + 1;
    private const float OutOfRange = 12;
    private const byte NoSuchFace = 8;
    private const int NoSuchStyle = 0x07_5A_38_20;
    private const int UnknownStyle = 0x7F_5A_38_20;

    private static ServiceProvider Provider(Action<IServiceCollection>? configureServices = null) => CreateProvider(db =>
    {
        db.Accounts.Add(new Account { Login = ShopperAccount, Password = "pw", Nation = AccountNation.ElMorad });
        db.Accounts.Add(new Account { Login = OtherAccount, Password = "pw", Nation = AccountNation.ElMorad });
        db.SaveChanges();
        var shopperAccount = db.Accounts.Single(a => a.Login == ShopperAccount).Id;
        var otherAccount = db.Accounts.Single(a => a.Login == OtherAccount).Id;
        db.Characters.Add(StoredCharacter(shopperAccount, Shopper, FirstSlot));
        db.Characters.Add(StoredCharacter(shopperAccount, Alt, SecondSlot));
        db.Characters.Add(StoredCharacter(otherAccount, Stranger, FirstSlot));
    }, configureServices: configureServices);

    private static Character StoredCharacter(int accountId, string name, byte slot) => new()
    {
        AccountId = accountId,
        Name = name,
        Slot = slot,
        Race = (byte)CharacterRace.ElMoradMale,
        Class = ElMoradPriest,
        Face = OldFace,
        Hair = OldHair,
        Hp = Hp,
        Money = Money,
    };

    private static (IClient Client, List<Packet> Sent) RecordingClient()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => sent.Add(ClonePacket(p))), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return (client, sent);
    }

    private static async Task<(UserSession Session, IClient Client, List<Packet> Sent)> Player(ServiceProvider provider)
    {
        var accountId = await GetAccountIdAsync(provider, ShopperAccount);
        var characterId = await GetCharacterIdAsync(provider, Shopper);
        var (client, sent) = RecordingClient();
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);

        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId, accountId);
        session.Name = Shopper;
        session.Race = (byte)CharacterRace.ElMoradMale;
        session.Class = ElMoradPriest;
        session.Face = OldFace;
        session.Hair = OldHair;
        session.Hp = Hp;
        session.Money = Money;
        session.Nation = AccountNation.ElMorad;
        session.ZoneId = (byte)ZoneId.Moradon;
        session.X = X;
        session.Z = Z;
        session.Inventory[CouponSlot].ItemId = BeautyShopPacketCoordinator.MakeoverCoupon;
        session.Inventory[CouponSlot].Count = Coupons;
        sessions.Regions.AddToRegion(session);
        session.Quest.EventNpcUniqueId = MakeupArtist(sessions).UniqueId;
        return (session, client, sent);
    }

    private static NpcInstance MakeupArtist(SessionManager sessions) => sessions.Regions.SpawnNpc(new NpcInstance
    {
        NpcId = NpcData.MakeupArtist, Name = "Kelly", NpcType = NpcData.TypeTalk, ZoneId = (byte)ZoneId.Moradon,
        X = X, Z = Z, SpawnX = X, SpawnZ = Z, Hp = Hp, MaxHp = Hp,
    });

    private static NpcInstance EventNpc(ServiceProvider provider, UserSession session) =>
        provider.GetRequiredService<SessionManager>().Regions.GetNpc(session.Quest.EventNpcUniqueId)!;

    private static List<Packet> PeerNear(ServiceProvider provider, UserSession near)
    {
        var (client, sent) = RecordingClient();
        var sessions = provider.GetRequiredService<SessionManager>();
        var peer = sessions.CreateSession(client, PeerId, PeerId);
        peer.Name = "Peer";
        peer.Hp = Hp;
        peer.ZoneId = near.ZoneId;
        peer.X = near.X;
        peer.Z = near.Z;
        sessions.Regions.AddToRegion(peer);
        return sent;
    }

    private static Packet Request(string name = Shopper, byte subOpcode = ApplySubOpcode, byte face = NewFace, int hair = NewHair)
    {
        var packet = new Packet(GameOpcodes.GS_CHANGE_HAIR);
        packet.WriteByte(subOpcode);
        packet.WriteSByteString(name);
        packet.WriteByte(face);
        packet.WriteInt(hair);
        packet.ResetOffset();
        return packet;
    }

    private static Task Send(ServiceProvider provider, IClient client, Packet packet) =>
        provider.GetRequiredService<IPacketHandler>().HandlePacket(client, packet);

    private static void ShouldReply(List<Packet> sent, byte result)
    {
        var reply = sent.Where(p => p.GetOpcode() == (byte)GameOpcodes.GS_CHANGE_HAIR).Should().ContainSingle().Subject;
        reply.GetData().Should().Equal(result);
    }

    private static void ShouldKeepOldLook(UserSession session)
    {
        session.Face.Should().Be(OldFace);
        session.Hair.Should().Be(OldHair);
        session.Inventory[CouponSlot].ItemId.Should().Be(BeautyShopPacketCoordinator.MakeoverCoupon);
        session.Inventory[CouponSlot].Count.Should().Be(Coupons);
    }

    private static async Task ShouldRefuse(ServiceProvider provider, UserSession session, IClient client, List<Packet> sent, Packet request)
    {
        await Send(provider, client, request);

        ShouldReply(sent, PreGamePacketWriter.ChangeHairFailed);
        sent.Should().ContainSingle();
        ShouldKeepOldLook(session);
        var stored = (await StoredCharacters(provider)).Single(c => c.Name == Shopper);
        stored.Face.Should().Be(OldFace);
        stored.Hair.Should().Be(OldHair);
    }

    private static ItemSlot[] StoredInventory(Character character)
    {
        var inventory = Enumerable.Range(0, InventoryConstants.InventoryTotal).Select(_ => new ItemSlot()).ToArray();
        UserSessionBinaryState.LoadItems(inventory, character.Items);
        return inventory;
    }

    private static async Task<List<Character>> StoredCharacters(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.ToList();
    }

    private static byte[] AppearanceBytes(byte face, int hair) => [face, .. BitConverter.GetBytes(hair)];

    [Fact]
    public async Task InGameApplyUpdatesTheSessionTheStoredCharacterAndNearbyPlayers()
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);
        var peerPackets = PeerNear(provider, session);

        await Send(provider, client, Request());

        ShouldReply(sent, PreGamePacketWriter.ChangeHairSucceeded);
        session.Face.Should().Be(NewFace);
        session.Hair.Should().Be(NewHair);
        session.Race.Should().Be((byte)CharacterRace.ElMoradMale);
        session.Money.Should().Be(Money);
        session.ZoneId.Should().Be((byte)ZoneId.Moradon);
        session.X.Should().Be(X);
        session.Z.Should().Be(Z);

        peerPackets.Should().HaveCount(InOutPacketsPerRestyle)
            .And.OnlyContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_USER_INOUT);
        peerPackets[0].ReadByte().Should().Be((byte)InOutType.Out);
        peerPackets[1].ReadByte().Should().Be((byte)InOutType.In);
        peerPackets[1].GetData().Should().ContainInConsecutiveOrder(AppearanceBytes(NewFace, NewHair));

        var stored = (await StoredCharacters(provider)).Single(c => c.Name == Shopper);
        stored.Face.Should().Be(NewFace);
        stored.Hair.Should().Be(NewHair);
    }

    [Fact]
    public async Task EachChangeTakesOneMakeoverCouponWithTheSave()
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);

        await Send(provider, client, Request());

        session.Inventory[CouponSlot].Count.Should().Be(Coupons - 1);
        var stored = (await StoredCharacters(provider)).Single(c => c.Name == Shopper);
        StoredInventory(stored)[CouponSlot].Count.Should().Be(Coupons - 1);
        sent.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
    }

    [Fact]
    public async Task TheWarehouseIsSavedWithTheBag()
    {
        using var provider = Provider();
        var (session, client, _) = await Player(provider);
        session.Warehouse[FirstSlot].ItemId = WarehousedItem;

        await Send(provider, client, Request());

        await using var scope = provider.CreateAsyncScope();
        var stored = scope.ServiceProvider.GetRequiredService<AppDbContext>().Warehouses.Single(w => w.AccountId == session.AccountId);
        var warehouse = Enumerable.Range(0, UserSession.WarehouseMax).Select(_ => new ItemSlot()).ToArray();
        UserSessionBinaryState.LoadWarehouse(warehouse, stored.Items);
        warehouse[FirstSlot].ItemId.Should().Be(WarehousedItem);
    }

    [Fact]
    public async Task TheLastCouponLeavesTheBag()
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);
        session.Inventory[CouponSlot].Count = LastCoupon;

        await Send(provider, client, Request());

        ShouldReply(sent, PreGamePacketWriter.ChangeHairSucceeded);
        session.Inventory[CouponSlot].IsEmpty.Should().BeTrue();
        var stored = (await StoredCharacters(provider)).Single(c => c.Name == Shopper);
        StoredInventory(stored)[CouponSlot].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutACouponTheLookIsKept()
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);
        session.Inventory[CouponSlot].Clear();

        await Send(provider, client, Request());

        ShouldReply(sent, PreGamePacketWriter.ChangeHairFailed);
        sent.Should().ContainSingle();
        session.Face.Should().Be(OldFace);
        session.Hair.Should().Be(OldHair);
        (await StoredCharacters(provider)).Single(c => c.Name == Shopper).Face.Should().Be(OldFace);
    }

    public static TheoryData<Action<UserSession, NpcInstance>> AwayFromTheMakeupArtist => new()
    {
        (session, _) => session.Quest.EventNpcUniqueId = 0,
        (_, npc) => npc.NpcId = OtherNpc,
        (_, npc) => npc.X = X + OutOfRange,
        (_, npc) => npc.Hp = 0,
        (session, _) => session.ZoneId = (byte)ZoneId.ElMoradEslant1,
    };

    [Theory]
    [MemberData(nameof(AwayFromTheMakeupArtist))]
    public async Task OnlyTheMakeupArtistInRangeAcceptsAChange(Action<UserSession, NpcInstance> leave)
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);
        leave(session, EventNpc(provider, session));

        await ShouldRefuse(provider, session, client, sent, Request());
    }

    [Theory]
    [InlineData(NoSuchFace, NewHair)]
    [InlineData(NewFace, NoSuchStyle)]
    [InlineData(NewFace, UnknownStyle)]
    public async Task ALookOutsideTheRaceIsRefused(byte face, int hair)
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);

        await ShouldRefuse(provider, session, client, sent, Request(face: face, hair: hair));
    }

    [Theory]
    [InlineData(NoSuchFace, NewHair)]
    [InlineData(NewFace, NoSuchStyle)]
    public async Task ThePreGameChangeRefusesALookOutsideTheRace(byte face, int hair)
    {
        using var provider = Provider();
        var accountId = await GetAccountIdAsync(provider, ShopperAccount);
        await using var scope = provider.CreateAsyncScope();

        var reply = await scope.ServiceProvider.GetRequiredService<IPreGameService>()
            .ChangeHairAsync(accountId, ApplySubOpcode, Shopper, face, hair);

        reply.GetData().Should().Equal(PreGamePacketWriter.ChangeHairFailed);
        (await StoredCharacters(provider)).Should().OnlyContain(c => c.Face == OldFace && c.Hair == OldHair);
    }

    [Fact]
    public async Task TheNextCharacterSaveKeepsTheNewLook()
    {
        using var provider = Provider();
        var (session, client, _) = await Player(provider);

        await Send(provider, client, Request());
        (await provider.GetRequiredService<ICharacterStatePersister>().SaveAsync(session)).Should().BeTrue();

        var stored = (await StoredCharacters(provider)).Single(c => c.Name == Shopper);
        stored.Face.Should().Be(NewFace);
        stored.Hair.Should().Be(NewHair);
        stored.Money.Should().Be(Money);
    }

    [Theory]
    [InlineData(Alt)]
    [InlineData(Stranger)]
    public async Task AnotherCharacterCannotBeRestyledFromInGame(string name)
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);

        await Send(provider, client, Request(name));

        ShouldReply(sent, PreGamePacketWriter.ChangeHairFailed);
        ShouldKeepOldLook(session);
        (await StoredCharacters(provider)).Should().OnlyContain(c => c.Face == OldFace && c.Hair == OldHair);
    }

    [Theory]
    [InlineData(LegacySubOpcode)]
    [InlineData(ApplySubOpcode)]
    public async Task BothSubOpcodesAcceptAZeroBasedAppearance(byte subOpcode)
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);

        await Send(provider, client, Request(subOpcode: subOpcode));

        ShouldReply(sent, PreGamePacketWriter.ChangeHairSucceeded);
        session.Face.Should().Be(NewFace);
        session.Hair.Should().Be(NewHair);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { ApplySubOpcode })]
    [InlineData(new byte[] { ApplySubOpcode, 0 })]
    [InlineData(new byte[] { UnknownSubOpcode, OneLetterName, Letter, NewFace, 0, 0, 0, 0 })]
    [InlineData(new byte[] { ApplySubOpcode, TwoLetterName, Letter, NewFace, 0, 0, 0, 0 })]
    [InlineData(new byte[] { ApplySubOpcode, OneLetterName, Letter, NewFace, 0 })]
    public async Task AMalformedRequestIsRefusedInsteadOfLeftUnanswered(byte[] body)
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);
        var packet = new Packet(GameOpcodes.GS_CHANGE_HAIR);
        packet.WriteBytes(body);
        packet.ResetOffset();

        await Send(provider, client, packet);

        ShouldReply(sent, PreGamePacketWriter.ChangeHairFailed);
        ShouldKeepOldLook(session);
    }

    [Fact]
    public async Task ADeadCharacterCannotRestyle()
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);
        session.Hp = 0;

        await Send(provider, client, Request());

        ShouldReply(sent, PreGamePacketWriter.ChangeHairFailed);
        ShouldKeepOldLook(session);
    }

    [Theory]
    [InlineData(Coupons, false)]
    [InlineData(Coupons, true)]
    [InlineData(LastCoupon, false)]
    [InlineData(LastCoupon, true)]
    public async Task ASaveFailureKeepsTheCouponAndTheOldLook(ushort coupons, bool throws)
    {
        var persister = Substitute.For<ICharacterStatePersister>();
        using var provider = Provider(services => services.AddSingleton(persister));
        var (session, client, sent) = await Player(provider);
        var coupon = session.Inventory[CouponSlot];
        coupon.Count = coupons;
        coupon.Durability = CouponDurability;
        coupon.UniqueId = CouponSerial;
        persister.SaveAsync(session, Arg.Any<CancellationToken>()).Returns(
            throws ? Task.FromException<bool>(new IOException("save failed")) : Task.FromResult(false));

        await Send(provider, client, Request());

        await persister.Received(1).SaveAsync(session, Arg.Any<CancellationToken>());
        ShouldReply(sent, PreGamePacketWriter.ChangeHairFailed);
        sent.Should().ContainSingle();
        session.Face.Should().Be(OldFace);
        session.Hair.Should().Be(OldHair);
        coupon.ItemId.Should().Be(BeautyShopPacketCoordinator.MakeoverCoupon);
        coupon.Count.Should().Be(coupons);
        coupon.Durability.Should().Be(CouponDurability);
        coupon.UniqueId.Should().Be(CouponSerial);
    }

    public static TheoryData<Action<UserSession>> BusyActivities => new()
    {
        session => session.Trade.ExchangeUser = PeerId,
        session => session.Trade.MerchantState = MerchantMode.Selling,
        session => session.IsMining = true,
        session => session.IsFishing = true,
    };

    [Theory]
    [MemberData(nameof(BusyActivities))]
    public async Task RestylingCannotInterruptAnotherActivity(Action<UserSession> startActivity)
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);
        startActivity(session);

        await Send(provider, client, Request());

        ShouldReply(sent, PreGamePacketWriter.ChangeHairFailed);
        ShouldKeepOldLook(session);
    }
}
