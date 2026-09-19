using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Login.Enums;
using LibreKO.Login.Protocol.Writers;

namespace LibreKO.Login.Tests;

public class LoginTests : ServerTest
{
    protected override void SeedDatabase(AppDbContext db)
    {
        var testAccount = new Account
        {
            Login = "user",
            Password = "password",
            Nation = AccountNation.None,
            Authority = AccountAuthority.Normal,
            PremiumDate = DateTime.UtcNow.AddHours(12)
        };
        db.Accounts.Add(testAccount);
        db.ServerGroups.Add(new ServerGroup { Id = 7, Name = "Beramus" });
        db.KingSystem.Add(new KingSystemData
        {
            Nation = (byte)AccountNation.Karus,
            KingName = "Darckchito",
            Notice = "Welcome to the Adonisian Continent"
        });
        db.KingSystem.Add(new KingSystemData
        {
            Nation = (byte)AccountNation.ElMorad,
            KingName = "WarriorComing",
            Notice = "Long live El Morad"
        });
        db.SaveChanges();

        db.Servers.Add(new Server
        {
            Name = "Beramus 1",
            GroupId = 7,
            Category = ServerCategory.Event,
            IpAddress = "127.0.0.1",
            LanIpAddress = "10.0.0.1",
            Port = 15001,
            OnlinePlayers = 0,
            MaxPlayers = 100,
            FreePlayerCap = 80,
            Status = "Online"
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task LS_VERSION_REQ_ReturnsServerVersion()
    {
        // Arrange
        await StartServerAsync();
        var (_, stream) = await ConnectAsync();
        var packet = new Packet(LoginOpcodes.LS_VERSION_REQ);
        var packetHex = Convert.ToHexString(PacketProvider.WrapPacket(packet, asClient: true));

        // Act
        var responseHex = await WriteHexAsync(stream, packetHex);

        // Assert
        var loginPacket = new Packet(LoginOpcodes.LS_VERSION_REQ);
        loginPacket.WriteShort((short)Settings.Version);
        var loginPacketHex = Convert.ToHexString(PacketProvider.WrapPacket(loginPacket));
        responseHex.Should().Be(loginPacketHex);
    }

    [Theory]
    [InlineData("user", "password", LoginResult.Success, 11)]
    [InlineData("user", "wrong_password", LoginResult.InvalidPassword, 0)]
    [InlineData("not_user", "password", LoginResult.Success, LoginPacketWriter.NoPremium)]
    public async Task LS_LOGIN_ReturnsCorrectResponse(string userName, string password, LoginResult resultCode, short premiumRemaining)
    {
        // Arrange
        await StartServerAsync();
        var (_, stream) = await ConnectAsync();

        // Act
        var loginPacket = new Packet(LoginOpcodes.LS_LOGIN);
        loginPacket.WriteString(userName);
        loginPacket.WriteString(password);
        var loginHex = Convert.ToHexString(PacketProvider.WrapPacket(loginPacket, asClient: true));
        var response = await WriteHexAsync(stream, loginHex);

        // Parse login response
        var responseStream = new MemoryStream(Convert.FromHexString(response));
        var responsePacket = await PacketProvider.ReadFromStream(responseStream, CancellationToken.None);

        // Assert — uint16(0) + uint8(result) [+ premiumHours + account on success]
        responsePacket.GetOpcode().Should().Be((byte)LoginOpcodes.LS_LOGIN);
        responsePacket.ReadShort().Should().Be(0);
        responsePacket.ReadByte().Should().Be((byte)resultCode);
        if (resultCode == LoginResult.Success)
        {
            responsePacket.ReadShort().Should().Be(premiumRemaining,
                "a fresh account has no premium, and zero would render as its last day");
            responsePacket.ReadString().Should().Be(userName);
        }
    }

    [Fact]
    public async Task LS_LOGIN_AfterCryption_UsesSeedProtectedPackets()
    {
        await StartServerAsync();
        var (_, stream) = await ConnectAsync();

        var cryptionPacket = new Packet(LoginOpcodes.LS_CRYPTION);
        var cryptionHex = Convert.ToHexString(PacketProvider.WrapPacket(cryptionPacket, asClient: true));
        var cryptionResponse = await WriteHexAsync(stream, cryptionHex);

        var cryptionStream = new MemoryStream(Convert.FromHexString(cryptionResponse));
        var cryptionResponsePacket = await PacketProvider.ReadFromStream(cryptionStream, CancellationToken.None);
        cryptionResponsePacket.GetOpcode().Should().Be((byte)LoginOpcodes.LS_CRYPTION);
        var seedLength = cryptionResponsePacket.ReadByte();
        seedLength.Should().Be(8);
        var seed = cryptionResponsePacket.ReadBytes(seedLength);

        var loginPacket = new Packet(LoginOpcodes.LS_LOGIN);
        loginPacket.WriteString("user");
        loginPacket.WriteString("password");
        loginPacket.WriteByte(0);
        loginPacket.WriteUInt(0);

        var protectedPayload = LoginSeedCipher.Protect(loginPacket.GetBytes(), seed);
        var protectedPacket = new Packet(protectedPayload[0]);
        protectedPacket.WriteBytes(protectedPayload[1..]);

        var response = await WriteHexAsync(stream, Convert.ToHexString(PacketProvider.WrapPacket(protectedPacket, asClient: true)));

        var responseStream = new MemoryStream(Convert.FromHexString(response));
        var responsePacket = await PacketProvider.ReadFromStream(responseStream, CancellationToken.None);
        responsePacket = PacketProvider.UnwrapLoginSeedPacket(responsePacket, seed);

        responsePacket.GetOpcode().Should().Be((byte)LoginOpcodes.LS_LOGIN);
        responsePacket.ReadShort().Should().Be(0);
        responsePacket.ReadByte().Should().Be((byte)LoginResult.Success);
        responsePacket.ReadShort().Should().Be(11);
        responsePacket.ReadString().Should().Be("user");
    }

    [Fact]
    public async Task LS_SERVERLIST_2239_IncludesLanIpFirstAndScreenType()
    {
        await StartServerAsync();
        var (_, stream) = await ConnectAsync();

        var request = new Packet(LoginOpcodes.LS_SERVERLIST);
        request.WriteShort(0);
        var response = await WriteHexAsync(stream, Convert.ToHexString(PacketProvider.WrapPacket(request, asClient: true)));

        var responseStream = new MemoryStream(Convert.FromHexString(response));
        var responsePacket = await PacketProvider.ReadFromStream(responseStream, CancellationToken.None);

        responsePacket.GetOpcode().Should().Be((byte)LoginOpcodes.LS_SERVERLIST);
        responsePacket.ReadShort().Should().Be(0);
        responsePacket.ReadByte().Should().Be(1);
        responsePacket.ReadString().Should().Be("127.0.0.1",
            "the client dials the first address, so it must be the routable one");
        responsePacket.ReadString().Should().Be("10.0.0.1");
        responsePacket.ReadString().Should().Be("Beramus|Beramus 1",
            "the group and the name are stored apart and joined only on the wire");
        responsePacket.ReadShort().Should().Be(0);
        responsePacket.ReadShort().Should().Be(1);
        responsePacket.ReadShort().Should().Be(7, "the group id comes from the group, not a constant");
        responsePacket.ReadShort().Should().Be(100);
        responsePacket.ReadShort().Should().Be(80, "free accounts get their own cap");
        responsePacket.ReadByte().Should().Be(0);
        responsePacket.ReadByte().Should().Be((byte)ServerCategory.Event,
            "the category byte is what draws the event banner");
        responsePacket.ReadString().Should().Be("Darckchito");
        responsePacket.ReadString().Should().Be("Welcome to the Adonisian Continent");
        responsePacket.ReadString().Should().Be("WarriorComing");
        responsePacket.ReadString().Should().Be("Long live El Morad");

        responsePacket.ReadUInt().Should().Be(GameplayProtocol.ServerListMagic);
        responsePacket.ReadByte().Should().Be(GameplayProtocol.ExtensionVersion);
        responsePacket.ReadByte().Should().Be(1);
        responsePacket.ReadShort().Should().Be(1);
        responsePacket.ReadInt().Should().Be(15001);
        responsePacket.RemainingBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(null, LoginPacketWriter.NoPremium)]
    [InlineData(-5, LoginPacketWriter.NoPremium)]
    [InlineData(12, (short)12)]
    [InlineData(456, (short)456)]
    public void LoginSucceeded_SendsMinusOneWhenThereIsNoPremiumLeft(int? premiumHoursFromNow, short expected)
    {
        var account = new Account
        {
            Login = "premium-user",
            PremiumDate = premiumHoursFromNow is { } hours
                ? DateTime.UtcNow.AddHours(hours).AddMinutes(1)
                : null
        };

        var packet = LoginPacketWriter.LoginSucceeded(
            LoginOpcodes.LS_LOGIN, LoginResult.Success, account.RemainingPremiumHours, account.Login);

        packet.ReadUShort();
        packet.ReadByte();
        packet.ReadShort().Should().Be(expected,
            "the client treats any value of zero or above as active premium, so zero renders as "
            + "\"today is the last day\" for an account that has none");
    }

    [Fact]
    public void RemainingPremiumHours_ClampsAVeryLongPremiumInsteadOfOverflowing()
    {
        var account = new Account { PremiumDate = DateTime.UtcNow.AddYears(10) };

        account.RemainingPremiumHours.Should().Be(short.MaxValue,
            "an overflowing cast would wrap to a negative value and read as no premium at all");
    }

    [Fact]
    public async Task LS_NEWS_SendsATitleAndBodyWithNothingBetweenThem()
    {
        await StartServerAsync();
        var (_, stream) = await ConnectAsync();

        var request = new Packet(LoginOpcodes.LS_NEWS);
        var response = await WriteHexAsync(stream, Convert.ToHexString(PacketProvider.WrapPacket(request, asClient: true)));

        var responseStream = new MemoryStream(Convert.FromHexString(response));
        var responsePacket = await PacketProvider.ReadFromStream(responseStream, CancellationToken.None);

        responsePacket.GetOpcode().Should().Be((byte)LoginOpcodes.LS_NEWS);
        responsePacket.ReadString().Length.Should().BeLessThan(
            LoginPacketWriter.NewsTitleDisconnects,
            "a title of exactly that length tells the client to drop the connection");
        responsePacket.ReadString().Should().NotBeNull(
            "a padding byte between the two strings shifts the body length and loses the notice");
        responsePacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void LoginOccupied_PutsEveryRetailFieldAheadOfOurExtension()
    {
        var server = new Server
        {
            Id = 3,
            Name = "Beramus 1",
            IpAddress = "203.0.113.7",
            LanIpAddress = "10.0.0.1",
            Port = 15001
        };

        var packet = LoginPacketWriter.LoginOccupied(
            LoginOpcodes.LS_LOGIN, LoginResult.AlreadyInGame, server);

        packet.ReadString().Should().BeEmpty();
        packet.ReadByte().Should().Be((byte)LoginResult.AlreadyInGame);
        packet.ReadString().Should().Be("203.0.113.7");
        packet.ReadUShort().Should().Be(15001);
        packet.ReadUInt().Should().Be(3u);
        packet.ReadString().Should().Be("Beramus 1",
            "the client reads a length-prefixed name here, so anything else makes it read a "
            + "garbage length and lose the rest of the packet");

        packet.ReadUInt().Should().Be(GameplayProtocol.AccountLockMagic,
            "our extension has to sit past the last field the client reads");
        packet.ReadByte().Should().Be(GameplayProtocol.ExtensionVersion);
        packet.ReadInt().Should().Be(15001);
        packet.ReadString().Should().Be("10.0.0.1");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void DownloadInfo_LengthPrefixesEveryString()
    {
        var packet = LoginPacketWriter.DownloadInfo("patch.example.com", "/live", ["a.zip", "b.zip"]);

        packet.ReadString().Should().Be("patch.example.com");
        packet.ReadString().Should().Be("/live");
        packet.ReadShort().Should().Be(2);
        packet.ReadString().Should().Be("a.zip");
        packet.ReadString().Should().Be("b.zip");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void SocketList_AnswersWithNoExtraConnections()
    {
        var packet = LoginPacketWriter.SocketList();

        packet.GetOpcode().Should().Be((byte)LoginOpcodes.LS_SOCKET_LIST);
        packet.ReadShort().Should().Be(LoginPacketWriter.NoExtraSockets,
            "every entry in this list makes the client open another socket");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void LauncherNews_CountsTheNoticesAndPairsTextWithItsLink()
    {
        var packet = LoginPacketWriter.LauncherNews(
        [
            new LoginPacketWriter.LauncherNotice("Server merge on Friday", "https://example.com/merge"),
            new LoginPacketWriter.LauncherNotice("Maintenance finished", string.Empty)
        ]);

        packet.ReadShort().Should().Be(2);
        packet.ReadString().Should().Be("Server merge on Friday");
        packet.ReadString().Should().Be("https://example.com/merge");
        packet.ReadString().Should().Be("Maintenance finished");
        packet.ReadString().Should().BeEmpty();
        packet.RemainingBytes.Should().Be(0);
    }
}
