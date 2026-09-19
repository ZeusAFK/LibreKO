using LibreKO.Game;
using LibreKO.Game.World;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Logging;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

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
        "libreko.game"));

SerilogHostLogging.CaptureUnhandledExceptions();

// Raise the thread-pool floor before any clients connect. The pool otherwise grows
// only ~1-2 threads/sec, so a surge of thousands of concurrent send/receive
// continuations stalls (manifests as the server "freezing" updates) until it catches up.
var configuredMinThreads = int.TryParse(bootstrapConfig["GameServer:Global:MinWorkerThreads"], out var cfgMin) ? cfgMin : 0;
var minThreads = configuredMinThreads > 0 ? configuredMinThreads : Math.Max(256, Environment.ProcessorCount * 16);
ThreadPool.SetMinThreads(minThreads, minThreads);
Log.Information("Thread-pool minimum threads set to {MinThreads}", minThreads);

var builder = Host.CreateDefaultBuilder(args)
    .UseSerilog()
    .ConfigureLogging(logging => logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace))
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        cfg.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
        cfg.AddEnvironmentVariables();
        cfg.AddCommandLine(args);
    })
    .ConfigureServices((ctx, services) =>
    {
        services.AddOptions<GameServerSettings>()
            .Bind(ctx.Configuration.GetSection(GameServerSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        var connectionString = ctx.Configuration.GetConnectionString("Default")!;
        var serverVersion = ServerVersion.AutoDetect(connectionString);
        services.AddDbContext<AppDbContext>(o => o.UseMySql(
            connectionString,
            serverVersion,
            mysql => mysql.EnableRetryOnFailure(maxRetryCount: 3)));
        services.AddMemoryCache();

        services.AddGameDataAccess();
        services.AddPreGameServices();
        services.AddGameDomainServices();
        services.AddProtocolCoordinators();
        services.AddWorldServices();
        services.AddGameSocketServer();
        services.AddGameHostedServices();
    })
    .Build();

// Apply migrations and seed game data
using (var scope = builder.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var hostEnvironment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    if (hostEnvironment.IsDevelopment()
        && Directory.Exists(Path.Combine(hostEnvironment.ContentRootPath, "Seed", "Data")))
        SeedDataLocation.Root = hostEnvironment.ContentRootPath;

    var seedRunner = scope.ServiceProvider.GetRequiredService<GameDataSeedRunner>();
    var gameServerSettings = scope.ServiceProvider.GetRequiredService<IOptions<GameServerSettings>>().Value;
    await seedRunner.SeedAllAsync(gameServerSettings.Seeding.Force);

    var bootstrapper = scope.ServiceProvider.GetRequiredService<IGameServerBootstrapper>();
    await bootstrapper.InitializeAsync();
}

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
