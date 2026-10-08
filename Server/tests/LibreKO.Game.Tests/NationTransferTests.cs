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
    private const short KarusRogue = 108;
    private const short KarusPriest = 111;
    private const byte Failed = 0;
    private const byte IsKing = 3;
    private const byte Moradon = (byte)ZoneId.Moradon;
    private const float MoradonTownX = 816f;
    private const float MoradonTownZ = 532f;
    private const int NameLengthOffset = 5;
    private const int FirstSlotOffset = 3;
    private const int SecondSlotOffset = 18;

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

    private ServiceProvider Provider(short clanOfSecond = 0, Action<IServiceCollection>? configureServices = null) => CreateProvider(
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
        }), configureServices: configureServices);

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

    private static Packet Raw(byte[] bytes)
    {
        var packet = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        packet.WriteBytes(bytes);
        packet.ResetOffset();
        return packet;
    }

    private static Packet ToKarus() => SubmitPacket((byte)CharacterRace.KarusTuarek, (byte)CharacterRace.KarusPuriTuarek);

    [Fact]
    public async Task EveryTruncatedSubmissionLeavesTheAccountAndCertificateUntouched()
    {
        using var provider = Provider();
        var (session, sent, client) = await Online(provider, carriesItem: true);
        var service = provider.GetRequiredService<INationTransferService>();
        var bytes = ToKarus().GetData();
        for (var length = 0; length < bytes.Length; length++)
        {
            await service.HandleAsync(client, Raw(bytes[..length]));
            session.Nation.Should().Be(AccountNation.ElMorad, $"prefix length {length}");
            session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        }
        sent.Where(p => p.GetOpcode() == (byte)GameOpcodes.GS_NATION_TRANSFER)
            .Should().NotContain(p => p.GetData().SequenceEqual(new byte[] { Submit, Accepted }));
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Accounts.Single().Nation.Should().Be(AccountNation.ElMorad);
        db.Characters.Single(c => c.Name == "Healer").Class.Should().Be(ElMoradPriest);
    }

    [Theory]
    [InlineData("negative-name-length")]
    [InlineData("oversized-name")]
    [InlineData("empty-name")]
    [InlineData("negative-slot")]
    [InlineData("duplicate-slot")]
    [InlineData("trailing-data")]
    [InlineData("zero-count")]
    public async Task MalformedSubmissionsCannotSpendTheCertificate(string malformed)
    {
        using var provider = Provider();
        var (session, sent, client) = await Online(provider, carriesItem: true);
        var bytes = ToKarus().GetData();
        switch (malformed)
        {
            case "negative-name-length": bytes[NameLengthOffset] = bytes[NameLengthOffset + 1] = byte.MaxValue; break;
            case "oversized-name": bytes[NameLengthOffset] = byte.MaxValue; break;
            case "empty-name": bytes[NameLengthOffset] = bytes[NameLengthOffset + 1] = 0; break;
            case "negative-slot": bytes[FirstSlotOffset] = bytes[FirstSlotOffset + 1] = byte.MaxValue; break;
            case "duplicate-slot": bytes[SecondSlotOffset] = bytes[SecondSlotOffset + 1] = 0; break;
            case "trailing-data": bytes = [.. bytes, 0]; break;
            case "zero-count": bytes = [Submit, Accepted, 0]; break;
        }
        await provider.GetRequiredService<INationTransferService>().HandleAsync(client, Raw(bytes));
        Last(sent).GetData().Should().Equal(Submit, WrongCharacter);
        session.Nation.Should().Be(AccountNation.ElMorad);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
    }

    [Fact]
    public async Task AFailedCertificateConsumptionCannotChangeAnyCharacter()
    {
        var usage = Substitute.For<IMagicItemUsageService>();
        usage.CanUseItem(Arg.Any<UserSession>(), TransferItem, 1).Returns(true);
        usage.TryConsumeItemAsync(Arg.Any<UserSession>(), TransferItem, 1).Returns(false);
        using var provider = Provider(configureServices: services => services.AddSingleton(usage));
        var (session, sent, client) = await Online(provider, carriesItem: true);

        await provider.GetRequiredService<INationTransferService>().HandleAsync(client, ToKarus());

        Last(sent).GetData().Should().Equal(Submit, NoItem);
        session.Nation.Should().Be(AccountNation.ElMorad);
        session.Class.Should().Be(ElMoradRogue);
        provider.GetRequiredService<SessionManager>().GetByClientId(client.Id).Should().BeSameAs(session);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Accounts.Single().Nation.Should().Be(AccountNation.ElMorad);
        db.Characters.Single(c => c.Name == "Healer").Class.Should().Be(ElMoradPriest);
    }

    [Theory]
    [InlineData("dead")]
    [InlineData("trade")]
    [InlineData("merchant")]
    [InlineData("merchant-preparing")]
    [InlineData("gathering")]
    public async Task IncompatibleActivitiesCannotOpenOrSubmitTransfer(string activity)
    {
        using var provider = Provider();
        var (session, sent, client) = await Online(provider, carriesItem: true);
        switch (activity)
        {
            case "dead": session.Hp = 0; break;
            case "trade": session.Trade.ExchangeUser = 999; break;
            case "merchant": session.Trade.MerchantState = MerchantMode.Selling; break;
            case "merchant-preparing": session.Trade.IsSellingMerchantPreparing = true; break;
            case "gathering": session.IsMining = true; break;
        }
        var service = provider.GetRequiredService<INationTransferService>();

        await service.OpenAsync(session);
        Last(sent).GetData().Should().Equal(OpenBox, Failed);
        await service.HandleAsync(client, ToKarus());

        Last(sent).GetData().Should().Equal(Submit, Failed);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        session.Nation.Should().Be(AccountNation.ElMorad);
    }

    private sealed class TransferSaveProbe(bool fail = false) : SaveChangesInterceptor
    {
        public int MigrationSaves { get; private set; }
        public int MigratedCharacters { get; private set; }
        public bool ConsumedCertificate { get; private set; }
        public Func<Task>? BeforeMigrationSave { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = (AppDbContext)eventData.Context!;
            db.ChangeTracker.DetectChanges();
            if (!db.ChangeTracker.Entries<Account>().Any(entry => entry.State == EntityState.Modified
                && entry.Property(account => account.Nation).OriginalValue == AccountNation.ElMorad
                && entry.Entity.Nation == AccountNation.Karus))
                return result;
            MigrationSaves++;
            var characters = db.ChangeTracker.Entries<Character>().Where(entry => entry.State == EntityState.Modified).ToList();
            MigratedCharacters = characters.Count;
            characters.Should().OnlyContain(entry => ClassIdHelper.GetNation(entry.Entity.Class) == AccountNation.Karus
                && entry.Entity.MapId == Moradon && entry.Entity.Bind == -1);
            var active = characters.Single(entry => entry.Entity.Name == "Rover").Entity;
            var slots = Enumerable.Range(0, InventoryConstants.InventoryTotal).Select(_ => new ItemSlot()).ToArray();
            UserSessionBinaryState.LoadItems(slots, active.Items);
            ConsumedCertificate = slots.All(item => item.ItemId != TransferItem);
            if (fail)
                throw new InvalidOperationException("Injected migration save failure");
            if (BeforeMigrationSave != null)
                await BeforeMigrationSave();
            return result;
        }
    }

    private ServiceProvider ProbedProvider(TransferSaveProbe probe) =>
        Provider(configureServices: services => services.AddDbContext<AppDbContext>(options => options.AddInterceptors(probe)));

    [Fact]
    public async Task SuccessIsSentOnlyAfterOneCompleteMigrationSaveAndLogout()
    {
        var probe = new TransferSaveProbe();
        using var provider = ProbedProvider(probe);
        var (session, sent, client) = await Online(provider, carriesItem: true);
        const int money = 456789;
        session.Money = money;
        var observedSuccess = false;
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var packet = call.Arg<Packet>();
            sent.Add(packet);
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_NATION_TRANSFER
                || !packet.GetData().SequenceEqual(new byte[] { Submit, Accepted }))
                return Task.CompletedTask;
            observedSuccess = true;
            probe.MigrationSaves.Should().Be(1);
            probe.MigratedCharacters.Should().Be(2);
            probe.ConsumedCertificate.Should().BeTrue();
            provider.GetRequiredService<SessionManager>().GetByClientId(client.Id).Should().BeNull();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Accounts.Single().Nation.Should().Be(AccountNation.Karus);
            var active = db.Characters.Single(character => character.Name == "Rover");
            active.MapId.Should().Be(Moradon, "logout cannot restore the departure map");
            active.X.Should().Be(MoradonTownX);
            active.Z.Should().Be(MoradonTownZ);
            active.Money.Should().Be(money);
            active.Race.Should().Be((byte)CharacterRace.KarusTuarek);
            active.Class.Should().Be(KarusRogue);
            return Task.CompletedTask;
        });

        await provider.GetRequiredService<INationTransferService>().HandleAsync(client, ToKarus());

        observedSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AFailedMigrationSaveRestoresTheExactCertificateAndKeepsTheAccountOnline()
    {
        var probe = new TransferSaveProbe(fail: true);
        using var provider = ProbedProvider(probe);
        var (session, sent, client) = await Online(provider, carriesItem: true);
        var certificate = session.Inventory[InventoryConstants.SlotMax];
        certificate.Flag = (byte)ItemFlag.Bound;
        certificate.UniqueId = 1245;
        certificate.ExpiresAt = 123456789;
        var before = session.SerializeItems();

        await provider.GetRequiredService<INationTransferService>().HandleAsync(client, ToKarus());

        Last(sent).GetData().Should().Equal(Submit, Failed);
        probe.MigrationSaves.Should().Be(1);
        session.SerializeItems().Should().Equal(before);
        session.Nation.Should().Be(AccountNation.ElMorad);
        session.Class.Should().Be(ElMoradRogue);
        session.NationTransferCommitted.Should().BeFalse();
        provider.GetRequiredService<SessionManager>().GetByClientId(client.Id).Should().BeSameAs(session);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Accounts.Single().Nation.Should().Be(AccountNation.ElMorad);
        db.Characters.Single(character => character.Name == "Rover").Class.Should().Be(ElMoradRogue);
        db.Characters.Single(character => character.Name == "Healer").Class.Should().Be(ElMoradPriest);
        (await provider.GetRequiredService<ICharacterStatePersister>().SaveAsync(session)).Should().BeTrue();
    }

    [Fact]
    public async Task AutosaveCannotWriteTheOldDestinationAcrossAnAccountMigration()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new TransferSaveProbe { BeforeMigrationSave = () => { entered.SetResult(); return release.Task; } };
        using var provider = ProbedProvider(probe);
        var (session, _, client) = await Online(provider, carriesItem: true);
        var wait = TimeSpan.FromSeconds(10);

        var transfer = provider.GetRequiredService<INationTransferService>().HandleAsync(client, ToKarus());
        await entered.Task.WaitAsync(wait);
        var autosave = provider.GetRequiredService<ICharacterStatePersister>().SaveAsync(session);
        autosave.IsCompleted.Should().BeFalse("the migration owns the character persistence gate");
        release.SetResult();
        await Task.WhenAll(transfer, autosave).WaitAsync(wait);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = db.Characters.Single(c => c.Name == "Rover");
        character.MapId.Should().Be(Moradon);
        character.X.Should().Be(MoradonTownX);
        character.Z.Should().Be(MoradonTownZ);
        character.Class.Should().Be(KarusRogue);
        db.Accounts.Single().Nation.Should().Be(AccountNation.Karus);
    }

    [Fact]
    public async Task ACurrentKingOnAnyAccountCharacterRefusesTheWholeTransfer()
    {
        var kings = Substitute.For<IKingSystemRuntimeService>();
        kings.GetKingData(AccountNation.ElMorad).Returns(new KingSystemData { KingName = "healer" });
        using var provider = Provider(configureServices: services => services.AddSingleton(kings));
        var (session, sent, client) = await Online(provider, carriesItem: true);
        var service = provider.GetRequiredService<INationTransferService>();

        await service.OpenAsync(session);
        Last(sent).GetData().Should().Equal(OpenBox, IsKing);
        await service.HandleAsync(client, ToKarus());

        Last(sent).GetData().Should().Equal(Submit, IsKing);
        session.Nation.Should().Be(AccountNation.ElMorad);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
    }

    [Fact]
    public async Task AWholeKarusAccountMovesToElMoradWithSeparateSavedLooks()
    {
        using var provider = Provider();
        using (var setup = provider.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Accounts.Single().Nation = AccountNation.Karus;
            var rogue = db.Characters.Single(c => c.Name == "Rover");
            rogue.Class = KarusRogue;
            rogue.Race = (byte)CharacterRace.KarusTuarek;
            var priest = db.Characters.Single(c => c.Name == "Healer");
            priest.Class = KarusPriest;
            priest.Race = (byte)CharacterRace.KarusPuriTuarek;
            await db.SaveChangesAsync();
        }
        var (session, sent, client) = await Online(provider, carriesItem: true);
        session.Nation = AccountNation.Karus;
        session.Class = KarusRogue;
        session.Race = (byte)CharacterRace.KarusTuarek;

        await provider.GetRequiredService<INationTransferService>().HandleAsync(client,
            SubmitPacket((byte)CharacterRace.ElMoradMale, (byte)CharacterRace.ElMoradFemale));

        Last(sent).GetData().Should().Equal(Submit, Accepted);
        session.Nation.Should().Be(AccountNation.ElMorad);
        session.Class.Should().Be(ElMoradRogue);
        session.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeTrue();
        using var scope = provider.CreateScope();
        var saved = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        saved.Accounts.Single().Nation.Should().Be(AccountNation.ElMorad);
        var healer = saved.Characters.Single(c => c.Name == "Healer");
        healer.Class.Should().Be(ElMoradPriest);
        healer.Race.Should().Be((byte)CharacterRace.ElMoradFemale);
        healer.Face.Should().Be(5);
        healer.Hair.Should().Be(0x02_405060);
        healer.MapId.Should().Be(Moradon);
    }
}
