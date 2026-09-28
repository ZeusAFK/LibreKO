using Godot;
using LibreKO.Plugins;

namespace LibreKO;

public partial class SettingsPanel : CanvasLayer
{
    private const int PluginRowSeparation = 4;
    private const int PluginDescriptionFontSize = 12;
    private const int PluginMetaFontSize = 11;

    private VBoxContainer _pluginList = null!;
    private Label _pluginRestartNote = null!;

    private void BuildPluginsTab(VBoxContainer tab)
    {
        var intro = UiTheme.Text(
            $"Plugins are folders inside \"{PluginHost.PrimaryRoot}\". Each one carries a plugin.json; " +
            "only one UI theme can be enabled at a time.", PluginDescriptionFontSize, UiTheme.TextLo);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        tab.AddChild(intro);

        _pluginRestartNote = UiTheme.Text("Changes take effect after the game restarts.", PluginDescriptionFontSize, UiTheme.Warning);
        _pluginRestartNote.Visible = PluginHost.RestartRequired;
        tab.AddChild(_pluginRestartNote);

        _pluginList = new VBoxContainer();
        _pluginList.AddThemeConstantOverride("separation", 10);
        tab.AddChild(_pluginList);
        FillPluginList();

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        tab.AddChild(buttons);
        var open = new Button { Text = "Open plugins folder" };
        open.Pressed += PluginHost.OpenFolder;
        buttons.AddChild(open);
        var rescan = new Button { Text = "Rescan" };
        rescan.Pressed += () => { PluginHost.Discover(); FillPluginList(); };
        buttons.AddChild(rescan);
    }

    private void FillPluginList()
    {
        foreach (var child in _pluginList.GetChildren()) child.QueueFree();
        if (PluginHost.All.Count == 0)
        {
            _pluginList.AddChild(UiTheme.Text("No plugins found.", PluginDescriptionFontSize, UiTheme.TextDim));
            return;
        }
        foreach (var info in PluginHost.All)
            _pluginList.AddChild(PluginRow(info));
    }

    private Control PluginRow(PluginInfo info)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(4));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        panel.AddChild(row);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", PluginRowSeparation);
        row.AddChild(text);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        text.AddChild(head);
        head.AddChild(UiTheme.Text(info.Name, 14, UiTheme.TextHi));
        if (info.Version.Length > 0)
            head.AddChild(UiTheme.Text(info.Version, PluginMetaFontSize, UiTheme.TextLo));
        head.AddChild(UiTheme.Text(PluginManifest.TypeName(info.Type), PluginMetaFontSize, UiTheme.Gold));

        string meta = info.Manifest?.Author.Length > 0 ? $"by {info.Manifest.Author}" : "";
        if (info.Manifest?.Homepage.Length > 0) meta += (meta.Length > 0 ? "  " : "") + info.Manifest.Homepage;
        if (meta.Length > 0)
            text.AddChild(UiTheme.Text(meta, PluginMetaFontSize, UiTheme.TextLo));

        if (info.Manifest?.Description.Length > 0)
        {
            var desc = UiTheme.Text(info.Manifest.Description, PluginDescriptionFontSize, UiTheme.TextLo);
            desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.AddChild(desc);
        }

        var stateColor = info.State switch
        {
            PluginState.Loaded => UiTheme.Good,
            PluginState.Failed or PluginState.Invalid => UiTheme.Bad,
            PluginState.Disabled => UiTheme.TextDim,
            _ => UiTheme.Warning,
        };
        var state = UiTheme.Text(info.StateText, PluginMetaFontSize, stateColor);
        state.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(state);
        text.AddChild(UiTheme.Text(info.Directory, PluginMetaFontSize, UiTheme.TextDim));

        var toggle = new CheckButton
        {
            Text = "Enabled",
            ButtonPressed = info.Enabled,
            Disabled = !info.CanEnable,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        toggle.Toggled += on =>
        {
            PluginHost.SetEnabled(info.Id, on);
            _pluginRestartNote.Visible = PluginHost.RestartRequired;
            FillPluginList();
        };
        row.AddChild(toggle);
        return panel;
    }
}
