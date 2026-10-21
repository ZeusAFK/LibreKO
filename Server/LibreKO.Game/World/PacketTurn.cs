namespace LibreKO.Game.World;

// The session whose packet this flow is handling; a close from that flow does not wait for itself.
public static class PacketTurn
{
    private static readonly AsyncLocal<UserSession?> current = new();

    public static UserSession? Current => current.Value;

    public static IDisposable Of(UserSession session)
    {
        var previous = current.Value;
        current.Value = session;
        return new Restore(previous);
    }

    private sealed class Restore(UserSession? previous) : IDisposable
    {
        public void Dispose() => current.Value = previous;
    }
}
