using LibreKO.Login;
using LibreKO.Login.Seed;
using LibreKO.Login.Startup;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Logging;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using LibreKO.Login.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

TranceConsoleUi.Initialize("Libre ● Login Server", 110, 36);
TranceConsoleUi.PrintBanner("LOGIN SERVER", "v2.6.19", "By Zeus x Design by Ahmad");
TranceConsoleUi.PrintDualCards(
    "SYSTEM METRICS",
    [
        ("Host", "0.0.0.0"),
        ("Port", "15100 (+10)"),
        ("Database", "MariaDB (XAMPP 3306)"),
        ("Environment", "Production")
    ],
    "AUTHENTICATION CONFIG",
    [
        ("Version", "2619"),
        ("Auto-Create Account", "ENABLED"),
        ("Max Connections / IP", "10"),
        ("Status", "ONLINE (Ready)")
    ]
);
TranceConsoleUi.PrintSectionHeader("INITIALIZATION & AUTH PIPELINE", 88);
TranceConsoleUi.PrintStatusLine("BOOT", ConsoleColor.Cyan, "Initializing database & auth services (silent mode: only errors displayed)...");

var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? Environments.Production;

var bootstrapConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

Log.Logger = SerilogSetup.CreateLogger(
    AppContext.BaseDirectory,
    bootstrapConfig["Logging:FileLevel"] ?? bootstrapConfig["Logging:LogLevel:Default"],
    bootstrapConfig["Logging:ConsoleLevel"],
    ParseLogRetentionDays(bootstrapConfig["Logging:RetentionDays"]),
    ErrorTrackingOptions.FromConfiguration(
        bootstrapConfig["Sentry:Dsn"],
        bootstrapConfig["Sentry:Release"],
        environmentName,
        "libreko.login"));

SerilogHostLogging.CaptureUnhandledExceptions();

var builder = Host.CreateDefaultBuilder(args)
    .UseSerilog()
    .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace))
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        cfg.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
        cfg.AddEnvironmentVariables();
        cfg.AddCommandLine(args);
    })
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<LoginServerSettings>(ctx.Configuration.GetSection(LoginServerSettings.SectionName));
        var connectionString = ctx.Configuration.GetConnectionString("Default")!;
        var serverVersion = ServerVersion.AutoDetect(connectionString);
        services.AddDbContext<AppDbContext>(o => o.UseMySql(
            connectionString,
            serverVersion,
            mysql => mysql.EnableRetryOnFailure(maxRetryCount: 3)));
        services.AddMemoryCache();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ILoginService, LoginService>();

        services.AddSingleton<IPatchRepository, PatchRepository>();
        services.AddSingleton<IServerRepository, ServerRepository>();
        services.AddSingleton<IKingRepository, KingRepository>();
        services.AddSingleton<IClientFactory>(sp =>
            new ClientFactory(ServerType.Login, sp.GetRequiredService<ILogger<Client>>()));
        services.AddSingleton<IPacketHandler, LoginPacketHandler>();

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<LoginServerSettings>>().Value;
            var clientFactory = sp.GetRequiredService<IClientFactory>();
            var handler = sp.GetRequiredService<IPacketHandler>();
            var logger = sp.GetRequiredService<ILogger<SocketServer>>();

            logger.LogInformation("Starting Login Server on {Host}:{Port}+10", settings.BindHost, settings.BindPort);

            return new SocketServer(settings.BindHost, settings.BindPort, extraPorts: 10, clientFactory, handler, logger,
                maxConnectionsPerIp: settings.Connections.MaxConnectionsPerIp,
                connectionRateWindowSeconds: settings.Connections.ConnectionRateWindowSeconds,
                maxConnectionAttemptsPerWindow: settings.Connections.MaxConnectionAttemptsPerWindow);
        });

        services.AddScoped<IDataSeeder, DataSeeder>();
        services.AddHostedService<PatchHttpServer>();
        services.AddHostedService(sp => sp.GetRequiredService<SocketServer>());
    })
    .Build();

// Apply migrations and seed server list
using (var scope = builder.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<IDataSeeder>();
    await seeder.SeedEntityAsync(new ServerGroupSeed());
    await seeder.SeedEntityAsync(new ServerSeed());
}

var socketServer = builder.Services.GetRequiredService<SocketServer>();
socketServer.OnReady = () =>
{
    TranceConsoleUi.PrintServerReadyBadge(
        "LOGIN SERVER",
        [
            ("Status", "ONLINE & ACCEPTING CONNECTIONS"),
            ("Network", "TCP Port 15100 - 15110 (Auth Sockets Active)"),
            ("Patch Server", "HTTP Port 15150 (Mobile Auto-Patch Active)"),
            ("Version", "Client v2619 Accepted  •  Auto-Registration ON"),
            ("Engine", "Libre Core v2.6.19  •  By Zeus x Design by Ahmad")
        ]
    );
    TranceConsoleUi.PrintSectionHeader("LIVE AUTHENTICATION LOG STREAM", 88);
};

try
{
    await builder.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}

static int ParseLogRetentionDays(string? configuredValue)
{
    return int.TryParse(configuredValue, out var days) && days > 0
        ? days
        : 10;
}
