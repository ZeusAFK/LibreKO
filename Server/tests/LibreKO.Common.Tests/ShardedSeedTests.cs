using FluentAssertions;
using LibreKO.Common.Infrastructure.Persistence.Seed;

namespace LibreKO.Common.Tests;

public class ShardedSeedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "libreko-seed-" + Guid.NewGuid().ToString("N"));
    private readonly string _previousRoot = SeedDataLocation.Root;

    public ShardedSeedTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Seed", "Data"));
        SeedDataLocation.Root = _root;
    }

    public void Dispose()
    {
        SeedDataLocation.Root = _previousRoot;
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    public class Row
    {
        public int Num { get; set; }
    }

    private sealed class RowSeed : JsonEntitySeed<Row>
    {
        protected override string JsonFileName => "Rows.json";

        protected override string ShardPattern => "Rows.part*.json";
    }

    private void Write(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_root, "Seed", "Data", fileName), json);

    [Fact]
    public void ShardsStandInForTheSingleFile()
    {
        Write("Rows.part00.json", """[{"Num":1}]""");
        Write("Rows.part01.json", """[{"Num":2}]""");

        var seed = new RowSeed();

        seed.SeedSources.Should().HaveCount(2);
        seed.GetSeedData().Select(row => row.Num).Should().Equal(1, 2);
    }

    [Fact]
    public void TheSingleFileWinsOverShards()
    {
        Write("Rows.json", """[{"Num":9}]""");
        Write("Rows.part00.json", """[{"Num":1}]""");

        var seed = new RowSeed();

        seed.SeedSources.Should().ContainSingle().Which.Should().EndWith("Rows.json");
        seed.GetSeedData().Select(row => row.Num).Should().Equal(9);
    }

    [Fact]
    public async Task TheFingerprintReadsEveryShard()
    {
        Write("Rows.part00.json", """[{"Num":1}]""");
        Write("Rows.part01.json", """[{"Num":2}]""");

        var seed = new RowSeed();
        var before = await SeedFingerprint.ComputeAsync(seed, seed.SeedSources);

        Write("Rows.part01.json", """[{"Num":3}]""");
        var after = await SeedFingerprint.ComputeAsync(seed, seed.SeedSources);

        after.Should().NotBe(before);
    }

    [Fact]
    public void AMissingSeedHasNoSources()
    {
        new RowSeed().SeedSources.Should().BeEmpty();
    }
}
