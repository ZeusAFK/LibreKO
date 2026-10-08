using FluentAssertions;
using LibreKO.Common.Domain.Entities;
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
        sessions.Regions.AddToRegion(session);
        return (session, client, sent);
    }

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

    private static Packet Request(string name = Shopper, byte subOpcode = ApplySubOpcode)
    {
        var packet = new Packet(GameOpcodes.GS_CHANGE_HAIR);
        packet.WriteByte(subOpcode);
        packet.WriteSByteString(name);
        packet.WriteByte(NewFace);
        packet.WriteInt(NewHair);
        packet.ResetOffset();
        return packet;
    }

    private static Task Send(ServiceProvider provider, IClient client, Packet packet) =>
        provider.GetRequiredService<IPacketHandler>().HandlePacket(client, packet);

    private static void ShouldReply(List<Packet> sent, byte result)
    {
        var reply = sent.Should().ContainSingle().Subject;
        reply.GetOpcode().Should().Be((byte)GameOpcodes.GS_CHANGE_HAIR);
        reply.GetData().Should().Equal(result);
    }

    private static void ShouldKeepOldLook(UserSession session)
    {
        session.Face.Should().Be(OldFace);
        session.Hair.Should().Be(OldHair);
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

    [Fact]
    public async Task TheStoredCharacterMustBelongToTheSessionAccount()
    {
        using var provider = Provider();
        var (session, client, sent) = await Player(provider);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Characters.Single(c => c.Name == Shopper).AccountId = db.Accounts.Single(a => a.Login == OtherAccount).Id;
            await db.SaveChangesAsync();
        }

        await Send(provider, client, Request());

        ShouldReply(sent, PreGamePacketWriter.ChangeHairFailed);
        ShouldKeepOldLook(session);
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

    [Fact]
    public async Task ASaveFailureIsRefusedAndKeepsTheLiveLook()
    {
        var preGameService = Substitute.For<IPreGameService>();
        preGameService.ChangeHairAsync(Arg.Any<int>(), Arg.Any<byte>(), Arg.Any<string>(), Arg.Any<byte>(), Arg.Any<int>())
            .Returns(Task.FromException<Packet>(new IOException("save failed")));
        using var provider = Provider(services => services.AddScoped(_ => preGameService));
        var (session, client, sent) = await Player(provider);

        await Send(provider, client, Request());

        ShouldReply(sent, PreGamePacketWriter.ChangeHairFailed);
        ShouldKeepOldLook(session);
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
