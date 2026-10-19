using Godot;

namespace LibreKO.Plugins;

public enum GameAttendanceState
{
    Locked,
    Claimable,
    Claimed,
}

public readonly record struct GameAttendanceCell(int ItemId, int Count, string ItemName, Texture2D? Icon, string Caption, GameAttendanceState State);

public interface IGameAttendance
{
    bool IsOpen { get; }
    int Attended { get; }
    IReadOnlyList<GameAttendanceCell> Daily { get; }
    IReadOnlyList<GameAttendanceCell> Bonus { get; }

    void Claim(int index);
    void ShowTooltip(int index);
    void HideTooltip();
    void Close();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameAttendance Attendance => attendance;

    private readonly AttendanceProxy attendance = new();

    internal void AttachAttendance(IGameAttendance attendanceSource)
    {
        attendance.Source = attendanceSource;
        RaiseAttendance();
    }

    internal void RaiseAttendance() => attendance.Raise();

    private sealed class AttendanceProxy : IGameAttendance
    {
        public IGameAttendance? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public int Attended => Source?.Attended ?? 0;
        public IReadOnlyList<GameAttendanceCell> Daily => Source?.Daily ?? Array.Empty<GameAttendanceCell>();
        public IReadOnlyList<GameAttendanceCell> Bonus => Source?.Bonus ?? Array.Empty<GameAttendanceCell>();
        public void Claim(int index) => Source?.Claim(index);
        public void ShowTooltip(int index) => Source?.ShowTooltip(index);
        public void HideTooltip() => Source?.HideTooltip();
        public void Close() => Source?.Close();
    }
}
