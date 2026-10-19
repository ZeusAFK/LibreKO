using Godot;

namespace LibreKO.Plugins;

public readonly record struct GameEquipValue(string Key, string Caption, string Value);

public sealed record GameEquipView(
    string Name,
    Color NameColour,
    string Class,
    int Nation,
    IReadOnlyList<GameEquipValue> Values,
    IReadOnlyDictionary<int, Texture2D?> Items,
    string Status);

public interface IGameEquipView
{
    bool IsOpen { get; }
    GameEquipView? View { get; }
    string Title { get; }
    string Note { get; }
    string ResistTitle { get; }

    void ShowTooltip(int slot);
    void HideTooltip();
    void Close();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameEquipView EquipView => equipView;

    private readonly EquipViewProxy equipView = new();

    internal void AttachEquipView(IGameEquipView equipViewSource)
    {
        equipView.Source = equipViewSource;
        RaiseEquipView();
    }

    internal void RaiseEquipView() => equipView.Raise();

    private sealed class EquipViewProxy : IGameEquipView
    {
        public IGameEquipView? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public GameEquipView? View => Source?.View;
        public string Title => Source?.Title ?? "";
        public string Note => Source?.Note ?? "";
        public string ResistTitle => Source?.ResistTitle ?? "";
        public void ShowTooltip(int slot) => Source?.ShowTooltip(slot);
        public void HideTooltip() => Source?.HideTooltip();
        public void Close() => Source?.Close();
    }
}
