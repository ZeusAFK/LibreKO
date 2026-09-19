using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibreKO.Common.Tests;

public class ClientTests
{
    [Fact]
    public async Task SendPacket_AfterDispose_DoesNotThrow()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var tcpClient = new TcpClient(AddressFamily.InterNetwork);
        var connectTask = tcpClient.ConnectAsync(endpoint.Address, endpoint.Port);
        using var serverSocket = await listener.AcceptSocketAsync();
        await connectTask;

        using var client = new Client(serverSocket, ServerType.Game, NullLogger<Client>.Instance);
        client.Dispose();

        var packet = new Packet(0x01);
        packet.WriteByte(0x02);

        var act = async () => await client.SendPacket(packet);

        await act.Should().NotThrowAsync();

        listener.Stop();
    }
}
