namespace LibreKO.Common.Infrastructure.Network;

public sealed class ClientDisconnectedException(Exception inner) : IOException("Disconnected", inner);
