using Godot;

namespace LibreKO.Plugins;

public sealed class PluginLog
{
    private readonly string prefix;

    internal PluginLog(string pluginId) => prefix = $"[plugin:{pluginId}]";

    public void Info(string message) => GD.Print($"{prefix} {message}");

    public void Warn(string message) => GD.PushWarning($"{prefix} {message}");

    public void Error(string message) => GD.PushError($"{prefix} {message}");
}
