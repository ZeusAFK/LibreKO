using System.IO;
using System.Text.Json;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IDrakiStageProvider
{
    IReadOnlyList<DrakiStageInfo> Stages { get; }
}

public sealed class DrakiStageProvider : IDrakiStageProvider
{
    private readonly IReadOnlyList<DrakiStageInfo> _stages;

    public DrakiStageProvider(ILogger<DrakiStageProvider>? logger = null)
    {
        var path = SeedDataLocation.DataPath("DrakiTowerStages.json");
        if (!File.Exists(path))
        {
            var msg = $"DrakiTowerStages seed file not found at '{path}'.";
            logger?.LogError(msg);
            throw new FileNotFoundException(msg, path);
        }

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<DrakiStageInfo>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _stages = list ?? throw new InvalidOperationException($"Failed to deserialize '{path}'.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load DrakiTowerStages from '{Path}'.", path);
            throw;
        }
    }

    public DrakiStageProvider(IReadOnlyList<DrakiStageInfo> stages)
    {
        _stages = stages;
    }

    public IReadOnlyList<DrakiStageInfo> Stages => _stages;
}
