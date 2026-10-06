using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NationTransferTests : GameTestBase
{
    private const int TransferItem = 810096000;
    private const byte WarStatus = 1;
    private const byte WarRunning = 8;
    private const byte OpenBox = 2;
    private const byte Submit = 3;
    private const byte Accepted = 1;
    private const byte InClan = 2;
    private const byte WrongCharacter = 5;
    private const byte NoItem = 7;
    private const short ElMoradRogue = 208;
    private const short ElMoradPriest = 211;

    [Theory]
    [InlineData((byte)CharacterRace.KarusArchTuarek, (short)105, (byte)CharacterRace.ElMoradBarbarian)]
    [InlineData((byte)CharacterRace.KarusTuarek, (short)108, (byte)CharacterRace.ElMoradMale)]
    [InlineData((byte)CharacterRace.KarusWrinkleTuarek, (short)110, (byte)CharacterRace.ElMoradMale)]
    [InlineData((byte)CharacterRace.KarusPuriTuarek, (short)111, (byte)CharacterRace.ElMoradFemale)]
    [InlineData((byte)CharacterRace.KarusKurian, (short)115, (byte)CharacterRace.ElMoradPorutu)]
    [InlineData((byte)CharacterRace.ElMoradFemale, (short)206, (byte)CharacterRace.KarusArchTuarek)]
    [InlineData((byte)CharacterRace.ElMoradMale, (short)208, (byte)CharacterRace.KarusTuarek)]
    [InlineData((byte)CharacterRace.ElMoradMale, (short)210, (byte)CharacterRace.KarusWrinkleTuarek)]
    [InlineData((byte)CharacterRace.ElMoradFemale, (short)210, (byte)CharacterRace.KarusPuriTuarek)]
    [InlineData((byte)CharacterRace.ElMoradMale, (short)212, (byte)CharacterRace.KarusTuarek)]
    [InlineData((byte)CharacterRace.ElMoradFemale, (short)212, (byte)CharacterRace.KarusPuriTuarek)]
    public void TheOtherNationOffersTheMatchingBody(byte race, short classId, byte proposed) =>
        NationTransferRules.ProposedRace(race, classId).Should().Be(proposed);

    [Theory]
    [InlineData((short)108, (short)208)]
    [InlineData((short)211, (short)111)]
    public void TheClassKeepsItsJobInTheOtherNation(short classId, short newClass) =>
        NationTransferRules.NewClass(classId).Should().Be(newClass);

    private ServiceProvider Provider(short clanOfSecond = 0) => CreateProvider(
        db =>
        {
            db.Accounts.Add(new Account { Login = "mover", Password = "pw", Nation = AccountNation.ElMorad, Authority = AccountAuthority.Normal });
            db.SaveChanges();
            var accountId = db.Accounts.Single(a => a.Login == "mover").Id;
            db.Characters.Add(new Character
            {
                AccountId = accountId, Slot = 0, Name = "Rover", Level = 60, Class = ElMoradRogue,
                Race = (byte)CharacterRace.ElMoradMale, Face = 2, Hair = 3, MapId = 2, X = 500, Z = 500, Hp = 100, Mp = 100,
            });
            db.Characters.Add(new Character
            {
                AccountId = accountId, Slot = 1, Name = "Healer", Level = 70, Class = ElMoradPriest,
                Race = (byte)CharacterRace.ElMoradFemale, Face = 1, Hair = 1, MapId = 2, X = 600, Z = 600, Hp = 100, Mp = 100,
                KnightsId = clanOfSecond,
            });
        },
        gameData => gameData.GetItem(TransferItem).Returns(new ItemData
        {
            Num = TransferItem, Name = "Nation Transfer Certificate", Kind = 255, Slot = 15, Duration = 1, ReqLevelMax = 100,
        }));

    private static async Task<(UserSession Session, List<Packet> Sent, IClient Client)> Online(ServiceProvider provider, bool carriesItem)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = db.Characters.Single(c => c.Name == "Rover");
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, character.Id, character.AccountId);
        session.Name = "Rover";
        session.Class = ElMoradRogue;
        session.Race = (byte)CharacterRace.ElMoradMale;
        session.Nation = AccountNation.ElMorad;
        session.Level = 60;
        session.ZoneId = 21;
        session.X = 800;
        session.Z = 400;
        session.Hp = 100;
        session.MaxHp = 100;
        if (carriesItem)
        {
            session.Inventory[InventoryConstants.SlotMax].ItemId = TransferItem;
            session.Inventory[InventoryConstants.SlotMax].Count = 1;
            session.Inventory[InventoryConstants.SlotMax].Durability = 1;
        }
        sessions.Regions.AddToRegion(session);
        return (session, sent, client);
    }

    private static Packet Last(List<Packet> sent)
    {
        var packet = sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_NATION_TRANSFER);
        packet.ResetOffset();
        return packet;
    }

    [Fact]
    public async Task KaishanListsEveryCharacterWithItsBodyInTheOtherNation()
    {
        using var provider = Provider();
        var (session, sent, _) = await Online(provider, carriesItem: true);

        await provider.GetRequiredService<INationTransferService>().OpenAsync(session);

        var packet = Last(sent);
        packet.ReadByte().Should().Be(OpenBox);
        packet.ReadByte().Should().Be(Accepted);
        packet.ReadByte().Should().Be(2);
        packet.ReadShort().Should().Be(0);
        packet.ReadString().Should().Be("Rover");
        packet.ReadByte().Should().Be((byte)CharacterRace.KarusTuarek);
        packet.ReadByte().Should().Be((byte)AccountNation.Karus);
        packet.ReadShort().Should().Be(108);
        packet.ReadByte().Should().Be(2);
        packet.ReadInt().Should().Be(3);
        packet.ReadShort().Should().Be(1);
        packet.ReadString().Should().Be("Healer");
        packet.ReadByte().Should().Be((byte)CharacterRace.KarusPuriTuarek);
        packet.ReadByte().Should().Be((byte)AccountNation.Karus);
        packet.ReadShort().Should().Be(111);
    }

    [Fact]
    public async Task WithoutTheCertificateTheBoxRefuses()
    {
        using var provider = Provider();
        var (session, sent, _) = await Online(provider, carriesItem: false);

        await provider.GetRequiredService<INationTransferService>().OpenAsync(session);

        var packet = Last(sent);
        packet.ReadByte().Should().Be(OpenBox);
        packet.ReadByte().Should().Be(NoItem);
    }

    [Fact]
    public async Task ACharacterInAClanStopsTheTransfer()
    {
        using var provider = Provider(clanOfSecond: 15);
        var (session, sent, _) = await Online(provider, carriesItem: true);

        await provider.GetRequiredService<INationTransferService>().OpenAsync(session);

        var packet = Last(sent);
        packet.ReadByte().Should().Be(OpenBox);
        packet.ReadByte().Should().Be(InClan);
    }

    private static Packet SubmitPacket(byte rogueRace, byte priestRace)
    {
        var packet = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        packet.WriteByte(Submit);
        packet.WriteByte(Accepted);
        packet.WriteByte(2);
        packet.WriteShort(0);
        packet.WriteString("Rover");
        packet.WriteByte(rogueRace);
        packet.WriteByte(4);
        packet.WriteInt(0x01_102030);
        packet.WriteShort(1);
        packet.WriteString("Healer");
        packet.WriteByte(priestRace);
        packet.WriteByte(5);
        packet.WriteInt(0x02_405060);
        packet.ResetOffset();
        return packet;
    }

    [Fact]
    public async Task TheWholeAccountMovesToKarus()
    {
        using var provider = Provider();
        var (session, sent, client) = await Online(provider, carriesItem: true);

        await provider.GetRequiredService<INationTransferService>()
            .HandleAsync(client, SubmitPacket((byte)CharacterRace.KarusTuarek, (byte)CharacterRace.KarusTuarek));

        var reply = Last(sent);
        reply.ReadByte().Should().Be(Submit);
        reply.ReadByte().Should().Be(Accepted);
        session.Nation.Should().Be(AccountNation.Karus);
        session.Class.Should().Be(108);
        session.Race.Should().Be((byte)CharacterRace.KarusTuarek);
        session.Face.Should().Be(4);
        session.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeTrue();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Accounts.Single(a => a.Login == "mover").Nation.Should().Be(AccountNation.Karus);
        var healer = db.Characters.Single(c => c.Name == "Healer");
        healer.Class.Should().Be(111);
        healer.Race.Should().Be((byte)CharacterRace.KarusTuarek);
        healer.Face.Should().Be(5);
        healer.Hair.Should().Be(0x02_405060);
        healer.MapId.Should().Be(21);
    }

    [Fact]
    public async Task ABodyTheNewClassCannotTakeRefusesTheWholeTransfer()
    {
        using var provider = Provider();
        var (session, sent, client) = await Online(provider, carriesItem: true);

        await provider.GetRequiredService<INationTransferService>()
            .HandleAsync(client, SubmitPacket((byte)CharacterRace.KarusArchTuarek, (byte)CharacterRace.KarusPuriTuarek));

        var reply = Last(sent);
        reply.ReadByte().Should().Be(Submit);
        reply.ReadByte().Should().Be(WrongCharacter);
        session.Nation.Should().Be(AccountNation.ElMorad);
        session.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeFalse();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Characters.Single(c => c.Name == "Healer").Class.Should().Be(ElMoradPriest);
    }

    [Fact]
    public async Task NoTransferDuringANationWar()
    {
        using var provider = Provider();
        var (session, sent, _) = await Online(provider, carriesItem: true);
        var battle = provider.GetRequiredService<SessionManager>().Battle;
        battle.OpenBattleZone(BattleZoneManager.BATTLEZONE_OPEN, BattleZoneManager.ZONE_BATTLE1);
        battle.KilledElmoNpc = 2;
        battle.KilledKarusNpc = 1;

        await provider.GetRequiredService<INationTransferService>().OpenAsync(session);

        var packet = Last(sent);
        packet.ReadByte().Should().Be(WarStatus);
        packet.ReadByte().Should().Be(WarRunning);
        packet.ReadByte().Should().Be(2);
        packet.ReadByte().Should().Be(1);
    }
}
