using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class LoginWireTests
{
    private const int DefaultPort = 15001;
    private const int ExtensionPort = 15011;
    private const short FirstId = 1;
    private const short SecondId = 2;

    private static void WriteServer(Packet p, string name, short id)
    {
        p.WriteString("10.0.0.1");
        p.WriteString("192.168.0.1");
        p.WriteString(name);
        p.WriteShort(12);
        p.WriteShort(id);
        p.WriteShort(0);
        p.WriteShort(500);
        p.WriteShort(0);
        p.WriteByte(0);
        p.WriteByte(0);
        for (int i = 0; i < 4; i++) p.WriteString("");
    }

    [Fact]
    public void TheServerListSplitsTheGroupAndTakesTheExtensionPort()
    {
        var p = new Packet(LoginOpcodes.LS_SERVERLIST);
        p.WriteShort(1);
        p.WriteByte(2);
        WriteServer(p, "Europe|Ronark", FirstId);
        WriteServer(p, "Moradon", SecondId);
        p.WriteUInt(LibreKOProtocol.ServerListMagic);
        p.WriteByte(LibreKOProtocol.ExtensionVersion);
        p.WriteByte(1);
        p.WriteShort(SecondId);
        p.WriteInt(ExtensionPort);
        p.ResetOffset();

        var list = LoginWire.ReadServerList(p, DefaultPort);

        Assert.Equal(2, list.Count);
        Assert.Equal(("Europe", "Ronark", DefaultPort), (list[0].Group, list[0].Name, list[0].Port));
        Assert.Equal(("", "Moradon", ExtensionPort), (list[1].Group, list[1].Name, list[1].Port));
        Assert.Equal("10.0.0.1", list[1].Address);
    }

    [Fact]
    public void AnOccupiedServerWithoutAHostFallsBackToTheDefaults()
    {
        var p = new Packet(LoginOpcodes.LS_LOGIN);
        p.WriteString("");
        p.WriteUShort(0);
        p.WriteUInt(7);
        p.WriteString("Ronark");
        p.ResetOffset();

        var occupied = LoginWire.ReadOccupiedServer(p, "127.0.0.1", DefaultPort);

        Assert.Equal(("127.0.0.1", DefaultPort, 7, "Ronark"), (occupied.Host, occupied.Port, occupied.ServerId, occupied.ServerName));
    }
}
