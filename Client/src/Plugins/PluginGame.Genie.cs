using Godot;
using LibreKO.Domain;

namespace LibreKO.Plugins;

public enum GeniePotionSlot
{
    Hp,
    Mp,
    Pet,
}

public readonly record struct GenieCellView(int SkillId, Texture2D? Icon, string Tooltip);

public readonly record struct GeniePotionView(int ItemId, Texture2D? Icon, int Count, string Name);

public readonly record struct GenieTarget(string Name, bool On);

public interface IGameGenie
{
    bool IsOpen { get; }
    bool Running { get; }
    bool Waiting { get; }
    int Points { get; }
    int PointsPerSpirit { get; }
    string RunLine { get; }
    GenieSetup Setup { get; }
    IReadOnlyList<GenieTarget> Targets { get; }
    int TargetRows { get; }
    bool HasMore { get; }

    GenieCellView Cell(GenieCellKind kind, int index);
    GeniePotionView Potion(GeniePotionSlot slot);

    void Apply(GenieSetup setup);
    bool SetCell(GenieCellKind kind, int index, int skillId);
    void MoveCell(GenieCellKind fromKind, int fromIndex, GenieCellKind toKind, int toIndex);
    void PickSkill(GenieCellKind kind, int index, Vector2 screenAt);
    void PickPotion(GeniePotionSlot slot, Vector2 screenAt);
    void ToggleRun();
    void UseSpirit();
    void OpenMore();
    void ToggleWindow();
    void AddTarget();
    void RemoveTarget(int row);
    void SetTargetOn(int row, bool on);
    void Close();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameGenie Genie => genie;

    private readonly GenieProxy genie = new();

    internal void AttachGenie(IGameGenie genieSource)
    {
        genie.Source = genieSource;
        RaiseGenie();
    }

    internal void RaiseGenie() => genie.Raise();

    private sealed class GenieProxy : IGameGenie
    {
        public IGameGenie? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public bool Running => Source?.Running ?? false;
        public bool Waiting => Source?.Waiting ?? false;
        public int Points => Source?.Points ?? 0;
        public int PointsPerSpirit => Source?.PointsPerSpirit ?? 1;
        public string RunLine => Source?.RunLine ?? "";
        public GenieSetup Setup => Source?.Setup ?? new GenieSetup();
        public IReadOnlyList<GenieTarget> Targets => Source?.Targets ?? Array.Empty<GenieTarget>();
        public int TargetRows => Source?.TargetRows ?? 0;
        public bool HasMore => Source?.HasMore ?? false;
        public GenieCellView Cell(GenieCellKind kind, int index) => Source?.Cell(kind, index) ?? default;
        public GeniePotionView Potion(GeniePotionSlot slot) => Source?.Potion(slot) ?? default;
        public void Apply(GenieSetup setup) => Source?.Apply(setup);
        public bool SetCell(GenieCellKind kind, int index, int skillId) => Source?.SetCell(kind, index, skillId) ?? false;
        public void MoveCell(GenieCellKind fromKind, int fromIndex, GenieCellKind toKind, int toIndex) =>
            Source?.MoveCell(fromKind, fromIndex, toKind, toIndex);
        public void PickSkill(GenieCellKind kind, int index, Vector2 screenAt) => Source?.PickSkill(kind, index, screenAt);
        public void PickPotion(GeniePotionSlot slot, Vector2 screenAt) => Source?.PickPotion(slot, screenAt);
        public void ToggleRun() => Source?.ToggleRun();
        public void UseSpirit() => Source?.UseSpirit();
        public void OpenMore() => Source?.OpenMore();
        public void ToggleWindow() => Source?.ToggleWindow();
        public void AddTarget() => Source?.AddTarget();
        public void RemoveTarget(int row) => Source?.RemoveTarget(row);
        public void SetTargetOn(int row, bool on) => Source?.SetTargetOn(row, on);
        public void Close() => Source?.Close();
    }
}
