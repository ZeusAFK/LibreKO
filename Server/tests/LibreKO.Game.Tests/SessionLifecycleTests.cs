using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LibreKO.Game.Tests;

public class SessionLifecycleTests : GameTestBase
{
    private const string Login = "lifecycle";
    private const string First = "First";
    private const string Second = "Second";
    private const byte FirstStep = 1;
    private const byte SecondStep = 2;
    private const byte GetLanguage = ClientSettingsPacketCoordinator.SubGetLanguage;
    private const byte SetLanguage = ClientSettingsPacketCoordinator.SubSetLanguage;

    [Fact]
    public async Task EachStepOfTheGameStartRunsOncePerLogin()
    {
        using var provider = Provider();
        var (client, sent) = await SignedInAsync(provider, First);

        await GameStart(provider, client, FirstStep);
        await GameStart(provider, client, FirstStep);
        var session = provider.GetRequiredService<SessionManager>().GetByClientId(client.Id)!;
        await GameStart(provider, client, SecondStep);
        session.BlinkUntilTicks = 0;
        await GameStart(provider, client, SecondStep);

        sent.Count(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MYINFO).Should().Be(1);
        session.BlinkUntilTicks.Should().Be(0, "a second start would arm the respawn blink again");
    }

    [Fact]
    public async Task ARepeatedSecondStepDoesNotWriteTheCharacterAgain()
    {
        using var provider = Provider();
        var (client, _) = await SignedInAsync(provider, First);
        await GameStart(provider, client, FirstStep);
        await GameStart(provider, client, SecondStep);
        await SetOnlineAsync(provider, First, online: false);

        await GameStart(provider, client, SecondStep);

        (await StoredAsync(provider, First)).IsOnline.Should().BeFalse("a step 2 in flight during a kick would write an old row over the final save");
    }

    [Fact]
    public async Task AGameStartWhoseAccountWasLostWhileLoadingLeavesNoSession()
    {
        var initializer = Substitute.For<IGameSessionInitializer>();
        using var provider = CreateProvider(SeedAccount, configureServices: services => services.AddScoped(_ => initializer));
        var (client, _) = await SignedInAsync(provider, First);
        var sessions = provider.GetRequiredService<SessionManager>();
        initializer.InitializeAsync(client).Returns(async _ =>
        {
            await provider.GetRequiredService<IAccountLockService>().ReleaseAsync(client);
            return sessions.CreateSession(client, client.CharacterId, client.AccountId);
        });

        await GameStart(provider, client, FirstStep);

        sessions.GetByClientId(client.Id).Should().BeNull("a kick that landed while the character loaded leaves no session behind");
    }

    [Fact]
    public async Task ACloseFromAnotherConnectionWaitsForThePacketInProgress()
    {
        using var provider = Provider();
        var persister = Substitute.For<ICharacterStatePersister>();
        persister.SaveAndSealAsync(Arg.Any<UserSession>(), Arg.Any<CancellationToken>()).Returns(true);
        var termination = ActivatorUtilities.CreateInstance<SessionTerminationService>(provider, persister);
        var client = Client([]);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, 1, 1);
        session.Name = First;
        await session.PacketGate.WaitAsync();

        var takeover = termination.EvictForTakeoverAsync(session);
        var savedWhileThePacketRan = persister.ReceivedCalls().Any();
        session.PacketGate.Release();
        await takeover;

        savedWhileThePacketRan.Should().BeFalse("the last save comes after the packet it was handling");
        await persister.Received(1).SaveAndSealAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APacketThatWaitedPastTheStartOfACloseIsDropped()
    {
        using var provider = Provider();
        var (client, sent) = await SignedInAsync(provider, First);
        await GameStart(provider, client, FirstStep);
        var session = provider.GetRequiredService<SessionManager>().GetByClientId(client.Id)!;
        await session.PacketGate.WaitAsync();

        var ask = AskLanguage(provider, client);
        session.TryBeginClosing();
        session.PacketGate.Release();
        await ask;

        Languages(sent).Should().Be(0);
    }

