using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace LibreKO.Common.Infrastructure.Network;

public interface IClientFactory
{
    IClient Create(Socket socket);
}

public class ClientFactory(ServerType serverType, ILogger<Client> logger) : IClientFactory
{
    public IClient Create(Socket socket)
    {
        return new Client(socket, serverType, logger);
    }
}
