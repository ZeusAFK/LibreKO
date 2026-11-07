using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LibreKO.Game.Tests;

public sealed class SeedFollowsFileTests : GameTestBase, IDisposable
{
    private const short Zone = 21;
    private const string Shipped = "Moradon";
    private const string Reshipped = "Moradon Square";
    private const string EditedInDatabase = "Edited";

    private readonly string file = Path.Combine(Path.GetTempPath(), $"libreko-zones-{Guid.NewGuid():N}.json");
    private readonly RecordingLogger logger = new();

    public void Dispose()
    {
        File.Delete(file);
    }

    private class ZoneFileSeed(string path) : ZoneInfoSeed
    {
        protected override string JsonFileName => path;
    }

    private sealed class EditedZoneFileSeed(string path) : ZoneFileSeed(path)
    {
        public override bool EditedInGame => true;
    }

    private sealed class RecordingLogger : ILogger<DataSeeder>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private void Ship(string mapName) =>
        File.WriteAllText(file, $$"""[{"ServerNo":1,"ZoneNo":{{Zone}},"SmdName":"moradon.smd","MapName":"{{mapName}}"}]""");

    private Task<bool> SeedAsync<T>(ServiceProvider provider, IEntitySeed<T> seed) where T : class =>
        QueryAsync(provider, db => new DataSeeder(db, logger).SeedEntityAsync(seed));

    private static async Task<T> QueryAsync<T>(ServiceProvider provider, Func<AppDbContext, Task<T>> query)
    {
        using var scope = provider.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<string> MapNameAsync(ServiceProvider provider) =>
        QueryAsync(provider, async db => (await db.ZoneInfos.AsNoTracking().SingleAsync()).MapName);

    private static Task EditAsync(ServiceProvider provider) =>
        QueryAsync(provider, async db =>
        {
            var row = await db.ZoneInfos.SingleAsync();
            row.MapName = EditedInDatabase;
            return await db.SaveChangesAsync();
        });

    private IEnumerable<string> Warnings =>
        logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message);

    [Fact]
    public async Task ATableOwnedByItsFileTakesTheFileWhenItChanges()
    {
        Ship(Shipped);
        using var provider = CreateProvider(_ => { });
        await SeedAsync(provider, new ZoneFileSeed(file));

        Ship(Reshipped);
        var applied = await SeedAsync(provider, new ZoneFileSeed(file));

        applied.Should().BeTrue();
        (await MapNameAsync(provider)).Should().Be(Reshipped);
    }

    [Fact]
    public async Task ATableOwnedByItsFileKeepsADatabaseEditWhileTheFileIsUnchanged()
    {
        Ship(Shipped);
        using var provider = CreateProvider(_ => { });
        await SeedAsync(provider, new ZoneFileSeed(file));
        await EditAsync(provider);

        var applied = await SeedAsync(provider, new ZoneFileSeed(file));

        applied.Should().BeFalse();
        (await MapNameAsync(provider)).Should().Be(EditedInDatabase);
    }

    [Fact]
    public async Task ATableOwnedByItsFileThatWasNeverRecordedIsBroughtToTheFile()
    {
        Ship(Shipped);
        using var provider = CreateProvider(db => db.ZoneInfos.Add(new ZoneInfoData { ServerNo = 1, ZoneNo = Zone, MapName = EditedInDatabase }));

        var applied = await SeedAsync(provider, new ZoneFileSeed(file));

        applied.Should().BeTrue();
        (await MapNameAsync(provider)).Should().Be(Shipped);
    }

    [Fact]
    public async Task ATableEditedInGameKeepsItsRowsAndWarnsWhenItsFileChanges()
    {
        Ship(Shipped);
        using var provider = CreateProvider(_ => { });
        await SeedAsync(provider, new EditedZoneFileSeed(file));
        await EditAsync(provider);

        Ship(Reshipped);
        var applied = await SeedAsync(provider, new EditedZoneFileSeed(file));

        applied.Should().BeFalse();
        (await MapNameAsync(provider)).Should().Be(EditedInDatabase);
        Warnings.Should().ContainSingle().Which.Should().Contain(nameof(EditedZoneFileSeed)).And.Contain("edited in game");
    }

    [Fact]
    public async Task ATableEditedInGameThatWasNeverRecordedTakesTheFileAsItsStartingPoint()
    {
        Ship(Shipped);
        using var provider = CreateProvider(db => db.ZoneInfos.Add(new ZoneInfoData { ServerNo = 1, ZoneNo = Zone, MapName = EditedInDatabase }));

        var applied = await SeedAsync(provider, new EditedZoneFileSeed(file));

        applied.Should().BeFalse();
        Warnings.Should().BeEmpty();
        (await QueryAsync(provider, db => db.Set<SeedState>().CountAsync(state => state.Name == nameof(EditedZoneFileSeed))))
            .Should().Be(1);

        Ship(Reshipped);
        await SeedAsync(provider, new EditedZoneFileSeed(file));

        (await MapNameAsync(provider)).Should().Be(EditedInDatabase);
        Warnings.Should().ContainSingle();
    }

    [Fact]
    public void TheTablesGmsEditInGameStayFillOnly()
    {
        var editedInGame = new Dictionary<string, bool>
        {
            [nameof(NpcSeed)] = new NpcSeed().EditedInGame,
            [nameof(NpcPosSeed)] = new NpcPosSeed().EditedInGame,
            [nameof(NpcItemSeed)] = new NpcItemSeed().EditedInGame,
            [nameof(MakeItemGroupSeed)] = new MakeItemGroupSeed().EditedInGame,
            [nameof(KingSystemSeed)] = new KingSystemSeed().EditedInGame,
            [nameof(SiegeWarfareSeed)] = new SiegeWarfareSeed().EditedInGame,
            [nameof(CollectionRaceSeed)] = new CollectionRaceSeed().EditedInGame,
            [nameof(CollectionRaceObjectiveSeed)] = new CollectionRaceObjectiveSeed().EditedInGame,
            [nameof(CollectionRaceRewardSeed)] = new CollectionRaceRewardSeed().EditedInGame,
            [nameof(CollectionRaceScheduleSeed)] = new CollectionRaceScheduleSeed().EditedInGame,
            [nameof(LotteryEventSeed)] = new LotteryEventSeed().EditedInGame,
            [nameof(LotteryRewardSeed)] = new LotteryRewardSeed().EditedInGame,
            [nameof(LotteryScheduleSeed)] = new LotteryScheduleSeed().EditedInGame,
            [nameof(TempleEventScheduleSeed)] = new TempleEventScheduleSeed().EditedInGame,
        };

        editedInGame.Where(seed => !seed.Value).Select(seed => seed.Key).Should().BeEmpty();
    }

    [Fact]
    public void TheItemAndZoneTablesFollowTheirFiles()
    {
        new[]
        {
            new ItemBaseSeed().EditedInGame,
            new ItemVariantSeed().EditedInGame,
            new ItemUpgradeRecipeSeed().EditedInGame,
            new ZoneInfoSeed().EditedInGame,
            new StartPositionSeed().EditedInGame,
        }.Should().AllBeEquivalentTo(false);
    }
}
