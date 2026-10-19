namespace LibreKO;

public static class Game
{
    public static GameSession Session { get; private set; } = null!;

    public static GameRequests Requests { get; private set; } = null!;

    public static IGameConnection Connection { get; private set; } = null!;

    public static ILoginConnection LoginServer { get; private set; } = null!;

    internal static void Boot(GameSession session, GameRequests requests, IGameConnection connection)
    {
        Session = session;
        Requests = requests;
        Connection = connection;
    }

    internal static void AttachLoginServer(ILoginConnection loginServer) => LoginServer = loginServer;
}
