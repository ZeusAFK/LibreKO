using FluentAssertions;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Configuration;
using LibreKO.Game.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LibreKO.Game.Tests;

public class ServiceGraphTests
{
    [Fact]
    public void TheProductionServiceGraphResolvesEveryRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(TestHostEnvironmentFactory.Create());
        services.AddOptions<GameServerSettings>();
        services.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"ServiceGraph_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

        services.AddGameDataAccess();
        services.AddPreGameServices();
        services.AddGameDomainServices();
        services.AddProtocolCoordinators();
        services.AddWorldServices();
        services.AddGameSocketServer();
        services.AddGameHostedServices();

        var build = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        build.Should().NotThrow();
    }
}
