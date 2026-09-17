namespace LibreKO.Common.Infrastructure.Network;

public interface IPacketHandler
{
    Task HandlePacket(IClient client, Packet packet);
    Task OnClientDisconnected(IClient client) => Task.CompletedTask;
}
