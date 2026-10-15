using System.IO;
using System.Text.Json;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public sealed record FullMoonRiftSpawn(
    int MonsterId,
    float PosX,
    float PosZ,
    int Count,
    int Radius,
    bool IsMonster = true,
    short Direction = 0);

public sealed record FullMoonRiftStageInfo(
    int StageId,
    byte Difficulty,
    int SpawnIntervalSeconds,
    IReadOnlyList<FullMoonRiftSpawn> Spawns);

public interface IFullMoonRiftStageProvider
{
    IReadOnlyList<FullMoonRiftStageInfo> Stages { get; }
    FullMoonRiftStageInfo? GetStage(int stageId);
}

public sealed class FullMoonRiftStageProvider : IFullMoonRiftStageProvider
{
    private readonly IReadOnlyList<FullMoonRiftStageInfo> _stages;

    public FullMoonRiftStageProvider(ILogger<FullMoonRiftStageProvider>? logger = null)
    {
        var path = SeedDataLocation.DataPath("FullMoonRiftStages.json");
        if (!File.Exists(path))
        {
            var msg = $"FullMoonRiftStages seed file not found at '{path}'.";
            logger?.LogError(msg);
            throw new FileNotFoundException(msg, path);
        }

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<FullMoonRiftStageInfo>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _stages = list ?? throw new InvalidOperationException($"Failed to deserialize '{path}'.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load FullMoonRiftStages from '{Path}'.", path);
            throw;
        }
    }

    public FullMoonRiftStageProvider(IReadOnlyList<FullMoonRiftStageInfo> stages)
    {
        _stages = stages;
    }

    public IReadOnlyList<FullMoonRiftStageInfo> Stages => _stages;

    public FullMoonRiftStageInfo? GetStage(int stageId) => _stages.FirstOrDefault(s => s.StageId == stageId);
}