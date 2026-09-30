using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class NpcPositionSeedFileTests
{
    private static string SeedDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Seed", "Data"));

    private const int Julia = 31741;

    private static string MoradonShard => Path.Combine(SeedDirectory(), "NpcPositions.zone021.json");

    private static NpcPosData JuliaRow()
    {
        var rows = JsonNode.Parse(File.ReadAllText(MoradonShard, Encoding.UTF8))!.AsArray();
        var row = rows.Single(r => r!["NpcId"]!.GetValue<int>() == Julia)!;
        return JsonSerializer.Deserialize<NpcPosData>(row.ToJsonString())!;
    }

    [Fact]
    public void RenderingAShardReproducesItByteForByte()
    {
        var shards = Directory.GetFiles(SeedDirectory(), "NpcPositions.zone*.json");
        shards.Should().NotBeEmpty();
        foreach (var shard in shards)
        {
            var text = File.ReadAllText(shard, Encoding.UTF8);
            NpcPositionSeedFile.Render(JsonNode.Parse(text)!.AsArray(), NpcPositionSeedFile.NewLineOf(text)).Should().Be(text, Path.GetFileName(shard));
        }
    }

    [Fact]
    public void UpdatingARowTouchesOnlyThatRowAndKeepsTheFormat()
    {
        var shard = MoradonShard;
        var temp = Path.Combine(Path.GetTempPath(), $"libreko-{Guid.NewGuid():N}.json");
        File.Copy(shard, temp);
        try
        {
            var julia = JuliaRow();
            var after = NpcSpawnRowService.CloneForZone(julia, julia.ZoneId);
            after.LeftX = julia.LeftX + 1;
            after.NumNPC = 2;

            NpcPositionSeedFile.TryUpdate(temp, julia, after, out var error).Should().BeTrue(error);

            var original = JsonNode.Parse(File.ReadAllText(shard))!.AsArray();
            var written = File.ReadAllText(temp, Encoding.UTF8);
            var rows = JsonNode.Parse(written)!.AsArray();
            NpcPositionSeedFile.Render(rows, NpcPositionSeedFile.NewLineOf(written)).Should().Be(written);
            rows.Count.Should().Be(original.Count);
            var changed = rows.Where(row => NpcPositionSeedFile.Matches(row!.AsObject(), after)).ToList();
            changed.Should().ContainSingle();
            rows.Select(row => row!.ToJsonString()).Where(row => !row.Contains("\"NpcId\":31741"))
                .Should().Equal(original.Select(row => row!.ToJsonString()).Where(row => !row.Contains("\"NpcId\":31741")));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void ARowTheFileDoesNotHaveIsRefused()
    {
        var shard = MoradonShard;
        var temp = Path.Combine(Path.GetTempPath(), $"libreko-{Guid.NewGuid():N}.json");
        File.Copy(shard, temp);
        try
        {
            var julia = JuliaRow();
            var elsewhere = NpcSpawnRowService.CloneForZone(julia, julia.ZoneId);
            elsewhere.LeftX = 1;
            NpcPositionSeedFile.TryUpdate(temp, elsewhere, julia, out var error).Should().BeFalse();
            error.Should().Contain("no row");
            File.ReadAllText(temp, Encoding.UTF8).Should().Be(File.ReadAllText(shard, Encoding.UTF8));
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
