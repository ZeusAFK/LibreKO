using System.Globalization;
using System.Text.Json;
using Godot;

namespace LibreKO.Plugins;

public sealed class PluginSettings
{
    private const string Folder = "user://plugins";

    public static bool Enabled { get; set; } = true;

    private readonly string _path;
    private readonly PluginLog _log;
    private readonly Dictionary<string, string> _values = new();

    internal PluginSettings(string pluginId, PluginLog log)
    {
        _log = log;
        _path = ProjectSettings.GlobalizePath($"{Folder}/{pluginId}.settings.json");
        Load();
    }

    public string Get(string key, string fallback = "") => Enabled && _values.TryGetValue(key, out var v) ? v : fallback;

    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public float GetFloat(string key, float fallback) =>
        float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public bool GetBool(string key, bool fallback) => bool.TryParse(Get(key), out var v) ? v : fallback;

    public void Set(string key, string value)
    {
        if (!Enabled) return;
        if (_values.TryGetValue(key, out var current) && current == value) return;
        _values[key] = value;
        Save();
    }

    public void SetInt(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

    public void SetFloat(string key, float value) => Set(key, value.ToString("R", CultureInfo.InvariantCulture));

    public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path));
            if (stored == null) return;
            foreach (var (key, value) in stored) _values[key] = value;
        }
        catch (Exception ex)
        {
            _log.Warn($"settings not read from {_path}: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.Warn($"settings not written to {_path}: {ex.Message}");
        }
    }
}
