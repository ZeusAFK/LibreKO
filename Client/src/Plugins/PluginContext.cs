namespace LibreKO.Plugins;

public sealed class PluginContext
{
    public PluginInfo Self { get; }
    public IReadOnlyList<PluginInfo> All { get; }
    public string Directory => Self.Directory;
    public PluginAssets Assets { get; }
    public PluginUi Ui { get; }
    public PluginGame Game { get; }
    public PluginLog Log { get; }
    public PluginSettings Settings { get; }
    public string ClientVersion => Build.Version;

    internal PluginContext(PluginInfo self, IReadOnlyList<PluginInfo> all, PluginUi ui, PluginGame game)
    {
        Self = self;
        All = all;
        Ui = ui;
        Game = game;
        Log = new PluginLog(self.Id);
        Assets = new PluginAssets(self.Directory, Log);
        Settings = new PluginSettings(self.Id, Log);
    }

    public IEnumerable<PluginInfo> Loaded => All.Where(p => p.IsLoaded);

    public bool IsLoaded(string pluginId) => All.Any(p => p.IsLoaded && p.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
}