    [Fact]
    public void AClientSaveIsAllowedAgainAfterTheCooldown()
    {
        var session = new UserSession(Client([]), 1, 1);
        const long start = 1_000;

        session.TryTakeClientSave(start).Should().BeTrue();
        session.TryTakeClientSave(start + UserSession.ClientSaveCooldownMs - 1).Should().BeFalse();
        session.TryTakeClientSave(start + UserSession.ClientSaveCooldownMs).Should().BeTrue();
    }

    [Fact]
    public async Task AConnectionThatLostItsAccountIsNoLongerHeard()
    {
        using var provider = Provider();
        var (client, sent) = await SignedInAsync(provider, First);
        await GameStart(provider, client, FirstStep);

        await AskLanguage(provider, client);
        var heardWhileHolding = Languages(sent);
        await provider.GetRequiredService<IAccountLockService>().ReleaseAsync(client);
        await AskLanguage(provider, client);

        heardWhileHolding.Should().Be(1);
        Languages(sent).Should().Be(1, "a kicked connection's packets are dropped");
    }

    [Fact]
    public async Task AClosingSessionIsNoLongerHeard()
    {
        using var provider = Provider();
        var (client, sent) = await SignedInAsync(provider, First);
        await GameStart(provider, client, FirstStep);
        provider.GetRequiredService<SessionManager>().GetByClientId(client.Id)!.TryBeginClosing();

        await AskLanguage(provider, client);

        Languages(sent).Should().Be(0);
    }

    [Fact]
    public async Task ACompressedPacketIsNotTakenBeforeSigningIn()
    {
        using var provider = Provider();
        var client = Client([]);
        var inner = new Packet(GameOpcodes.GS_LOGIN);
        inner.WriteString(Login);
        inner.WriteString("pw");

        await provider.GetRequiredService<IPacketHandler>().HandlePacket(client, Compressed(inner));

        provider.GetRequiredService<IAccountLockService>().Owns(client).Should().BeFalse();
    }

    [Fact]
    public async Task ACompressedPacketInsideAnotherIsDropped()
    {
        using var provider = Provider();
        var (client, sent) = await SignedInAsync(provider, First);
        await GameStart(provider, client, FirstStep);
        var ask = new Packet(GameOpcodes.GS_CLIENT_SETTINGS);
        ask.WriteByte(GetLanguage);

        await provider.GetRequiredService<IPacketHandler>().HandlePacket(client, Compressed(Compressed(ask)));
        var nested = Languages(sent);
        await provider.GetRequiredService<IPacketHandler>().HandlePacket(client, Compressed(ask));

        nested.Should().Be(0);
        Languages(sent).Should().Be(1, "one level of compression is still read");
    }

    [Fact]
    public async Task SelectingACharacterWaitsForTheAccountsEarlierSessionToBeSaved()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var accountId = await GetAccountIdAsync(provider, Login);
        var earlierClient = Client([]);
        var earlier = sessions.CreateSession(earlierClient, await GetCharacterIdAsync(provider, First), accountId);
        earlier.Name = First;
        const int earlierGold = 777;
        earlier.Money = earlierGold;
        var (client, _) = await SignedInAsync(provider, characterName: null);
        var select = new Packet(GameOpcodes.GS_SELECT_CHARACTER);
        select.WriteString(Login);
        select.WriteString(Second);
        select.WriteByte(FirstStep);
        select.ResetOffset();

        await provider.GetRequiredService<IPreGamePacketCoordinator>().HandleAsync(client, select, GameOpcodes.GS_SELECT_CHARACTER);

