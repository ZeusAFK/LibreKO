namespace LibreKO.Plugins;

public enum PluginState
{
    Invalid,
    Unsupported,
    Incompatible,
    Disabled,
    Conflict,
    Loaded,
    Failed,
}

public sealed class PluginInfo
{
    public string Directory { get; }
    public string FolderName => Path.GetFileName(Directory.TrimEnd('/', '\\'));
    public PluginManifest? Manifest { get; }
    public PluginState State { get; internal set; }
    public string Message { get; internal set; } = "";
    public bool Enabled { get; internal set; }
    internal IPlugin? Instance { get; set; }

    public string Id => Manifest?.Id ?? FolderName;
    public string Name => Manifest?.Name ?? FolderName;
    public string Version => Manifest?.Version ?? "";
    public PluginType Type => Manifest?.Type ?? PluginType.Extension;
    public bool IsLoaded => State == PluginState.Loaded;
    public bool CanEnable => State is not (PluginState.Invalid or PluginState.Unsupported or PluginState.Incompatible);

    internal PluginInfo(string directory, PluginManifest? manifest, PluginState state, string message = "")
    {
        Directory = directory;
        Manifest = manifest;
        State = state;
        Message = message;
    }

    public string StateText => State switch
    {
        PluginState.Invalid => $"Invalid manifest: {Message}",
        PluginState.Unsupported => Message.Length > 0 ? Message : "Not supported on this platform",
        PluginState.Incompatible => Message.Length > 0 ? Message : "Needs a newer client",
        PluginState.Disabled => "Disabled",
        PluginState.Conflict => Message.Length > 0 ? Message : "Another plugin of this type is enabled",
        PluginState.Loaded => "Loaded",
        PluginState.Failed => $"Failed: {Message}",
        _ => State.ToString(),
    };
}
