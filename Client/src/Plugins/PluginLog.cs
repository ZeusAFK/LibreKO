using Godot;

namespace LibreKO.Plugins;

public sealed class PluginLog
{
    private readonly string _prefix;

    internal PluginLog(string pluginId) => _prefix = $"[plugin:{pluginId}]";

    public void Info(string message) => GD.Print($"{_prefix} {message}");

    public void Warn(string message) => GD.PushWarning($"{_prefix} {message}");

    public void Error(string message) => GD.PushError($"{_prefix} {message}");
}
