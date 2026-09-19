using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibreKO.Common.Tests;

public class SocketServerTests
{
    private sealed class NoopPacketHandler : IPacketHandler
    {
        public Task HandlePacket(IClient client, Packet packet) => Task.CompletedTask;
    }

    private sealed class RealClientFactory : IClientFactory
    {
        public IClient Create(Socket socket) => new Client(socket, ServerType.Game, NullLogger<Client>.Instance);
    }

    private sealed class ThrowOnceClientFactory : IClientFactory
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public IClient Create(Socket socket)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
                throw new InvalidOperationException("simulated factory failure");

            return new Client(socket, ServerType.Game, NullLogger<Client>.Instance);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
                _entries.Add((logLevel, formatter(state, exception)));
        }

        public IReadOnlyList<(LogLevel Level, string Message)> Snapshot()
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var chosen = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return chosen;
    }

    private static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return true;

            await Task.Delay(25);
        }

        return condition();
    }

    private static async Task<TcpClient> ConnectWithRetry(int port, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();

        while (true)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                return client;
            }
            catch (SocketException)
            {
                client.Dispose();
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw;

                await Task.Delay(25);
            }
        }
    }

    [Fact]
    public async Task AcceptLoop_SurvivesClientFactoryFailure()
    {
        var port = FreePort();
        var factory = new ThrowOnceClientFactory();
        var server = new SocketServer("127.0.0.1", port, 0, factory, new NoopPacketHandler(),
            NullLogger<SocketServer>.Instance, maxConnectionsPerIp: 50, maxConnectionAttemptsPerWindow: 500);

        _ = server.StartAsync(CancellationToken.None);

        using (var doomed = await ConnectWithRetry(port))
        {
            (await WaitFor(() => factory.Attempts >= 1)).Should().BeTrue();
        }

        using var survivor = await ConnectWithRetry(port);

        (await WaitFor(() => server.ConnectedClientCount == 1))
            .Should().BeTrue("the accept loop must keep serving after a failed connection setup");

        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PerIpCap_RejectsExcess_ThenReleasesSlotOnDisconnect()
    {
        var port = FreePort();
        var server = new SocketServer("127.0.0.1", port, 0, new RealClientFactory(), new NoopPacketHandler(),
            NullLogger<SocketServer>.Instance, maxConnectionsPerIp: 2, maxConnectionAttemptsPerWindow: 500);

        _ = server.StartAsync(CancellationToken.None);

        var first = await ConnectWithRetry(port);
        var second = await ConnectWithRetry(port);

        (await WaitFor(() => server.ConnectedClientCount == 2)).Should().BeTrue();

        using (var rejected = await ConnectWithRetry(port))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var buffer = new byte[1];
            var read = -1;

            try { read = await rejected.GetStream().ReadAsync(buffer, cts.Token); }
            catch (IOException) { read = 0; }

            read.Should().Be(0, "a capped connection is closed by the server");
        }

        server.ConnectedClientCount.Should().Be(2);

        first.Dispose();
        (await WaitFor(() => server.ConnectedClientCount == 1)).Should().BeTrue();

        using var replacement = await ConnectWithRetry(port);

        (await WaitFor(() => server.ConnectedClientCount == 2))
            .Should().BeTrue("the freed per-IP slot must be reusable");

        second.Dispose();
        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RateLimit_RejectsBurst_WithoutStoppingTheListener()
    {
        var port = FreePort();
        var server = new SocketServer("127.0.0.1", port, 0, new RealClientFactory(), new NoopPacketHandler(),
            NullLogger<SocketServer>.Instance, maxConnectionsPerIp: 200,
            connectionRateWindowSeconds: 60, maxConnectionAttemptsPerWindow: 3);

        _ = server.StartAsync(CancellationToken.None);

        var held = new List<TcpClient>();
        for (var i = 0; i < 12; i++)
            held.Add(await ConnectWithRetry(port));

        (await WaitFor(() => server.ConnectedClientCount == 3))
            .Should().BeTrue("only the attempts inside the window are served");

        await WaitFor(() => false, 250);

        server.ConnectedClientCount.Should().Be(3);

        foreach (var client in held)
            client.Dispose();

        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PlainDisconnect_IsNotReportedAsAnError()
    {
        var port = FreePort();
        var logger = new RecordingLogger<SocketServer>();
        var server = new SocketServer("127.0.0.1", port, 0, new RealClientFactory(), new NoopPacketHandler(),
            logger, maxConnectionsPerIp: 50, maxConnectionAttemptsPerWindow: 500);

        _ = server.StartAsync(CancellationToken.None);

        var probe = await ConnectWithRetry(port);
        (await WaitFor(() => server.ConnectedClientCount == 1)).Should().BeTrue();
        probe.Dispose();

        (await WaitFor(() => logger.Snapshot().Any(e => e.Message.Contains("disconnected"))))
            .Should().BeTrue("a peer that connects and closes must be reported as a disconnect");

        logger.Snapshot().Should().NotContain(e => e.Level == LogLevel.Error,
            "an uptime probe closing its connection is not a server error");

        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MalformedFraming_IsAClientFault_NotAServerError()
    {
        var port = FreePort();
        var logger = new RecordingLogger<SocketServer>();
        var server = new SocketServer("127.0.0.1", port, 0, new RealClientFactory(), new NoopPacketHandler(),
            logger, maxConnectionsPerIp: 50, maxConnectionAttemptsPerWindow: 500);

        _ = server.StartAsync(CancellationToken.None);

        using (var scanner = await ConnectWithRetry(port))
        {
            await scanner.GetStream().WriteAsync("GET / HTTP/1.1"u8.ToArray());

            (await WaitFor(() => logger.Snapshot().Any(e => e.Message.Contains("sent malformed data"))))
                .Should().BeTrue("a port scanner speaking HTTP must be logged as a client fault");
        }

        logger.Snapshot().Should().NotContain(e => e.Level == LogLevel.Error,
            "attacker-controlled bytes must never mint a server error event");

        await server.StopAsync(CancellationToken.None);
    }
}
