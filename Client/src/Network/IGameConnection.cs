namespace LibreKO.Network;

public interface IGameConnection
{
    bool Connected { get; }

    int PingMs { get; }

    int ServerVersion { get; }

    string ServerHost { get; }

    ReconnectSupervisor Reconnect { get; }

    bool WorldActive { get; set; }

    Func<bool>? WindowCloseHandler { get; set; }

    event Action? ConnectedEvent;

    event Action<string>? ErrorEvent;

    event Action<int>? VersionEvent;

    void BeginGameLogin(string host, int port, string account, string password);

    void ConnectToServer(string host, int port);

    void Disconnect(bool expected = false);

    void DropConnection(string reason);

    void ReturnToCharSelect();

    void AcknowledgeWorldLoaded();

    void RebuildWorldInPlace(float x, float z, float y);

    void Receive(Packet packet);
}
