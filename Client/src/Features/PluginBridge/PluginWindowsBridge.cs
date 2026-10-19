using Godot;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginWindowsBridge(TopIconsFeature topIcons, MainWindowsFeature mainWindows, SkillPresetFeature skillPreset, TitlePickerFeature titlePicker) : IGameWindows
{
    event Action? IGameWindows.TopRightRowMoved { add { } remove { } }

    public Rect2 TopRightRow => topIcons.Row;

    public bool IsOpen(string id)
    {
        if (PluginBridgeFeature.MainWindowKeys.TryGetValue(id, out var key)) return mainWindows.IsOpen(key);
        return HudWindow.Find(id)?.Visible ?? false;
    }

    public void Open(string id)
    {
        if (PluginBridgeFeature.MainWindowKeys.TryGetValue(id, out var key)) { mainWindows.Show(key); return; }
        if (HudWindow.Find(id) is { } win) win.Visible = true;
    }

    public void Close(string id)
    {
        if (PluginBridgeFeature.MainWindowKeys.TryGetValue(id, out var key))
        {
            if (mainWindows.IsOpen(key)) mainWindows.Toggle(key);
            return;
        }
        if (HudWindow.Find(id) is { Visible: true } win) win.Close();
    }

    public void Toggle(string id)
    {
        if (string.Equals(id, PluginBridgeFeature.ClanWindowId, StringComparison.OrdinalIgnoreCase)) { mainWindows.OpenCharacterPage(CharacterPage.Clan); return; }
        if (string.Equals(id, PluginBridgeFeature.PresetWindowId, StringComparison.OrdinalIgnoreCase)) { skillPreset.TogglePreset(); return; }
        if (string.Equals(id, PluginBridgeFeature.TitlesWindowId, StringComparison.OrdinalIgnoreCase)) { titlePicker.Toggle(); return; }
        if (PluginBridgeFeature.MainWindowKeys.TryGetValue(id, out var key)) { mainWindows.Toggle(key); return; }
        if (HudWindow.Find(id) is not { } win) return;
        if (win.Visible) win.Close();
        else win.Visible = true;
    }
}
