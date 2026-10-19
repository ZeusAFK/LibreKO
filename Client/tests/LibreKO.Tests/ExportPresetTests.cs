using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LibreKO.Tests;

public class ExportPresetTests
{
    private const string ExportPresets = "export_presets.cfg";
    private const string IncludeFilterKey = "include_filter=";
    private const string ResPrefix = "res://";
    private const string AnyZone = "moradon";

    public static TheoryData<string> FilesGodotDoesNotImport => new()
    {
        Config.DefaultsResPath,
        Build.VersionResPath,
        FxSceneDepth.ComputePath,
        ZoneCollision.ResPath(AnyZone),
    };

    [Theory]
    [MemberData(nameof(FilesGodotDoesNotImport))]
    public void EveryExportPresetShipsTheFilesGodotDoesNotImport(string resPath)
    {
        if (!File.Exists(Path.Combine(ClientFiles.Dir(), ExportPresets)))
            return;
        var filters = ClientFiles.Read(ExportPresets).Split('\n')
            .Where(line => line.StartsWith(IncludeFilterKey, StringComparison.Ordinal))
            .Select(line => line[IncludeFilterKey.Length..].Trim('"'))
            .ToList();
        Assert.NotEmpty(filters);
        foreach (string filter in filters)
            Assert.Contains(filter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                pattern => Matches(pattern, resPath) || Matches(pattern, resPath[ResPrefix.Length..]));
    }

    [Theory]
    [InlineData("shaders/*.comp", "shaders/fx_scene_depth.comp", true)]
    [InlineData("assets/terrain/*/collision.bin", "assets/terrain/moradon/collision.bin", true)]
    [InlineData("assets/terrain/*/collision.bin", "assets/terrain/moradon/placements.json", false)]
    [InlineData("version.txt", "res://version.txt", false)]
    [InlineData("VERSION.TXT", "version.txt", true)]
    [InlineData("version.tx?", "version.txt", true)]
    [InlineData("version?txt", "version.txt", false)]
    public void FiltersMatchLikeGodot(string pattern, string path, bool expected) =>
        Assert.Equal(expected, Matches(pattern, path));

    private static bool Matches(string pattern, string path) =>
        Regex.IsMatch(path, "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", "[^.]") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
