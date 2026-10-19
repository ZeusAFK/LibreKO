using Godot;

namespace LibreKO.Plugins;

public readonly record struct GameWarpRow(string Name, string Levels, string Fee, string? Blocked, Texture2D? Image, string Description);

public interface IGameWarp
{
    bool IsOpen { get; }
    string Title { get; }
    IReadOnlyList<GameWarpRow> Rows { get; }
    int Selected { get; }

    void Select(int index);
    void Travel();
    void Close();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameWarp Warp => warp;

    private readonly WarpProxy warp = new();

    internal void AttachWarp(IGameWarp warpSource)
    {
        warp.Source = warpSource;
        RaiseWarp();
    }

    internal void RaiseWarp() => warp.Raise();

    private sealed class WarpProxy : IGameWarp
    {
        public IGameWarp? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public string Title => Source?.Title ?? "";
        public IReadOnlyList<GameWarpRow> Rows => Source?.Rows ?? Array.Empty<GameWarpRow>();
        public int Selected => Source?.Selected ?? -1;
        public void Select(int index) => Source?.Select(index);
        public void Travel() => Source?.Travel();
        public void Close() => Source?.Close();
    }
}
