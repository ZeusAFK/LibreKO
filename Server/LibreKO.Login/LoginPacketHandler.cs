using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;
using Microsoft.Extensions.DependencyInjection;
using LibreKO.Login.Protocol.Writers;

namespace LibreKO.Login;

public class LoginPacketHandler(IServiceProvider serviceProvider) : IPacketHandler
{
    public async Task HandlePacket(IClient client, Packet packet)
    {
        var opcode = (LoginOpcodes)packet.GetOpcode();
        if (opcode == LoginOpcodes.LS_CRYPTION)
        {
            await HandleCryptoHandshakeAsync(client);
            return;
        }

        if (opcode == LoginOpcodes.LS_OTP)
            return;

        using var scope = serviceProvider.CreateScope();
        var loginService = scope.ServiceProvider.GetRequiredService<ILoginService>();

        var response = await DispatchCommand(loginService, opcode, packet);
        if (response != null)
        {
            await client.SendPacket(response);
        }
    }

    private static async Task HandleCryptoHandshakeAsync(IClient client)
    {
        var (keyBytes, _) = PacketCipher.GeneratePublicKey();
        var resp = LoginPacketWriter.Cryption(keyBytes);
        await client.SendPacket(resp);

        client.EnableLoginCrypto(keyBytes);
    }

    private static Task<Packet> DispatchCommand(ILoginService loginService, LoginOpcodes opcode, Packet packet)
    {
        return opcode switch
        {
            LoginOpcodes.LS_VERSION_REQ => loginService.VersionCheckAsync(),
            LoginOpcodes.LS_DOWNLOADINFO_REQ => loginService.DownloadInfoAsync(packet.ReadShort()),
            LoginOpcodes.LS_LOGIN => Login(loginService, packet, LoginOpcodes.LS_LOGIN),
            LoginOpcodes.LS_MGAME_LOGIN => Login(loginService, packet, LoginOpcodes.LS_MGAME_LOGIN),
            LoginOpcodes.LS_SERVERLIST => loginService.ServerListAsync(packet.ReadShort()),
            LoginOpcodes.LS_NEWS => loginService.NewsAsync(),
            LoginOpcodes.LS_UNKNOWN_F7 => loginService.UnknownF7Async(),
            LoginOpcodes.LS_LAUNCHER_NEWS => loginService.LauncherNewsAsync(),
            LoginOpcodes.LS_SOCKET_LIST => loginService.SocketListAsync(),
            _ => throw new NotSupportedException($"Unsupported opcode: {opcode}"),
        };
    }

    private static Task<Packet> Login(ILoginService loginService, Packet packet, LoginOpcodes responseOpcode)
    {
        var login = packet.ReadString();
        var password = packet.ReadString();
        return loginService.LoginAsync(login, password, responseOpcode, ReadLoginFlags(packet));
    }

    private static LoginRequestFlags ReadLoginFlags(Packet packet)
    {
        const int extensionBytes = 6;
        if (packet.RemainingBytes < extensionBytes
            || packet.ReadUInt() != GameplayProtocol.AccountLockMagic
            || packet.ReadByte() != GameplayProtocol.ExtensionVersion)
            return LoginRequestFlags.None;

        return (LoginRequestFlags)packet.ReadByte();
    }
}
