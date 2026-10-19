namespace LibreKO.Session;

public sealed class AttendanceSession
{
    public bool AttendanceAutoOpened { get; private set; }

    internal void NoteAutoOpened() => AttendanceAutoOpened = true;

    public event Action<int[], byte[]>? AttendanceBoardEvent;

    internal void RaiseAttendanceBoard(int[] arg1, byte[] arg2) => AttendanceBoardEvent?.Invoke(arg1, arg2);

    public event Action<EventBoardSubOpcode, EventBoardResult>? AttendanceFailedEvent;

    internal void RaiseAttendanceFailed(EventBoardSubOpcode arg1, EventBoardResult arg2) => AttendanceFailedEvent?.Invoke(arg1, arg2);
}