        sessions.GetByClientId(earlierClient.Id).Should().BeNull("the account's other character is taken out before this one loads");
        earlier.SavesSealed.Should().BeTrue();
        earlierClient.Received().Disconnect();
        await using var scope = provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<Common.Infrastructure.Persistence.AppDbContext>().Characters.FindAsync(earlier.CharacterId);
        stored!.Money.Should().Be(earlierGold);
    }

    [Fact]
    public async Task ALogoutAndADropMeetingCloseTheSessionOnce()
    {
        using var provider = Provider();
        var persister = Substitute.For<ICharacterStatePersister>();
        var saving = new TaskCompletionSource<bool>();
        persister.SaveAndSealAsync(Arg.Any<UserSession>(), Arg.Any<CancellationToken>()).Returns(saving.Task);
        var termination = ActivatorUtilities.CreateInstance<SessionTerminationService>(provider, persister);
        var client = Client([]);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, 1, 1);
        session.Name = First;

        var logout = termination.LogoutAsync(client);
        var drop = termination.DisconnectAsync(client);
        var dropWaitedForTheSave = !drop.IsCompleted;
        saving.SetResult(true);
        await Task.WhenAll(logout, drop);

        dropWaitedForTheSave.Should().BeTrue("the account is let go only after the one final save");
        await persister.Received(1).SaveAndSealAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATakeoverMeetingADropWaitsForTheDropsSave()
    {
        using var provider = Provider();
        var persister = Substitute.For<ICharacterStatePersister>();
        var saving = new TaskCompletionSource<bool>();
        persister.SaveAndSealAsync(Arg.Any<UserSession>(), Arg.Any<CancellationToken>()).Returns(saving.Task);
        var termination = ActivatorUtilities.CreateInstance<SessionTerminationService>(provider, persister);
        var client = Client([]);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, 1, 1);
        session.Name = First;

        var drop = termination.DisconnectAsync(client);
        var takeover = termination.EvictForTakeoverAsync(session);
        var takeoverWaited = !takeover.IsCompleted;
        saving.SetResult(true);
        await Task.WhenAll(drop, takeover);

        takeoverWaited.Should().BeTrue("the next session loads only after the dropped one is saved");
        await persister.Received(1).SaveAndSealAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheAccountIsLetGoEvenWhenTheDisconnectFails()
    {
        var termination = Substitute.For<ISessionTerminationService>();
        termination.DisconnectAsync(Arg.Any<IClient>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the database went away"));
        using var provider = CreateProvider(SeedAccount, configureServices: services => services.AddSingleton(termination));
        var (client, _) = await SignedInAsync(provider, characterName: null);

        var disconnect = () => provider.GetRequiredService<IPacketHandler>().OnClientDisconnected(client);

        await disconnect.Should().ThrowAsync<InvalidOperationException>();
        provider.GetRequiredService<IAccountLockService>().Owns(client).Should().BeFalse();
        (await provider.GetRequiredService<IAccountLockService>().AcquireAsync(Client([]), await GetAccountIdAsync(provider, Login)))
            .Granted.Should().BeTrue("a failed disconnect must not lock the account for good");
    }

    [Fact]
    public async Task TheFinalSaveRunsWhenLettingGoOfTheWorldFails()
    {
        using var provider = Provider();
        var persister = Substitute.For<ICharacterStatePersister>();
        persister.SaveAndSealAsync(Arg.Any<UserSession>(), Arg.Any<CancellationToken>()).Returns(true);
        var pets = Substitute.For<IPetService>();
        pets.DismissAsync(Arg.Any<UserSession>()).ThrowsAsync(new InvalidOperationException("the pet store went away"));
        var termination = ActivatorUtilities.CreateInstance<SessionTerminationService>(provider, persister, pets);
        var client = Client([]);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, 1, 1);
        session.Name = First;

        await termination.DisconnectAsync(client);

        await persister.Received(1).SaveAndSealAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALanguageChangeIsSavedAtMostOncePerCooldown()
    {
        using var provider = Provider();
        var persister = Substitute.For<ICharacterStatePersister>();
        var settings = ActivatorUtilities.CreateInstance<ClientSettingsPacketCoordinator>(provider, persister);
        var client = Client([]);
        provider.GetRequiredService<SessionManager>().CreateSession(client, 1, 1).Name = First;

        foreach (var language in new[] { GameLanguage.Spanish, GameLanguage.English, GameLanguage.Spanish })
        {
            var change = new Packet(GameOpcodes.GS_CLIENT_SETTINGS);
            change.WriteByte(SetLanguage);
            change.WriteByte((byte)language);
            change.ResetOffset();
            await settings.HandleAsync(client, change);
        }

        await persister.Received(1).SaveAsync(Arg.Any<UserSession>(), Arg.Any<CancellationToken>());
    }

    private static ServiceProvider Provider() => CreateProvider(SeedAccount);

    private static void SeedAccount(Common.Infrastructure.Persistence.AppDbContext db)
    {
        db.Accounts.Add(new Account { Login = Login, Password = "pw", Nation = AccountNation.Karus });
        db.SaveChanges();
        var accountId = db.Accounts.Single(account => account.Login == Login).Id;
        db.Characters.AddRange(
            Character(accountId, First, slot: 0),
            Character(accountId, Second, slot: 1));
    }

    private static Character Character(int accountId, string name, byte slot) => new()
    {
        AccountId = accountId, Name = name, Slot = slot, Race = 1, Class = 101, Level = 10, Hp = 100, Mp = 100,
        MapId = 21, X = 10, Z = 20, Items = new byte[InventoryConstants.InventoryTotal * 8], SkillPointData = new byte[9],
    };

    private static IClient Client(List<Packet> sent)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.IsConnected.Returns(true);
        client.IsCryptoEnabled.Returns(true);
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(ClonePacket(packet))), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return client;
    }

    private static async Task<(IClient Client, List<Packet> Sent)> SignedInAsync(ServiceProvider provider, string? characterName)
    {
        var sent = new List<Packet>();
        var client = Client(sent);
        var accountId = await GetAccountIdAsync(provider, Login);
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterName == null ? 0 : await GetCharacterIdAsync(provider, characterName));
        (await provider.GetRequiredService<IAccountLockService>().AcquireAsync(client, accountId)).Granted.Should().BeTrue();
        return (client, sent);
    }

    private static async Task<Character> StoredAsync(ServiceProvider provider, string name)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Common.Infrastructure.Persistence.AppDbContext>().Characters
            .AsNoTracking().SingleAsync(character => character.Name == name);
    }

    private static async Task SetOnlineAsync(ServiceProvider provider, string name, bool online)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Common.Infrastructure.Persistence.AppDbContext>();
        var character = await db.Characters.SingleAsync(row => row.Name == name);
        character.IsOnline = online;
        await db.SaveChangesAsync();
    }

    private static Task GameStart(ServiceProvider provider, IClient client, byte step)
    {
        var request = new Packet(GameOpcodes.GS_GAMESTART);
        request.WriteByte(step);
        request.ResetOffset();
        return provider.GetRequiredService<IPacketHandler>().HandlePacket(client, request);
    }

    private static Task AskLanguage(ServiceProvider provider, IClient client)
    {
        var ask = new Packet(GameOpcodes.GS_CLIENT_SETTINGS);
        ask.WriteByte(GetLanguage);
        ask.ResetOffset();
        return provider.GetRequiredService<IPacketHandler>().HandlePacket(client, ask);
    }

    private static int Languages(List<Packet> sent) =>
        sent.Count(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CLIENT_SETTINGS);

    private static Packet Compressed(Packet inner)
    {
        var body = new byte[1 + inner.GetLength()];
        body[0] = inner.GetOpcode();
        inner.GetData().AsSpan(0, inner.GetLength()).CopyTo(body.AsSpan(1));
        var squeezed = new byte[body.Length * 2 + 16];
        var length = Lzf.Compress(body, body.Length, squeezed, squeezed.Length);
        var packet = new Packet(GameOpcodes.GS_COMPRESS_PACKET);
        packet.WriteInt(length);
        packet.WriteInt(body.Length);
        packet.WriteUInt(Crc32.Compute(body));
        packet.WriteBytes(squeezed.AsSpan(0, length).ToArray());
        packet.ResetOffset();
        return packet;
    }
}
