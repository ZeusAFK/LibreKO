using Godot;

namespace LibreKO.Plugins;

public readonly record struct GameRepurchaseRow(string Name, Color NameColour, string Price, bool TooDear, string Time, bool Expired);

public interface IGameRepurchase
{
    bool IsOpen { get; }
    IReadOnlyList<GameRepurchaseRow> Rows { get; }
    string AskText { get; }

    void Buy(int index);
    void Refresh();
    void ShowTooltip(int index);
    void HideTooltip();
    void Close();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameRepurchase Repurchase => repurchase;

    private readonly RepurchaseProxy repurchase = new();

    internal void AttachRepurchase(IGameRepurchase repurchaseSource)
    {
        repurchase.Source = repurchaseSource;
        RaiseRepurchase();
    }

    internal void RaiseRepurchase() => repurchase.Raise();

    private sealed class RepurchaseProxy : IGameRepurchase
    {
        public IGameRepurchase? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public IReadOnlyList<GameRepurchaseRow> Rows => Source?.Rows ?? Array.Empty<GameRepurchaseRow>();
        public string AskText => Source?.AskText ?? "";
        public void Buy(int index) => Source?.Buy(index);
        public void Refresh() => Source?.Refresh();
        public void ShowTooltip(int index) => Source?.ShowTooltip(index);
        public void HideTooltip() => Source?.HideTooltip();
        public void Close() => Source?.Close();
    }
}
