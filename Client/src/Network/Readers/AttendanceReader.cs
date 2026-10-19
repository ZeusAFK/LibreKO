namespace LibreKO.Network.Readers;

internal sealed class AttendanceReader : IPacketReader
{
    private readonly AttendanceSession attendance;

    private const int BoardBytes =
            4 + 2 + AttendanceWire.DailySlots * 5 + 2 + AttendanceWire.BonusSlots * 5 + 4;

    public AttendanceReader(AttendanceSession attendance)
    {
        this.attendance = attendance;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.OnOtherSubs<EventBoardSubOpcode>(GameOpcodes.GS_EVENT_BOARD, HandleEventBoard);
    }

    private void HandleEventBoard(Packet p, EventBoardSubOpcode sub)
    {
        if (sub != EventBoardSubOpcode.AttendanceBoard && sub != EventBoardSubOpcode.AttendanceClaim) return;
        if (p.RemainingBytes < 8) return;

        if (p.ReadUInt() != EventBoardWire.Channel) return;
        var result = (EventBoardResult)p.ReadUInt();
        if (result != EventBoardResult.Ok)
        {
            attendance.RaiseAttendanceFailed(sub, result);
            return;
        }

        if (p.RemainingBytes < BoardBytes) return;

        p.ReadUInt();
        var slots = new int[AttendanceWire.DailySlots + AttendanceWire.BonusSlots];
        var states = new byte[slots.Length];

        int dailyCount = p.ReadUShort();
        for (int i = 0; i < AttendanceWire.DailySlots; i++)
        {
            int slot = p.ReadInt();
            byte state = p.ReadByte();
            if (i >= dailyCount) continue;
            slots[i] = slot;
            states[i] = state;
        }

        int bonusCount = p.ReadUShort();
        for (int i = 0; i < AttendanceWire.BonusSlots; i++)
        {
            int slot = p.ReadInt();
            byte state = p.ReadByte();
            if (i >= bonusCount) continue;
            slots[AttendanceWire.DailySlots + i] = slot;
            states[AttendanceWire.DailySlots + i] = state;
        }

        p.ReadUInt();
        attendance.RaiseAttendanceBoard(slots, states);
    }
}
