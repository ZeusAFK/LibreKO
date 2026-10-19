using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Common.Infrastructure.Network;

public class SocketServer(
    string bindHost, int port, int extraPorts,
    IClientFactory clientFactory,
    IPacketHandler packetHandler,
    ILogger<SocketServer> logger,
    int maxConnectionsPerIp = 10,
    int connectionRateWindowSeconds = 10,
    int maxConnectionAttemptsPerWindow = 20,
    bool exemptLoopback = false
) : IHostedService
{
    private const int AcceptRetryBaseDelayMs = 50;
    private const int AcceptRetryMaxDelayMs = 5_000;
    private const int AcceptRetryMaxBackoffShift = 7;
    private const int RateEntryStaleWindowMultiplier = 6;
    private static readonly TimeSpan RateSweepInterval = TimeSpan.FromMinutes(1);

    private readonly IPAddress bindAddress = bindHost == "*" ? IPAddress.Any : IPAddress.Parse(bindHost);
    private readonly int startPort = port;
    private readonly int? endPort = port + extraPorts;
    private readonly List<TcpListener> listeners = [];
    private readonly List<Task> acceptTasks = [];
    private readonly ConcurrentDictionary<Guid, IClient> clients = new();
    private CancellationTokenSource cts = new();
    private Task? sweepTask;

    private readonly ConcurrentDictionary<IPAddress, int> connectionsPerIp = new();
    private readonly ConcurrentDictionary<IPAddress, RateEntry> connectionRate = new();

    private sealed class RateEntry
    {
        public int Count;
        public long WindowStart;
        public readonly Lock Sync = new();
    }

    public IReadOnlyCollection<IClient> GetConnectedClients() => [.. clients.Values];

    public IClient? FindClient(Guid clientId) => clients.TryGetValue(clientId, out var client) ? client : null;

    public IClient? FindClientBySocket(Socket socket) => clients.Values.FirstOrDefault(c => c.Socket == socket);

    public IClient? FindClientByEndPoint(EndPoint endPoint) => clients.Values.FirstOrDefault(c => c.Socket.RemoteEndPoint?.Equals(endPoint) == true);

    public int ConnectedClientCount => clients.Count;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var actualEnd = endPort ?? startPort;
        cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sweepTask = SweepRateEntriesAsync(cts.Token);

        try
        {
            for (var p = startPort; p <= actualEnd; p++)
            {
                try
                {
                    var listener = new TcpListener(bindAddress, p);
                    listener.Start();
                    listeners.Add(listener);
                    acceptTasks.Add(AcceptLoopAsync(listener, cts.Token));
                }
                catch (SocketException ex)
                {
                    logger.LogError(ex, "Failed to start listener on port {Port}", p);
                }
            }

            if (listeners.Count == 0)
            {
                logger.LogError("No listeners could be started. Stopping server.");
                return;
            }

            logger.LogInformation("Accepting connections on {BindHost}:{StartPort}{Range}", bindHost, startPort, actualEnd == startPort ? string.Empty : $"-{actualEnd}");

            await Task.WhenAll(acceptTasks);

        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Socket server is stopping.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in socket server.");
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        var consecutiveFailures = 0;

        while (!ct.IsCancellationRequested)
        {
            Socket socket;

            try
            {
                socket = await listener.AcceptSocketAsync(ct);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                var backoffMs = Math.Min(
                    AcceptRetryMaxDelayMs,
                    AcceptRetryBaseDelayMs << Math.Min(consecutiveFailures - 1, AcceptRetryMaxBackoffShift));

                logger.LogError(ex, "Accept failed on {LocalEndPoint} ({Failures} consecutive); retrying in {BackoffMs}ms",
                    listener.Server.LocalEndPoint, consecutiveFailures, backoffMs);

                try { await Task.Delay(backoffMs, ct); }
                catch (OperationCanceledException) { break; }

                continue;
            }

            IPAddress? countedIp = null;

            try
            {
                var remote = socket.RemoteEndPoint?.ToString() ?? "unknown";

                if (socket.RemoteEndPoint is IPEndPoint remoteEp && !(exemptLoopback && IPAddress.IsLoopback(remoteEp.Address)))
                {
                    var ip = remoteEp.Address;

                    if (IsRateLimited(ip))
                    {
                        logger.LogWarning("Rate limited connection from {Ip}", ip);
                        socket.Close();
                        continue;
                    }

                    var held = connectionsPerIp.AddOrUpdate(ip, 1, (_, c) => c + 1);
                    if (held > maxConnectionsPerIp)
                    {
                        ReleaseConnection(ip);
                        logger.LogWarning("Max connections reached for {Ip} ({Count})", ip, maxConnectionsPerIp);
                        socket.Close();
                        continue;
                    }

                    countedIp = ip;
                }

                socket.NoDelay = true;

                var client = clientFactory.Create(socket);
                clients.TryAdd(client.Id, client);

                logger.LogTrace("Client connected: {RemoteEndPoint}", remote);
                _ = Task.Run(() => HandleClientAsync(client, countedIp, remote, ct), ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialise accepted connection on {LocalEndPoint}", listener.Server.LocalEndPoint);

                if (countedIp is not null)
                    ReleaseConnection(countedIp);

                try { socket.Close(); }
                catch (Exception closeEx) { logger.LogDebug(closeEx, "Error closing rejected socket"); }
            }
        }

        logger.LogInformation("Accept loop stopped on {LocalEndPoint}", listener.Server.LocalEndPoint);
    }

    private static bool IsTransportClose(Exception ex) =>
        ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException;

    private static bool IsProtocolFault(Exception ex) => ex is InvalidDataException;

    private void ReleaseConnection(IPAddress ip)
    {
        var remaining = connectionsPerIp.AddOrUpdate(ip, 0, (_, c) => Math.Max(0, c - 1));
        if (remaining == 0)
            connectionsPerIp.TryRemove(new KeyValuePair<IPAddress, int>(ip, 0));
    }

    private async Task SweepRateEntriesAsync(CancellationToken ct)
    {
        var staleAfterMs = connectionRateWindowSeconds * 1000L * RateEntryStaleWindowMultiplier;

        try
        {
            using var timer = new PeriodicTimer(RateSweepInterval);

            while (await timer.WaitForNextTickAsync(ct))
            {
                var now = Environment.TickCount64;
                var evicted = 0;

                foreach (var (ip, entry) in connectionRate)
                {
                    lock (entry.Sync)
                    {
                        if (now - entry.WindowStart <= staleAfterMs)
                            continue;

                        if (connectionRate.TryRemove(new KeyValuePair<IPAddress, RateEntry>(ip, entry)))
                            evicted++;
                    }
                }

                if (evicted > 0)
                    logger.LogDebug("Evicted {Evicted} stale rate-limit entries, {Tracked} still tracked", evicted, connectionRate.Count);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool IsRateLimited(IPAddress ip)
    {
        var now = Environment.TickCount64;
        var windowMs = connectionRateWindowSeconds * 1000L;

        var entry = connectionRate.GetOrAdd(ip, _ => new RateEntry { Count = 0, WindowStart = now });

        lock (entry.Sync)
        {
            if (now - entry.WindowStart > windowMs)
            {
                entry.Count = 0;
                entry.WindowStart = now;
            }

            entry.Count++;
            return entry.Count > maxConnectionAttemptsPerWindow;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await cts.CancelAsync();

        if (sweepTask is not null)
        {
            try { await sweepTask; }
            catch (Exception ex) { logger.LogDebug(ex, "Rate-limit sweep ended with an error"); }
            sweepTask = null;
        }

        foreach (var client in clients.Values)
        {
            try
            {
                client.Disconnect();
                if (client is IDisposable disposableClient)
                {
                    disposableClient.Dispose();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error disconnecting client {ClientId}", client.Id);
            }
        }
        clients.Clear();

        foreach (var l in listeners)
            l.Stop();
    }

    private async Task HandleClientAsync(IClient client, IPAddress? countedIp, string remote, CancellationToken ct)
    {
        // Socket.RemoteEndPoint throws once disposed, and anything escaping this detached task skips the cleanup.
        try
        {
            while (!ct.IsCancellationRequested && client.Socket.Connected)
            {
                try
                {
                    var packet = await client.ReceivePacket(ct);
                    await packetHandler.HandlePacket(client, packet);
                }
                catch (Exception ex)
                {
                    if (client.ExpectedClose)
                        logger.LogDebug("Client {Id} closed after logout: {RemoteEndPoint}", client.Id, remote);
                    else if (IsTransportClose(ex))
                        logger.LogInformation("Client {Id} disconnected: {RemoteEndPoint}, Reason: {Reason}", client.Id, remote, ex.Message);
                    else if (IsProtocolFault(ex))
                        logger.LogWarning("Client {Id} ({RemoteEndPoint}) sent malformed data: {Reason}", client.Id, remote, ex.Message);
                    else
                        logger.LogError(ex, "Client {Id} ({RemoteEndPoint}) dropped by a failing packet handler", client.Id, remote);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error serving client {Id} ({RemoteEndPoint})", client.Id, remote);
        }
        finally
        {
            if (countedIp != null)
                ReleaseConnection(countedIp);

            clients.TryRemove(client.Id, out _);

            try { await packetHandler.OnClientDisconnected(client); }
            catch (Exception ex) { logger.LogWarning(ex, "Error in disconnect handler for client {Id}", client.Id); }

            try
            {
                client.Disconnect();
                if (client is IDisposable disposableClient)
                    disposableClient.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error closing client {Id}", client.Id);
            }
        }
    }
}
