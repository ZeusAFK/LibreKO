using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Numerics;

namespace LibreKO.Game.Tests;

public class CharacterTests : GameTestBase
{
    [Fact]
    public async Task CharacterCreateCommand_UsesMoradonStartPositionAndRejectsOccupiedSlot()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "karus-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
            },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(new CoefficientData
                {
                    ClassId = 101,
                    ShortSword = 1,
                    Sword = 1,
                    Axe = 1,
                    Club = 1,
                    Spear = 1,
                    Staff = 1,
                    Bow = 1,
                    Hp = 0.01,
                    Mp = 0.01,
                    Ac = 1,
                    Hitrate = 0.01,
                    Evasionrate = 0.01
                });
                gameData.GetStartPosition((short)21).Returns(new StartPositionData
                {
                    ZoneId = 21,
                    KarusX = 10,
                    KarusZ = 20,
                    ElmoradX = 30,
                    ElmoradZ = 40,
                    RangeX = 0,
                    RangeZ = 0
                });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "karus-user");

        var create = await preGameService.CreateCharacterAsync(accountId, 0, "Alpha", 1, 101, 2, 3, 60, 60, 60, 60, 60);
        create.ReadByte().Should().Be((byte)CreateCharacterResult.Success);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var created = await db.Characters.SingleAsync(c => c.Name == "Alpha");
            created.MapId.Should().Be(21);
            created.X.Should().Be(10);
            created.Z.Should().Be(20);

            var inventorySession = new UserSession(Substitute.For<IClient>(), created.Id, created.AccountId);
            inventorySession.LoadItems(created.Items);
            inventorySession.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(120010000);
            inventorySession.Inventory[InventoryConstants.InventoryStart].Durability.Should().Be(5000);
            inventorySession.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(1);
        }

        var duplicate = await preGameService.CreateCharacterAsync(accountId, 0, "Beta", 1, 101, 2, 3, 60, 60, 60, 60, 60);
        duplicate.ReadByte().Should().Be((byte)CreateCharacterResult.SlotFull);
    }

    [Fact]
    public async Task CharacterCreateCommand_RejectsPartiallyAllocatedStats()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "partial-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
            },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(new CoefficientData { ClassId = 101 });
                gameData.GetStartPosition((short)21).Returns(new StartPositionData { ZoneId = 21 });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "partial-user");

        var create = await preGameService.CreateCharacterAsync(accountId, 0, "Alpha", 1, 101, 2, 3, 50, 50, 50, 50, 50);

        create.ReadByte().Should().Be((byte)CreateCharacterResult.PointsRemaining);
    }

    [Fact]
    public async Task CharacterCreateCommand_RejectsInvalidStarterRaceAndClassCombination()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "starter-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
            },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(new CoefficientData { ClassId = 101 });
                gameData.GetCoefficient(104).Returns(new CoefficientData { ClassId = 104 });
                gameData.GetStartPosition((short)21).Returns(new StartPositionData { ZoneId = 21 });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "starter-user");

        var invalidRace = await preGameService.CreateCharacterAsync(accountId, 0, "BadRace", 9, 101, 2, 3, 60, 60, 60, 60, 60);
        invalidRace.ReadByte().Should().Be((byte)CreateCharacterResult.InvalidRace);

        var invalidCombo = await preGameService.CreateCharacterAsync(accountId, 0, "BadCombo", 1, 104, 2, 3, 60, 60, 60, 60, 60);
        invalidCombo.ReadByte().Should().Be((byte)CreateCharacterResult.InvalidClass);
    }

    [Theory]
    [InlineData(AccountNation.Karus, (byte)CharacterRace.KarusKurian, (short)113)]
    [InlineData(AccountNation.ElMorad, (byte)CharacterRace.ElMoradPorutu, (short)213)]
    public async Task TheKurianFamilyCanBeCreated(AccountNation nation, byte race, short classId)
    {
        using var provider = CreateProvider(
            db => db.Accounts.Add(new Account
            {
                Login = "kurian-user",
                Password = "pw",
                Nation = nation,
                Authority = AccountAuthority.Normal
            }),
            gameData =>
            {
                gameData.GetCoefficient(classId).Returns(new CoefficientData { ClassId = classId });
                gameData.GetStartPosition(Arg.Any<short>()).Returns(new StartPositionData { ZoneId = 21 });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "kurian-user");

        var created = await preGameService.CreateCharacterAsync(
            accountId, 0, "Kurianite", race, classId, 0, 0, 75, 65, 60, 50, 50);

        created.ReadByte().Should().Be((byte)CreateCharacterResult.Success,
            "this family is a creatable race in its own right, not a job change reached from another one");
    }

    [Theory]
    [InlineData(AccountNation.Karus, (byte)CharacterRace.KarusKurian, (short)213)]
    [InlineData(AccountNation.ElMorad, (byte)CharacterRace.ElMoradPorutu, (short)113)]
    public async Task TheKurianFamilyCannotBeCreatedForTheOtherNation(AccountNation nation, byte race, short classId)
    {
        using var provider = CreateProvider(
            db => db.Accounts.Add(new Account
            {
                Login = "kurian-user",
                Password = "pw",
                Nation = nation,
                Authority = AccountAuthority.Normal
            }),
            gameData =>
            {
                gameData.GetCoefficient(classId).Returns(new CoefficientData { ClassId = classId });
                gameData.GetStartPosition(Arg.Any<short>()).Returns(new StartPositionData { ZoneId = 21 });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "kurian-user");

        var created = await preGameService.CreateCharacterAsync(
            accountId, 0, "Kurianite", race, classId, 0, 0, 75, 65, 60, 50, 50);

        created.ReadByte().Should().Be((byte)CreateCharacterResult.InvalidClass);
    }

    [Theory]
    [InlineData((short)101, AccountNation.Karus, 120010000, (short)5000)]
    [InlineData((short)102, AccountNation.Karus, 110010000, (short)4000)]
    [InlineData((short)103, AccountNation.Karus, 180010000, (short)5000)]
    [InlineData((short)104, AccountNation.Karus, 190010000, (short)10000)]
    [InlineData((short)113, AccountNation.Karus, 1110110000, (short)7000)]
    [InlineData((short)201, AccountNation.ElMorad, 120050000, (short)5000)]
    [InlineData((short)202, AccountNation.ElMorad, 110050000, (short)4000)]
    [InlineData((short)203, AccountNation.ElMorad, 180050000, (short)5000)]
    [InlineData((short)204, AccountNation.ElMorad, 190050000, (short)10000)]
    [InlineData((short)213, AccountNation.ElMorad, 1110110000, (short)7000)]
    public void EveryCreatableClassIsGivenAStarterWeapon(
        short classId, AccountNation nation, int expectedItemId, short expectedDurability)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var session = new UserSession(client, characterId: 1, accountId: 1);

        new UserSessionCharacterMapper().HydrateSession(
            session,
            new Character
            {
                Name = "Fresh",
                Class = classId,
                Level = 1,
                Experience = 0,
                Hp = 100,
                Mp = 100,
                MapId = 21
            },
            new Account { Nation = nation },
            new Warehouse(),
            maxHp: 100,
            maxMp: 100,
            Substitute.For<IGameDataService>());

        var slot = session.Inventory[InventoryConstants.InventoryStart];
        slot.ItemId.Should().Be(expectedItemId,
            "a class that can be created and is handed nothing has no way to attack anything");
        slot.Durability.Should().Be(expectedDurability);
        slot.Count.Should().Be(1);
    }

    [Theory]
    [InlineData((short)101, (byte)1, (short)113, (byte)6)]
    [InlineData((short)102, (byte)1, (short)113, (byte)6)]
    [InlineData((short)201, (byte)11, (short)213, (byte)14)]
    [InlineData((short)105, (byte)1, (short)114, (byte)6)]
    [InlineData((short)206, (byte)12, (short)215, (byte)14)]
    public void TheKurianFamilyIsAValidJobChangeTarget(
        short currentClass, byte currentRace, short expectedClass, byte expectedRace)
    {
        var nation = (byte)(currentClass / 100);

        var resolved = JobChangeRules.Resolve(currentClass, currentRace, nation, newJob: 5, changeType: 0);

        resolved.Should().NotBeNull("this family is a job change destination, not only a starting class");
        resolved!.Value.NewClass.Should().Be(expectedClass);
        resolved.Value.NewRace.Should().Be(expectedRace);
    }

    [Theory]
    [InlineData((short)113)]
    [InlineData((short)213)]
    public void TheKurianFamilyCannotChangeIntoItself(short currentClass)
    {
        var nation = (byte)(currentClass / 100);
        var race = currentClass < 200 ? (byte)6 : (byte)14;

        JobChangeRules.Resolve(currentClass, race, nation, newJob: 5, changeType: 0)
            .Should().BeNull();
    }

    [Fact]
    public void UserSessionCharacterMapper_HydrateSession_AddsOriginalStarterWeaponWhenMissing()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var session = new UserSession(client, characterId: 1, accountId: 1);
        var mapper = new UserSessionCharacterMapper();
        var gameData = Substitute.For<IGameDataService>();

        mapper.HydrateSession(
            session,
            new Character
            {
                Name = "FreshMage",
                Race = 13,
                Class = 203,
                Level = 1,
                Experience = 0,
                Face = 1,
                Hair = 1,
                Hp = 100,
                Mp = 100,
                MapId = 21
            },
            new Account
            {
                Nation = AccountNation.ElMorad
            },
            new Warehouse(),
            maxHp: 100,
            maxMp: 100,
            gameData);

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(180050000);
        session.Inventory[InventoryConstants.InventoryStart].Durability.Should().Be(5000);
        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(1);
    }

    [Fact]
    public async Task AllCharacterInfoRequestCommand_UsesFixedSlotOrderAndCorrectEquipmentPreview()
    {
        var alphaItems = CreateInventory(
            (InventoryConstants.Head, 111, 11),
            (InventoryConstants.Breast, 222, 22),
            (InventoryConstants.Pet, 333, 33),
            (InventoryConstants.Leg, 444, 44),
            (InventoryConstants.Glove, 555, 55),
            (InventoryConstants.Foot, 666, 66),
            (InventoryConstants.RightHand, 777, 77),
            (InventoryConstants.LeftHand, 888, 88));

        var gammaItems = CreateInventory(
            (InventoryConstants.Head, 911, 91),
            (InventoryConstants.Breast, 922, 92),
            (InventoryConstants.Pet, 933, 93),
            (InventoryConstants.Leg, 944, 94),
            (InventoryConstants.Glove, 955, 95),
            (InventoryConstants.Foot, 966, 96),
            (InventoryConstants.RightHand, 977, 97),
            (InventoryConstants.LeftHand, 988, 98));

        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "info-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "info-user").Id;
                db.Characters.AddRange(
                    new Character
                    {
                        AccountId = accountId,
                        Slot = 0,
                        Name = "Alpha",
                        Race = 1,
                        Class = 101,
                        Face = 2,
                        Hair = 3,
                        Level = 10,
                        MapId = 1,
                        Items = alphaItems
                    },
                    new Character
                    {
                        AccountId = accountId,
                        Slot = 2,
                        Name = "Gamma",
                        Race = 2,
                        Class = 102,
                        Face = 4,
                        Hair = 5,
                        Level = 20,
                        MapId = 2,
                        Items = gammaItems
                    });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "info-user");
        var packet = await preGameService.GetAllCharacterInfoAsync(accountId);

        packet.ReadByte().Should().Be((byte)AllCharacterInfoOpcode.CharacterList);
        packet.ReadByte().Should().Be(1);

        var slot0 = ReadCharacterInfo(packet);
        var slot1 = ReadCharacterInfo(packet);
        var slot2 = ReadCharacterInfo(packet);
        var slot3 = ReadCharacterInfo(packet);

        slot0.Name.Should().Be("Alpha");
        slot1.Name.Should().BeEmpty();
        slot2.Name.Should().Be("Gamma");
        slot3.Name.Should().BeEmpty();

        // 17 equipment pairs — first 8 are visual slots, then 9 extended (empty)
        // Order: HEAD, BREAST, SHOULDER, RIGHTHAND, LEFTHAND, LEG, GLOVE, FOOT
        slot0.Equipment[0].Should().Be((111, (short)11));   // HEAD
        slot0.Equipment[1].Should().Be((222, (short)22));   // BREAST
        slot0.Equipment[2].Should().Be((333, (short)33));   // SHOULDER
        slot0.Equipment[3].Should().Be((777, (short)77));   // RIGHTHAND
        slot0.Equipment[4].Should().Be((888, (short)88));   // LEFTHAND
        slot0.Equipment[5].Should().Be((444, (short)44));   // LEG
        slot0.Equipment[6].Should().Be((555, (short)55));   // GLOVE
        slot0.Equipment[7].Should().Be((666, (short)66));   // FOOT

        slot2.Equipment[0].Should().Be((911, (short)91));   // HEAD
        slot2.Equipment[1].Should().Be((922, (short)92));   // BREAST
        slot2.Equipment[2].Should().Be((933, (short)93));   // SHOULDER
        slot2.Equipment[3].Should().Be((977, (short)97));   // RIGHTHAND
        slot2.Equipment[4].Should().Be((988, (short)98));   // LEFTHAND
        slot2.Equipment[5].Should().Be((944, (short)94));   // LEG
        slot2.Equipment[6].Should().Be((955, (short)95));   // GLOVE
        slot2.Equipment[7].Should().Be((966, (short)96));   // FOOT

        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task ChangeHairCommand_ReturnsResultOnlyAndUpdatesCharacter()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "hair-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "hair-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Hairy",
                    Face = 2,
                    Hair = 1234
                });
                db.SaveChanges();
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "hair-user");
        var packet = await preGameService.ChangeHairAsync(accountId, 0, "Hairy", 7, 998877);

        packet.GetLength().Should().Be(1);
        packet.ReadByte().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = await db.Characters.SingleAsync(c => c.Name == "Hairy");
        character.Face.Should().Be(7);
        character.Hair.Should().Be(998877);
    }

    [Fact]
    public async Task AllCharacterInfoNameChange_UpdatesOwnedCharacterNameAndReturnsSuccess()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "rename-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "rename-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Before",
                    Race = 1,
                    Class = 101
                });
                db.SaveChanges();
            });

        using var scope = provider.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IPreGamePacketCoordinator>();
        var client = Substitute.For<IClient>();
        client.AccountId.Returns(await GetAccountIdAsync(provider, "rename-user"));

        var request = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        request.WriteByte((byte)AllCharacterInfoOpcode.NameChange);
        request.WriteUShort(1);
        request.WriteString("Before");
        request.WriteString("After");

        var response = await coordinator.HandleAsync(client, request, GameOpcodes.GS_ALLCHAR_INFO_REQ);

        response.Should().NotBeNull();
        response!.ReadByte().Should().Be((byte)AllCharacterInfoOpcode.NameChange);
        response.ReadByte().Should().Be((byte)SelectingCharacterNameChangeResult.Success);
        response.ReadUShort().Should().Be(0);
        response.ReadString().Should().Be("After");
        response.RemainingBytes.Should().Be(0);

        await using var verifyScope = provider.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Characters.AnyAsync(character => character.Name == "After")).Should().BeTrue();
        (await db.Characters.AnyAsync(character => character.Name == "Before")).Should().BeFalse();
    }

    [Fact]
    public async Task AllCharacterInfoNameChange_ReturnsFailureWhenTargetNameExists()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "rename-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "rename-user").Id;
                db.Characters.AddRange(
                    new Character
                    {
                        AccountId = accountId,
                        Slot = 0,
                        Name = "Before",
                        Race = 1,
                        Class = 101
                    },
                    new Character
                    {
                        AccountId = accountId,
                        Slot = 1,
                        Name = "Taken",
                        Race = 1,
                        Class = 101
                    });
                db.SaveChanges();
            });

        using var scope = provider.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IPreGamePacketCoordinator>();
        var client = Substitute.For<IClient>();
        client.AccountId.Returns(await GetAccountIdAsync(provider, "rename-user"));

        var request = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        request.WriteByte((byte)AllCharacterInfoOpcode.NameChange);
        request.WriteUShort(1);
        request.WriteString("Before");
        request.WriteString("Taken");

        var response = await coordinator.HandleAsync(client, request, GameOpcodes.GS_ALLCHAR_INFO_REQ);

        response.Should().NotBeNull();
        response!.ReadByte().Should().Be((byte)AllCharacterInfoOpcode.NameChange);
        response.ReadByte().Should().Be((byte)SelectingCharacterNameChangeResult.Failed);
        response.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task PreGamePacketCoordinator_ParsesSByteChangeHairRequest()
    {
        var preGameService = Substitute.For<IPreGameService>();
        var expected = new Packet(GameOpcodes.GS_CHANGE_HAIR);
        expected.WriteByte(0);
        preGameService
            .ChangeHairAsync(12, 1, "Hairy", 4, 556677)
            .Returns(expected);

        var coordinator = new PreGamePacketCoordinator(
            preGameService,
            new SessionManager(),
            CreateOwningAccountLock(),
            Substitute.For<ISessionTerminationService>(),
            NullLogger<PreGamePacketCoordinator>.Instance);

        var client = Substitute.For<IClient>();
        client.AccountId.Returns(12);

        var request = new Packet(GameOpcodes.GS_CHANGE_HAIR);
        request.WriteByte(1);
        request.WriteSByteString("Hairy");
        request.WriteByte(4);
        request.WriteInt(556677);

        var response = await coordinator.HandleAsync(client, request, GameOpcodes.GS_CHANGE_HAIR);

        response.Should().NotBeNull();
        response!.ReadByte().Should().Be(0);
        await preGameService.Received(1).ChangeHairAsync(12, 1, "Hairy", 4, 556677);
    }

    [Fact]
    public async Task PreGamePacketCoordinator_IgnoresArrangeAllCharInfoSubOpcodes()
    {
        var preGameService = Substitute.For<IPreGameService>();
        var coordinator = new PreGamePacketCoordinator(
            preGameService,
            new SessionManager(),
            CreateOwningAccountLock(),
            Substitute.For<ISessionTerminationService>(),
            NullLogger<PreGamePacketCoordinator>.Instance);

        var client = Substitute.For<IClient>();
        client.AccountId.Returns(99);

        var arrangeOpen = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        arrangeOpen.WriteByte((byte)AllCharacterInfoOpcode.ArrangeOpen);

        var arrangeReceive = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        arrangeReceive.WriteByte((byte)AllCharacterInfoOpcode.ArrangeReceive);

        (await coordinator.HandleAsync(client, arrangeOpen, GameOpcodes.GS_ALLCHAR_INFO_REQ)).Should().BeNull();
        (await coordinator.HandleAsync(client, arrangeReceive, GameOpcodes.GS_ALLCHAR_INFO_REQ)).Should().BeNull();
        await preGameService.DidNotReceive().GetAllCharacterInfoAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task GamePacketHandler_Login_SendsTheLoginSuccessPackets()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "game-login",
                    Password = PasswordHasher.Hash("pw"),
                    Nation = AccountNation.ElMorad,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();
            });

        var packetHandler = provider.GetRequiredService<IPacketHandler>();
        var sentPackets = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(ClonePacket(packet))), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new Packet(GameOpcodes.GS_LOGIN);
        request.WriteString("game-login");
        request.WriteString("pw");

        await packetHandler.HandlePacket(client, request);

        sentPackets.Should().HaveCount(2);

        var loginPacket = sentPackets[0];
        loginPacket.GetOpcode().Should().Be((byte)GameOpcodes.GS_LOGIN);
        loginPacket.ReadByte().Should().Be((byte)AccountNation.ElMorad);
        loginPacket.ReadInt().Should().Be(0);
        loginPacket.ReadByte().Should().Be(6);
        loginPacket.RemainingBytes.Should().Be(0);

        var followUpPacket = sentPackets[1];
        followUpPacket.GetOpcode().Should().Be(0xC0);
        followUpPacket.ReadByte().Should().Be(1);
        followUpPacket.ReadByte().Should().Be(2);
        followUpPacket.ReadByte().Should().Be(1);
        followUpPacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task GamePacketHandler_Login_SendsFailurePacketForInvalidCredentials()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "game-login",
                    Password = PasswordHasher.Hash("pw"),
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();
            });

        var packetHandler = provider.GetRequiredService<IPacketHandler>();
        var sentPackets = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(ClonePacket(packet))), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new Packet(GameOpcodes.GS_LOGIN);
        request.WriteString("game-login");
        request.WriteString("wrong");

        await packetHandler.HandlePacket(client, request);

        sentPackets.Should().HaveCount(1);
        sentPackets[0].GetOpcode().Should().Be((byte)GameOpcodes.GS_LOGIN);
        sentPackets[0].ReadByte().Should().Be(byte.MaxValue);
        sentPackets[0].RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task DeleteCharacterCommand_RejectsClanChief()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "chief-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "chief-user").Id;
                const short clanId = 900;
                db.Knights.Add(new KnightsEntity
                {
                    Id = clanId,
                    Name = "Chiefs",
                    Chief = "ChiefChar",
                    Nation = (byte)AccountNation.Karus,
                    Flag = 1,
                    Grade = 0,
                    Members = 1
                });
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "ChiefChar",
                    Race = 1,
                    Class = 101,
                    KnightsId = clanId,
                    Fame = 1
                });
                db.SaveChanges();
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "chief-user");
        var packet = await preGameService.DeleteCharacterAsync(accountId, 0, "ChiefChar", "123456789");

        packet.ReadShort().Should().Be((short)DeleteCharacterResult.Refused);
        packet.ReadByte().Should().Be(PreGamePacketWriter.DeleteFailedSlot);
        packet.RemainingBytes.Should().Be(0);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = await db.Characters.SingleAsync(c => c.Name == "ChiefChar");
        character.DeletionTime.Should().BeNull();
    }

    [Fact]
    public async Task CharacterSelectCommand_Uses2239PacketLayoutWithNationSuffix()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "test",
                    Password = "pw",
                    Nation = AccountNation.ElMorad,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "test").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Name = "Zeus",
                    Slot = 0,
                    Race = 2,
                    Class = 205,
                    MapId = 2,
                    X = 1601,
                    Z = 410,
                    Y = 0
                });
            });

        provider.GetRequiredService<SessionManager>().Battle.Victory = 1;

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "test");
        var result = await preGameService.SelectCharacterAsync(accountId, "test", "Zeus", 1);

        result.CharacterId.Should().BeGreaterThan(0);
        // Layout: u8(success) + i16(zone) + i16(x) + i16(z) + i16(y) + u8(nation) + i16(-1)
        result.Packet.GetLength().Should().Be(12);
        result.Packet.ReadByte().Should().Be((byte)SelectCharacterResult.Success);
        result.Packet.ReadShort().Should().Be(2);
        result.Packet.ReadShort().Should().Be(16010);
        result.Packet.ReadShort().Should().Be(4100);
        result.Packet.ReadShort().Should().Be(0);
        result.Packet.ReadByte().Should().Be((byte)AccountNation.ElMorad);
        result.Packet.ReadShort().Should().Be(-1);
        result.Packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task CharacterSelectCommand_FallsBackToNationHomeZoneWhenSavedPositionIsMissing()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "fallback-user",
                    Password = "pw",
                    Nation = AccountNation.ElMorad,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "fallback-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Name = "Fallback",
                    Slot = 0,
                    Race = 12,
                    Class = 201,
                    MapId = 0,
                    X = 0,
                    Z = 0,
                    Y = 0
                });
            },
            gameData =>
            {
                gameData.GetStartPosition((short)2).Returns(new StartPositionData
                {
                    ZoneId = 2,
                    KarusX = 10,
                    KarusZ = 20,
                    ElmoradX = 1598,
                    ElmoradZ = 407,
                    RangeX = 0,
                    RangeZ = 0
                });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "fallback-user");
        var result = await preGameService.SelectCharacterAsync(accountId, "fallback-user", "Fallback", 1);

        result.Packet.ReadByte().Should().Be((byte)SelectCharacterResult.Success);
        // Zone as short
        result.Packet.ReadShort().Should().Be(2);
        result.Packet.ReadShort().Should().Be(15980);
        result.Packet.ReadShort().Should().Be(4070);
        result.Packet.ReadShort().Should().Be(0);
    }

    [Fact]
    public async Task CharacterSelectCommand_RepairsUnsupportedReconnectZoneAndPersistsIt()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "tower-user",
                    Password = "pw",
                    Nation = AccountNation.ElMorad,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "tower-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Name = "TowerStuck",
                    Slot = 0,
                    Race = 12,
                    Class = 201,
                    MapId = 18,
                    X = 389,
                    Z = 1591,
                    Y = 0
                });
            },
            gameData =>
            {
                gameData.GetStartPosition((short)21).Returns(new StartPositionData
                {
                    ZoneId = 21,
                    KarusX = 10,
                    KarusZ = 20,
                    ElmoradX = 817,
                    ElmoradZ = 527,
                    RangeX = 0,
                    RangeZ = 0
                });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "tower-user");
        var result = await preGameService.SelectCharacterAsync(accountId, "tower-user", "TowerStuck", 1);

        result.Packet.ReadByte().Should().Be((byte)SelectCharacterResult.Success);
        result.Packet.ReadShort().Should().Be(CharacterReconnectZoneRepair.SafeReconnectZoneId);
        result.Packet.ReadShort().Should().Be(8180);
        result.Packet.ReadShort().Should().Be(6280);
        result.Packet.ReadShort().Should().Be(0);
        result.Packet.ReadByte().Should().Be((byte)AccountNation.ElMorad);
        result.Packet.ReadShort().Should().Be(-1);
        result.Packet.RemainingBytes.Should().Be(0);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = await db.Characters.SingleAsync(entry => entry.Name == "TowerStuck");
        character.MapId.Should().Be((byte)CharacterReconnectZoneRepair.SafeReconnectZoneId);
        character.X.Should().Be(818);
        character.Z.Should().Be(628);
        character.Y.Should().Be(0);
    }

    [Fact]
    public async Task CharacterSelectCommand_KeepsASavedMoradonTownSpot()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "town-user",
                    Password = "pw",
                    Nation = AccountNation.ElMorad,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "town-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Name = "TownStanding",
                    Slot = 0,
                    Race = 12,
                    Class = 201,
                    MapId = 21,
                    X = 816,
                    Z = 532,
                    Y = 0
                });
            },
            gameData => { });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "town-user");
        var result = await preGameService.SelectCharacterAsync(accountId, "town-user", "TownStanding", 1);

        result.Packet.ReadByte().Should().Be((byte)SelectCharacterResult.Success);
        result.Packet.ReadShort().Should().Be(21);
        result.Packet.ReadShort().Should().Be(8160);
        result.Packet.ReadShort().Should().Be(5320);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = await db.Characters.SingleAsync(entry => entry.Name == "TownStanding");
        character.MapId.Should().Be(21);
        character.X.Should().Be(816);
        character.Z.Should().Be(532);
    }

    [Fact]
    public void CharacterPacketMapper_AllCharacterInfo_MatchesTheRetailCharacterListLayout()
    {
        var items = new byte[InventoryConstants.InventoryTotal * 8];
        WriteInventoryItem(items, InventoryConstants.Head, 111, 100);
        WriteInventoryItem(items, InventoryConstants.Breast, 222, 90);
        WriteInventoryItem(items, InventoryConstants.Pet, 333, 80);
        WriteInventoryItem(items, InventoryConstants.RightHand, 444, 70);
        WriteInventoryItem(items, InventoryConstants.LeftHand, 555, 60);
        WriteInventoryItem(items, InventoryConstants.Leg, 666, 50);
        WriteInventoryItem(items, InventoryConstants.Glove, 777, 40);
        WriteInventoryItem(items, InventoryConstants.Foot, 888, 30);
        WriteInventoryItem(items, InventoryConstants.CosWing, 999, 20);
        WriteInventoryItem(items, InventoryConstants.CosTalisman, 1010, 10);

        var packet = CharacterPacketMapper.BuildAllCharacterInfo([
            new Character
            {
                Slot = 0,
                Name = "Rin",
                Race = 11,
                Class = 202,
                Level = 63,
                RebirthLevel = 2,
                Face = 3,
                Hair = 0x04AABBCC,
                MapId = 21,
                Items = items
            }
        ]);

        packet.ReadByte().Should().Be((byte)AllCharacterInfoOpcode.CharacterList);
        packet.ReadByte().Should().Be(PreGamePacketWriter.CharacterListReady);

        packet.ReadString().Should().Be("Rin");
        packet.ReadByte().Should().Be(11);
        packet.ReadShort().Should().Be(202);
        packet.ReadByte().Should().Be(63, "level precedes rebirth");
        packet.ReadByte().Should().Be(2, "rebirth precedes face");
        packet.ReadByte().Should().Be(3, "face follows rebirth");
        packet.ReadInt().Should().Be(0x04AABBCC, "hair packs the style into the top byte");
        packet.ReadShort().Should().Be(21);

        int[] expectedItems = [111, 222, 333, 444, 555, 666, 777, 888, 999, 0, 0, 0, 0, 0, 0, 0, 1010];
        short[] expectedDurability = [100, 90, 80, 70, 60, 50, 40, 30, 20, 0, 0, 0, 0, 0, 0, 0, 10];

        for (var wireSlot = 0; wireSlot < PreGamePacketWriter.CharSelectEquipmentSlots; wireSlot++)
        {
            packet.ReadInt().Should().Be(expectedItems[wireSlot], $"wire slot {wireSlot} carries its item");
            packet.ReadShort().Should().Be(expectedDurability[wireSlot], $"wire slot {wireSlot} carries its durability");
        }

        for (var emptySlot = 1; emptySlot < GameConstants.MaxAccountCharacters; emptySlot++)
        {
            packet.ReadString().Should().BeEmpty();
            packet.ReadBytes(12 + PreGamePacketWriter.CharSelectEquipmentSlots * 6);
        }

        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void PreGamePacketWriter_DeleteCharacter_SendsAShortResultThenTheSlot()
    {
        var refused = PreGamePacketWriter.DeleteCharacter(
            DeleteCharacterResult.Refused, PreGamePacketWriter.DeleteFailedSlot);

        refused.GetLength().Should().Be(3);
        refused.ReadShort().Should().Be((short)DeleteCharacterResult.Refused,
            "the retail client treats any result above zero as a successful delete");
        refused.ReadByte().Should().Be(PreGamePacketWriter.DeleteFailedSlot);

        var deleted = PreGamePacketWriter.DeleteCharacter(DeleteCharacterResult.Succeeded, 2);

        deleted.ReadShort().Should().Be((short)DeleteCharacterResult.Succeeded);
        deleted.ReadByte().Should().Be(2);
        deleted.RemainingBytes.Should().Be(0);
    }

    private static void WriteInventoryItem(byte[] items, int slot, int itemId, short durability)
    {
        BitConverter.TryWriteBytes(items.AsSpan(slot * 8), itemId);
        BitConverter.TryWriteBytes(items.AsSpan(slot * 8 + 4), durability);
    }

    [Fact]
    public void CharacterPacketMapper_UsesModernSelectLayoutWithNationSuffix()
    {
        var packet = CharacterPacketMapper.BuildSelectCharacterSuccess(new SelectCharacterPacketContext(
            ZoneId: 21,
            PosX: 12345,
            PosZ: 23456,
            PosY: 345,
            Nation: (byte)AccountNation.ElMorad));

        packet.GetLength().Should().Be(12);
        packet.ReadByte().Should().Be((byte)SelectCharacterResult.Success);
        packet.ReadShort().Should().Be(21);
        packet.ReadShort().Should().Be(12345);
        packet.ReadShort().Should().Be(23456);
        packet.ReadShort().Should().Be(345);
        packet.ReadByte().Should().Be((byte)AccountNation.ElMorad);
        packet.ReadShort().Should().Be(-1);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task PacketProvider_UnwrapPacket_ReturnsActualSequenceId()
    {
        var cipher = new PacketCipher(new BigInteger(123456789));
        var packet = new Packet(LoginOpcodes.LS_LOGIN);
        packet.WriteString("user");

        var bytes = PacketProvider.WrapPacket(packet, cipher, sequenceId: 42, asClient: true);
        await using var stream = new MemoryStream(bytes);
        var wrapped = await PacketProvider.ReadFromStream(stream, CancellationToken.None);
        var (unwrapped, sequenceId) = PacketProvider.UnwrapPacket(wrapped, encrypted: true, cipher);

        sequenceId.Should().Be(42);
        unwrapped.GetOpcode().Should().Be((byte)LoginOpcodes.LS_LOGIN);
        unwrapped.ReadString().Should().Be("user");
    }

    [Fact]
    public void Packet_Decompress_RejectsCorruptedPayload()
    {
        var payload = new Packet(GameOpcodes.GS_CHAT);
        payload.WriteBytes(new byte[700]);

        var compressed = payload.CompressIfNeeded();
        compressed.GetOpcode().Should().Be((byte)GameOpcodes.GS_COMPRESS_PACKET);
        compressed.ResetOffset();

        var compressedLength = compressed.ReadInt();
        var originalLength = compressed.ReadInt();
        var crc = compressed.ReadUInt();
        var compressedData = compressed.ReadBytes(compressedLength);
        compressedData[0] ^= 0xFF;

        var corrupted = new Packet(GameOpcodes.GS_COMPRESS_PACKET);
        corrupted.WriteInt(compressedLength);
        corrupted.WriteInt(originalLength);
        corrupted.WriteUInt(crc);
        corrupted.WriteBytes(compressedData);

        Packet.Decompress(corrupted).Should().BeNull();
    }

    [Fact]
    public async Task GameStartCommand_SerializesAuthorityAndZeroPremiumHoursForNonPremiumAccounts()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "gm-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.GameMaster
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "gm-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Gamma",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    X = 10,
                    Z = 20,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "gm-user");

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var characterId = await db.Characters.Where(character => character.Name == "Gamma").Select(character => character.Id).SingleAsync();

        var packets = await preGameService.GameStartAsync(characterId, accountId, 1);
        var myInfo = packets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MYINFO);
        var (authority, accountStatus, premiumHours) = ReadMyInfoAuthorityAndPremium(myInfo);
        var noClanCape = ReadMyInfoNoClanCape(myInfo);

        authority.Should().Be((byte)AccountAuthority.GameMaster);
        noClanCape.Should().Be(99);
        accountStatus.Should().Be(0);
        premiumHours.Should().Be(0);
    }

    [Fact]
    public async Task GameStartCommand_RevivesSavedDeadCharacterInMyInfoPacket()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "dead-login-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "dead-login-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "DeadLogin",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 0,
                    Mp = 80,
                    MapId = 21,
                    X = 82.69f,
                    Z = 52.70f,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "dead-login-user");
        var characterId = await GetCharacterIdAsync(provider, "DeadLogin");

        var packets = await preGameService.GameStartAsync(characterId, accountId, 1);
        var myInfo = packets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MYINFO);
        var (maxHp, hp, _, _) = ReadMyInfoVitals(myInfo);

        maxHp.Should().BePositive();
        hp.Should().Be(maxHp);
    }

    [Fact]
    public async Task GamePacketHandler_GameStartPhaseTwo_DoesNotSendPremiumPacketForNonPremiumAccounts()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "plain-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "plain-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Plain",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    X = 10,
                    Z = 20,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var packetHandler = provider.GetRequiredService<IPacketHandler>();
        var accountId = await GetAccountIdAsync(provider, "plain-user");
        var characterId = await GetCharacterIdAsync(provider, "Plain");

        var sentPackets = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.IsCryptoEnabled.Returns(true);
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        (await provider.GetRequiredService<IAccountLockService>()
            .AcquireAsync(client, accountId)).Granted.Should().BeTrue();

        var request = new Packet(GameOpcodes.GS_GAMESTART);
        request.WriteByte(2);

        await packetHandler.HandlePacket(client, request);

        sentPackets.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_PREMIUM);
    }

    [Fact]
    public async Task GamePacketHandler_GameStartPhaseTwo_SendsPremiumPacketForPremiumAccounts()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "premium-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal,
                    PremiumDate = DateTime.UtcNow.AddHours(12)
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "premium-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Premium",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    X = 10,
                    Z = 20,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var packetHandler = provider.GetRequiredService<IPacketHandler>();
        var accountId = await GetAccountIdAsync(provider, "premium-user");
        var characterId = await GetCharacterIdAsync(provider, "Premium");

        var sentPackets = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.IsCryptoEnabled.Returns(true);
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        (await provider.GetRequiredService<IAccountLockService>()
            .AcquireAsync(client, accountId)).Granted.Should().BeTrue();

        var request = new Packet(GameOpcodes.GS_GAMESTART);
        request.WriteByte(2);

        await packetHandler.HandlePacket(client, request);

        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_PREMIUM);
    }

    [Fact]
    public async Task GamePacketHandler_GameStartPhaseTwo_BroadcastsRespawnUserInOut()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "respawn-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "respawn-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Respawned",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    X = 10,
                    Z = 20,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var packetHandler = provider.GetRequiredService<IPacketHandler>();
        var accountId = await GetAccountIdAsync(provider, "respawn-user");
        var characterId = await GetCharacterIdAsync(provider, "Respawned");

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var viewerClient = Substitute.For<IClient>();
        viewerClient.Id.Returns(Guid.NewGuid());
        Packet? viewerPacket = null;
        viewerClient.SendPacket(Arg.Do<Packet>(packet => viewerPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var viewer = sessionManager.CreateSession(viewerClient, characterId: 9001, accountId: 9002);
        viewer.Name = "Viewer";
        viewer.ZoneId = 1;
        viewer.X = 10;
        viewer.Z = 20;
        sessionManager.Regions.AddToRegion(viewer);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.IsCryptoEnabled.Returns(true);
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        (await provider.GetRequiredService<IAccountLockService>()
            .AcquireAsync(client, accountId)).Granted.Should().BeTrue();

        var request = new Packet(GameOpcodes.GS_GAMESTART);
        request.WriteByte(2);

        await packetHandler.HandlePacket(client, request);

        viewerPacket.Should().NotBeNull();
        viewerPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_USER_INOUT);
        viewerPacket.ResetOffset();
        viewerPacket.ReadByte().Should().Be((byte)InOutType.Respawn);
        viewerPacket.ReadByte().Should().Be(0);
        viewerPacket.ReadInt().Should().Be(characterId);
    }

    [Fact]
    public async Task GamePacketHandler_GameStartPhaseOne_SendsStoryAndQuestBootstrapPackets()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "startup-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "startup-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Startup",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    X = 10,
                    Z = 20,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var packetHandler = provider.GetRequiredService<IPacketHandler>();
        var accountId = await GetAccountIdAsync(provider, "startup-user");
        var characterId = await GetCharacterIdAsync(provider, "Startup");

        var sentPackets = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.IsCryptoEnabled.Returns(true);
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        (await provider.GetRequiredService<IAccountLockService>()
            .AcquireAsync(client, accountId)).Granted.Should().BeTrue();

        var request = new Packet(GameOpcodes.GS_GAMESTART);
        request.WriteByte(1);
        request.WriteSByteString("Startup");

        await packetHandler.HandlePacket(client, request);

        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_STORY);
        var questPackets = sentPackets.Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_QUEST).ToList();
        questPackets.Select(packet =>
        {
            packet.ResetOffset();
            return packet.ReadByte();
        }).Should().BeEquivalentTo([8, 1]);
    }

    [Fact]
    public async Task GameStartCommand_SerializesNoClanAsZeroClanId()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "no-clan-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "no-clan-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Solo",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "no-clan-user");
        var characterId = await GetCharacterIdAsync(provider, "Solo");

        var packets = await preGameService.GameStartAsync(characterId, accountId, 1);
        var myInfo = packets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MYINFO);

        myInfo.ResetOffset();
        myInfo.ReadInt().Should().Be(characterId);
        myInfo.ReadSByteString().Should().Be("Solo");
        myInfo.ReadShort();
        myInfo.ReadShort();
        myInfo.ReadShort();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadShort();
        myInfo.ReadByte();
        myInfo.ReadInt();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadShort();
        myInfo.ReadLong();
        myInfo.ReadLong();
        myInfo.ReadInt();
        myInfo.ReadInt();
        myInfo.ReadShort().Should().Be(0);
        myInfo.ReadByte().Should().Be(0);
    }

    [Fact]
    public async Task GameStartCommand_SerializesClanBlockWithSingleByteClanNameAndAlignedMoney()
    {
        const short clanId = 77;
        const int money = 1234567;

        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "clan-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "clan-user").Id;
                db.Set<KnightsEntity>().Add(new KnightsEntity
                {
                    Id = clanId,
                    Name = "Devs",
                    Chief = "ClanUser",
                    Nation = (byte)AccountNation.Karus,
                    Flag = 1,
                    Members = 1,
                    Points = 0,
                    Grade = 0
                });
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "ClanUser",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    Money = money,
                    KnightsId = clanId,
                    Fame = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        provider.GetRequiredService<SessionManager>().Knights.AddClan(clanId, new KnightsEntity
        {
            Id = clanId,
            Name = "Devs",
            Chief = "ClanUser",
            Nation = (byte)AccountNation.Karus,
            Flag = 1,
            Members = 1,
            Points = 0,
            Grade = 0
        });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "clan-user");
        var characterId = await GetCharacterIdAsync(provider, "ClanUser");

        var packets = await preGameService.GameStartAsync(characterId, accountId, 1);
        var myInfo = packets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MYINFO);

        myInfo.ResetOffset();
        myInfo.ReadInt().Should().Be(characterId);
        myInfo.ReadSByteString().Should().Be("ClanUser");
        myInfo.ReadShort();
        myInfo.ReadShort();
        myInfo.ReadShort();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadShort();
        myInfo.ReadByte();
        myInfo.ReadInt();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadByte();
        myInfo.ReadShort();
        myInfo.ReadLong();
        myInfo.ReadLong();
        myInfo.ReadInt();
        myInfo.ReadInt();
        myInfo.ReadShort().Should().Be(clanId);
        myInfo.ReadByte().Should().Be(1);
        myInfo.ReadShort().Should().Be(0);
        myInfo.ReadByte().Should().Be(1);
        myInfo.ReadSByteString().Should().Be("Devs");
        myInfo.ReadByte().Should().Be(5);
        myInfo.ReadByte().Should().Be(0);
        myInfo.ReadShort().Should().Be(0);
        myInfo.ReadShort().Should().Be(-1);
        myInfo.ReadByte().Should().Be(0);
        myInfo.ReadByte().Should().Be(0);
        myInfo.ReadByte().Should().Be(0);
        myInfo.ReadByte().Should().Be(0);
        myInfo.ReadLong().Should().Be(0);
        myInfo.ReadShort();
        myInfo.ReadShort();
        myInfo.ReadShort();
        myInfo.ReadShort();
        myInfo.ReadInt();
        myInfo.ReadInt();
        for (var i = 0; i < 10; i++)
            myInfo.ReadByte();
        myInfo.ReadShort();
        myInfo.ReadShort();
        for (var i = 0; i < 6; i++)
            myInfo.ReadByte();
        myInfo.ReadInt().Should().Be(money);
    }

    [Fact]
    public async Task GameStartCommand_SerializesLeftHandItemIntoClientLeftHandSlot()
    {
        const int bowItemId = 160210001;
        var items = CreateInventory((InventoryConstants.LeftHand, bowItemId, 83));

        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "left-hand-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "left-hand-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Lefty",
                    Race = 1,
                    Class = 105,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    Items = items,
                    SkillPointData = new byte[9]
                });
            });

        var preGameService = provider.GetRequiredService<IPreGameService>();
        var accountId = await GetAccountIdAsync(provider, "left-hand-user");
        var characterId = await GetCharacterIdAsync(provider, "Lefty");

        var packets = await preGameService.GameStartAsync(characterId, accountId, 1);
        var myInfo = packets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MYINFO);
        var inventory = ReadMyInfoItems(myInfo);

        inventory[GameConstants.ItemSlots.LeftHand].Should().Be((bowItemId, (short)83, (short)1));
        inventory[GameConstants.ItemSlots.Waist].Should().Be((0, (short)0, (short)0));
    }

    [Fact]
    public async Task GameSessionInitializer_LoadsWarehouseFromAccountStorage()
    {
        const int warehouseItemId = 900001234;

        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "warehouse-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "warehouse-user").Id;
                db.Warehouses.Add(new Warehouse
                {
                    AccountId = accountId,
                    Money = 54321,
                    Items = CreateWarehouse((0, warehouseItemId, 33, 7))
                });
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "WarehouseOwner",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 80,
                    MapId = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var accountId = await GetAccountIdAsync(provider, "warehouse-user");
        var characterId = await GetCharacterIdAsync(provider, "WarehouseOwner");

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);

        var initializer = provider.GetRequiredService<IGameSessionInitializer>();
        var session = await initializer.InitializeAsync(client);

        session.Should().NotBeNull();
        session!.WarehouseMoney.Should().Be(54321);
        session.Warehouse[0].ItemId.Should().Be(warehouseItemId);
        session.Warehouse[0].Durability.Should().Be(33);
        session.Warehouse[0].Count.Should().Be(7);
    }

    [Fact]
    public async Task GameSessionInitializer_RevivesSavedDeadCharacterAndPersistsRecoveredHp()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "revive-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "revive-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "ReviveMe",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 0,
                    Mp = 80,
                    MapId = 21,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var accountId = await GetAccountIdAsync(provider, "revive-user");
        var characterId = await GetCharacterIdAsync(provider, "ReviveMe");

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);

        var initializer = provider.GetRequiredService<IGameSessionInitializer>();
        var session = await initializer.InitializeAsync(client);

        session.Should().NotBeNull();
        session!.MaxHp.Should().BePositive();
        session.Hp.Should().Be(session.MaxHp);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = await db.Characters.SingleAsync(entry => entry.Id == characterId);

        character.Hp.Should().Be(session.MaxHp);
    }

    [Fact]
    public async Task GameSessionInitializer_RelocatesUnsupportedReconnectZoneAndPersistsSafeSpawn()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "tower-revive-user",
                    Password = "pw",
                    Nation = AccountNation.ElMorad,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "tower-revive-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "TowerRescue",
                    Race = 12,
                    Class = 201,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 80,
                    MapId = 18,
                    X = 389,
                    Z = 1591,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            },
            gameData =>
            {
                gameData.GetCoefficient(201).Returns(CreateBasicCoefficient(201));
                gameData.GetStartPosition((short)21).Returns(new StartPositionData
                {
                    ZoneId = 21,
                    KarusX = 10,
                    KarusZ = 20,
                    ElmoradX = 817,
                    ElmoradZ = 527,
                    RangeX = 0,
                    RangeZ = 0
                });
            });

        var accountId = await GetAccountIdAsync(provider, "tower-revive-user");
        var characterId = await GetCharacterIdAsync(provider, "TowerRescue");

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);

        var initializer = provider.GetRequiredService<IGameSessionInitializer>();
        var session = await initializer.InitializeAsync(client);

        session.Should().NotBeNull();
        session!.ZoneId.Should().Be((byte)CharacterReconnectZoneRepair.SafeReconnectZoneId);
        session.X.Should().Be(818);
        session.Z.Should().Be(628);
        session.Y.Should().Be(0);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = await db.Characters.SingleAsync(entry => entry.Id == characterId);

        character.MapId.Should().Be((byte)CharacterReconnectZoneRepair.SafeReconnectZoneId);
        character.X.Should().Be(818);
        character.Z.Should().Be(628);
        character.Y.Should().Be(0);
    }

    [Fact]
    public async Task CharacterStatePersister_SaveAsync_PersistsWarehouseToAccountStorage()
    {
        const int warehouseItemId = 900001235;

        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "persist-warehouse-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "persist-warehouse-user").Id;
                db.Warehouses.Add(new Warehouse
                {
                    AccountId = accountId,
                    Money = 100
                });
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "WarehouseSaver",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 80,
                    MapId = 1,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var accountId = await GetAccountIdAsync(provider, "persist-warehouse-user");
        var characterId = await GetCharacterIdAsync(provider, "WarehouseSaver");

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);

        var initializer = provider.GetRequiredService<IGameSessionInitializer>();
        var session = await initializer.InitializeAsync(client);
        session.Should().NotBeNull();

        session!.WarehouseMoney = 6789;
        session.Warehouse[0].ItemId = warehouseItemId;
        session.Warehouse[0].Durability = 44;
        session.Warehouse[0].Count = 2;

        var persister = provider.GetRequiredService<ICharacterStatePersister>();
        (await persister.SaveAsync(session)).Should().BeTrue();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var warehouse = await db.Warehouses.SingleAsync(entry => entry.AccountId == accountId);

        warehouse.Money.Should().Be(6789);
        BitConverter.ToInt32(warehouse.Items, 0).Should().Be(warehouseItemId);
        BitConverter.ToInt16(warehouse.Items, 4).Should().Be(44);
        BitConverter.ToUInt16(warehouse.Items, 6).Should().Be(2);
    }

    [Fact]
    public async Task CharacterStatePersister_SetOnlineStateAsync_UpdatesLastOnlineTimeWhenGoingOffline()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "persist-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(a => a.Login == "persist-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Persisted",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    IsOnline = true,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var characterId = await GetCharacterIdAsync(provider, "Persisted");
        var persister = provider.GetRequiredService<ICharacterStatePersister>();
        var before = DateTime.UtcNow.AddSeconds(-1);

        await persister.SetOnlineStateAsync(characterId, isOnline: false);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = await db.Characters.SingleAsync(c => c.Id == characterId);

        character.IsOnline.Should().BeFalse();
        character.LastOnlineTime.Should().NotBeNull();
        character.LastOnlineTime.Should().BeOnOrAfter(before);
    }

}
