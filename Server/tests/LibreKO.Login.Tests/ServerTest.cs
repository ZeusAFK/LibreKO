using LibreKO.Common;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Login.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace LibreKO.Login.Tests;

public abstract class ServerTest : IDisposable
{
    private readonly InMemoryDatabaseRoot _dbRoot = new();
    private readonly string _dbName = $"LibreKO_Test_{Guid.NewGuid()}";

    protected readonly IHost host;
    protected readonly SocketServer Server;
    protected readonly LoginServerSettings Settings;

    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    protected ServerTest(int? port = null)
    {
        var bindPort = port ?? GetFreeTcpPort();
        Settings = new LoginServerSettings
        {
            BindHost = "127.0.0.1",
            BindPort = bindPort,
            Account = new AccountSettings
            {
                AutoCreate = true
            }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LoginServerSettings.SectionName}:BindHost"] = Settings.BindHost,
                [$"{LoginServerSettings.SectionName}:BindPort"] = Settings.BindPort.ToString(),
                [$"{LoginServerSettings.SectionName}:Version"] = Settings.Version.ToString(),
                [$"{LoginServerSettings.SectionName}:Account:AutoCreate"] = Settings.Account.AutoCreate.ToString(),
            })
            .Build();

        host = Host.CreateDefaultBuilder()
            .ConfigureLogging((_, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddConfiguration(configuration);
            })
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<LoginServerSettings>(ctx.Configuration.GetSection(LoginServerSettings.SectionName));
                services.AddDbContext<AppDbContext>(o => o
                    .UseInMemoryDatabase(_dbName, _dbRoot)
                    .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
                services.AddMemoryCache();


                services.AddScoped<IAccountRepository, AccountRepository>();
                services.AddScoped<ILoginService, LoginService>();

                services.AddSingleton<IServerRepository, ServerRepository>();
                services.AddSingleton<IKingRepository, KingRepository>();
                services.AddSingleton<IPatchRepository, PatchRepository>();
                services.AddSingleton<IClientFactory>(sp =>
                    new ClientFactory(ServerType.Login, sp.GetRequiredService<ILogger<Client>>()));
                services.AddSingleton<IPacketHandler, LoginPacketHandler>();

                ConfigureServices(ctx, services);
            })
            .Build();

        var sp = host.Services;
        var clientFactory = sp.GetRequiredService<IClientFactory>();
        var packetHandler = sp.GetRequiredService<IPacketHandler>();
        var logger = sp.GetRequiredService<ILogger<SocketServer>>();

        Server = new SocketServer(Settings.BindHost, Settings.BindPort, extraPorts: 0, clientFactory, packetHandler, logger);

        InitializeDatabase();
    }

    protected virtual void ConfigureServices(HostBuilderContext ctx, IServiceCollection services) { }

    protected virtual void SeedDatabase(AppDbContext context) { }

    private void InitializeDatabase()
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
        SeedDatabase(context);
    }

    protected async Task<SocketServer> StartServerAsync()
    {
        if (_serverTask == null)
        {
            _serverTask = Task.Run(async () =>
            {
                try { await Server.StartAsync(_cts.Token); }
                catch (OperationCanceledException) { }
            });
            await Task.Delay(100);
        }
        return Server;
    }

    protected Task<(TcpClient client, NetworkStream stream)> ConnectAsync() => ConnectAsync(Settings.BindHost, Settings.BindPort);

    protected static async Task<(TcpClient client, NetworkStream stream)> ConnectAsync(string host, int port)
    {
        var tcpClient = new TcpClient(AddressFamily.InterNetwork);
        await tcpClient.ConnectAsync(IPAddress.Parse(host), port);
        return (tcpClient, tcpClient.GetStream());
    }

    protected static async Task<string> WriteHexAsync(NetworkStream stream, string hex, CancellationToken ct = default)
    {
        var bytes = Convert.FromHexString(hex);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
        return await ReadHexAsync(stream, 1024, ct);
    }

    protected static async Task<string> ReadHexAsync(NetworkStream stream, int bufferSize = 1024, CancellationToken ct = default)
    {
        var buffer = new byte[bufferSize];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);

        await Task.Delay(300, ct);

        return Convert.ToHexString(buffer.AsSpan(0, read));
    }

    protected static int GetFreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _serverTask?.Wait(TimeSpan.FromSeconds(5)); } catch { }
        host.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
