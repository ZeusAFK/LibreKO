using LibreKO.Game.World;
using LibreKO.Common.Enums;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class SessionPacketWriter
{
    public const byte LoginRejected = byte.MaxValue;
    public const byte LoginFollowUpOpcode = 0xC0;
    public const byte NoCryptionKey = 0;

    private const byte LoginPadding = 6;

    public static Packet Pong(byte[] echo)
    {
        var packet = new Packet(GameOpcodes.GS_PING);
        if (echo.Length > 0)
            packet.WriteBytes(echo);
        return packet;
    }

    public static Packet LoginDenied()
    {
        var packet = new Packet(GameOpcodes.GS_LOGIN);
        packet.WriteByte(LoginRejected);
        return packet;
    }

    public static Packet LoginDeniedAccountInUse(AccountOccupant occupant)
    {
        var packet = LoginDenied();
        packet.WriteUInt(GameplayProtocol.AccountLockMagic);
        packet.WriteByte(GameplayProtocol.ExtensionVersion);
        packet.WriteByte((byte)GameLoginDenial.AccountInUse);
        packet.WriteByte((byte)occupant.Stage);
        packet.WriteShort((short)occupant.ServerId);
        packet.WriteString(occupant.ServerName);
        packet.WriteString(occupant.CharacterName);
        return packet;
    }

    public static Packet LoginAccepted(byte nation)
    {
        var packet = new Packet(GameOpcodes.GS_LOGIN);
        packet.WriteByte(nation);
        packet.WriteInt(0);
        packet.WriteByte(LoginPadding);
        return packet;
    }

    public static Packet LoginFollowUp()
    {
        var packet = new Packet(LoginFollowUpOpcode);
        packet.WriteByte(1);
        packet.WriteByte(2);
        packet.WriteByte(1);
        return packet;
    }

    public static Packet KickResult(AccountKickCode code)
    {
        var packet = new Packet(GameOpcodes.GS_KICKOUT);
        packet.WriteByte((byte)code);
        return packet;
    }

    public static Packet VersionCheck(short version)
    {
        var packet = new Packet(GameOpcodes.GS_VERSION_CHECK);
        packet.WriteByte((byte)GameServerMode.Normal);
        packet.WriteShort(version);
        packet.WriteByte(NoCryptionKey);
        packet.WriteByte((byte)GameServerState.Open);
        return packet;
    }
}
