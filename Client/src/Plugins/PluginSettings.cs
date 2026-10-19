using System.Globalization;
using System.Text.Json;
using Godot;

namespace LibreKO.Plugins;

public sealed class PluginSettings
{
    private const string Folder = "user://plugins";

    public static bool Enabled { get; set; } = true;

    private readonly string path;
    private readonly PluginLog log;
    private readonly Dictionary<string, string> values = new();

    internal PluginSettings(string pluginId, PluginLog log)
    {
        this.log = log;
        path = ProjectSettings.GlobalizePath($"{Folder}/{pluginId}.settings.json");
        Load();
    }

    public string Get(string key, string fallback = "") => Enabled && values.TryGetValue(key, out var v) ? v : fallback;

    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public float GetFloat(string key, float fallback) =>
        float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public bool GetBool(string key, bool fallback) => bool.TryParse(Get(key), out var v) ? v : fallback;

    public void Set(string key, string value)
    {
        if (!Enabled) return;
        if (values.TryGetValue(key, out var current) && current == value) return;
        values[key] = value;
        Save();
    }

    public void SetInt(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

    public void SetFloat(string key, float value) => Set(key, value.ToString("R", CultureInfo.InvariantCulture));

    public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");

    private void Load()
    {
        try
        {
            if (!File.Exists(path)) return;
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (stored == null) return;
            foreach (var (key, value) in stored) values[key] = value;
        }
        catch (Exception ex)
        {
            log.Warn($"settings not read from {path}: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            log.Warn($"settings not written to {path}: {ex.Message}");
        }
    }
}
