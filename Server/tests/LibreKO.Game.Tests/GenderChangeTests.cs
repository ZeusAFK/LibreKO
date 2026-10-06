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

public class GenderChangeTests : GameTestBase
{
    private const int GenderChangeItem = 810594000;
    private const byte Request = 1;
    private const byte Failed = 0;
    private const byte Changed = 1;
    private const byte NoItem = 2;
    private const byte NewFace = 5;
    private const int NewHair = 0x04_10_20_30;

    [Theory]
    [InlineData(101, new byte[] { 1 })]
    [InlineData(107, new byte[] { 2 })]
    [InlineData(109, new byte[] { 3, 4 })]
    [InlineData(112, new byte[] { 2, 4 })]
    [InlineData(115, new byte[] { 6 })]
    [InlineData(206, new byte[] { 11, 12, 13 })]
    [InlineData(208, new byte[] { 12, 13 })]
    [InlineData(203, new byte[] { 12, 13 })]
    [InlineData(211, new byte[] { 12, 13 })]
    [InlineData(213, new byte[] { 14 })]
    public void EachClassMayTakeTheRacesItCouldStartAs(short classId, byte[] races) =>
        GenderChangeRules.AllowedRaces(classId).Should().Equal(races);

    [Theory]
    [InlineData(101, false)]
    [InlineData(102, false)]
    [InlineData(110, true)]
    [InlineData(104, true)]
    [InlineData(214, false)]
    [InlineData(202, true)]
    public void OnlyAClassWithASecondRaceCanChange(short classId, bool canChange) =>
        GenderChangeRules.CanChange(classId).Should().Be(canChange);

    private ServiceProvider Provider() => CreateProvider(
        db =>
        {
            db.Accounts.Add(new Account { Login = "gender", Password = "pw", Nation = AccountNation.ElMorad, Authority = AccountAuthority.Normal });
            db.SaveChanges();
            db.Characters.Add(new Character
            {
                AccountId = db.Accounts.Single(a => a.Login == "gender").Id,
                Slot = 0,
                Name = "Changer",
                Level = 60,
                Class = 208,
                Race = (byte)CharacterRace.ElMoradMale,
                Face = 1,
                Hair = 2,
                MapId = 21,
                X = 800,
                Z = 400,
                Hp = 1000,
                Mp = 1000,
            });
        },
        gameData => gameData.GetItem(GenderChangeItem).Returns(new ItemData
        {
            Num = GenderChangeItem, Name = "Gender Change", Kind = 255, Slot = 15, Duration = 1, ReqLevelMax = 100,
        }));

    private static async Task<(UserSession Session, List<Packet> Sent, IClient Client)> Player(
        ServiceProvider provider, short classId, byte race, bool carriesItem)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = db.Characters.Single(c => c.Name == "Changer");
        character.Class = classId;
        character.Race = race;
        await db.SaveChangesAsync();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, character.Id, character.AccountId);
        session.Name = character.Name;
        session.Class = classId;
        session.Race = race;
        session.Face = 1;
        session.Hair = 2;
        session.Level = 60;
        session.Nation = ClassIdHelper.GetNation(classId);
        session.ZoneId = 21;
        session.X = 800;
        session.Z = 400;
        session.Hp = 1000;
        session.MaxHp = 1000;
        if (carriesItem)
        {
            session.Inventory[InventoryConstants.SlotMax].ItemId = GenderChangeItem;
            session.Inventory[InventoryConstants.SlotMax].Count = 1;
            session.Inventory[InventoryConstants.SlotMax].Durability = 1;
        }
        sessions.Regions.AddToRegion(session);
        return (session, sent, client);
    }

    private static async Task Ask(ServiceProvider provider, IClient client, byte race)
    {
        var packet = new Packet(GameOpcodes.GS_GENDER_CHANGE);
        packet.WriteByte(Request);
        packet.WriteByte(race);
        packet.WriteByte(NewFace);
        packet.WriteInt(NewHair);
        packet.ResetOffset();
        await provider.GetRequiredService<IGenderChangePacketCoordinator>().HandleAsync(client, packet);
    }

    private static Packet Reply(List<Packet> sent)
    {
        var reply = sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_GENDER_CHANGE);
        reply.ResetOffset();
        return reply;
    }

    [Fact]
    public async Task AnElMoradRogueBecomesAWomanAndTheItemIsUsedUp()
    {
        using var provider = Provider();
        var (session, sent, client) = await Player(provider, 208, (byte)CharacterRace.ElMoradMale, carriesItem: true);

        await Ask(provider, client, (byte)CharacterRace.ElMoradFemale);

        session.Race.Should().Be((byte)CharacterRace.ElMoradFemale);
        session.Face.Should().Be(NewFace);
        session.Hair.Should().Be(NewHair);
        session.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeTrue();

        var reply = Reply(sent);
        reply.ReadByte().Should().Be(Changed);
        reply.ReadInt().Should().Be(session.CharacterId);
        reply.ReadByte().Should().Be((byte)CharacterRace.ElMoradFemale);
        reply.ReadByte().Should().Be(NewFace);
        reply.ReadInt().Should().Be(NewHair);
        sent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_ZONE_CHANGE, "the world reloads to show the new body");

        await using var scope = provider.CreateAsyncScope();
        var stored = scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.Single(c => c.Name == "Changer");
        stored.Race.Should().Be((byte)CharacterRace.ElMoradFemale);
        stored.Face.Should().Be(NewFace);
        stored.Hair.Should().Be(NewHair);
    }

    [Fact]
    public async Task WithoutTheItemNothingChanges()
    {
        using var provider = Provider();
        var (session, sent, client) = await Player(provider, 208, (byte)CharacterRace.ElMoradMale, carriesItem: false);

        await Ask(provider, client, (byte)CharacterRace.ElMoradFemale);

        session.Race.Should().Be((byte)CharacterRace.ElMoradMale);
        Reply(sent).ReadByte().Should().Be(NoItem);
    }

    [Fact]
    public async Task ARaceTheClassCannotTakeIsRefused()
    {
        using var provider = Provider();
        var (session, sent, client) = await Player(provider, 208, (byte)CharacterRace.ElMoradMale, carriesItem: true);

        await Ask(provider, client, (byte)CharacterRace.ElMoradBarbarian);

        session.Race.Should().Be((byte)CharacterRace.ElMoradMale);
        session.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeFalse();
        Reply(sent).ReadByte().Should().Be(Failed);
    }

    [Fact]
    public async Task AKarusWarriorCannotChange()
    {
        using var provider = Provider();
        var (session, sent, client) = await Player(provider, 106, (byte)CharacterRace.KarusArchTuarek, carriesItem: true);

        await Ask(provider, client, (byte)CharacterRace.KarusTuarek);

        session.Race.Should().Be((byte)CharacterRace.KarusArchTuarek);
        Reply(sent).ReadByte().Should().Be(Failed);
    }
}
