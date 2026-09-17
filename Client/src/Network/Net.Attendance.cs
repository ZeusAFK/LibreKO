using System;

namespace LibreKO.Network;

public partial class Net
{
    public const int AttendanceDailySlots = 25;
    public const int AttendanceBonusSlots = 3;
    public const int AttendanceBonusFirstSlot = 101;

    public const byte AttendanceStateClaimed = 1;
    public const byte AttendanceStateExpired = 2;
    public const byte AttendanceStateClaimable = 3;
    public const byte AttendanceStateClaimableExtra = 4;

    public const byte EventBoardAttendanceList = 4;
    public const byte EventBoardAttendanceClaim = 5;

    private const uint EventBoardChannel = 0;
    private const uint EventBoardResultOk = 1;

    private const int AttendanceBoardBytes =
        4 + 2 + AttendanceDailySlots * 5 + 2 + AttendanceBonusSlots * 5 + 4;

    public bool AttendanceAutoOpened { get; set; }

    public event Action<int[], byte[]>? AttendanceBoardEvent;
    public event Action<byte, uint>? AttendanceFailedEvent;

    private void HandleEventBoard(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == EventBoardRouletteOpen || sub == EventBoardRouletteSpin)
        {
            HandleRoulette(p, sub);
            return;
        }
        if (sub != EventBoardAttendanceList && sub != EventBoardAttendanceClaim) return;
        if (p.RemainingBytes < 8) return;

        if (p.ReadUInt() != EventBoardChannel) return;
        uint result = p.ReadUInt();
        if (result != EventBoardResultOk)
        {
            AttendanceFailedEvent?.Invoke(sub, result);
            return;
        }

        if (p.RemainingBytes < AttendanceBoardBytes) return;

        p.ReadUInt();
        var slots = new int[AttendanceDailySlots + AttendanceBonusSlots];
        var states = new byte[slots.Length];

        int dailyCount = p.ReadUShort();
        for (int i = 0; i < AttendanceDailySlots; i++)
        {
            int slot = p.ReadInt();
            byte state = p.ReadByte();
            if (i >= dailyCount) continue;
            slots[i] = slot;
            states[i] = state;
        }

        int bonusCount = p.ReadUShort();
        for (int i = 0; i < AttendanceBonusSlots; i++)
        {
            int slot = p.ReadInt();
            byte state = p.ReadByte();
            if (i >= bonusCount) continue;
            slots[AttendanceDailySlots + i] = slot;
            states[AttendanceDailySlots + i] = state;
        }

        p.ReadUInt();
        AttendanceBoardEvent?.Invoke(slots, states);
    }

    public void SendAttendanceBoardRequest()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte(EventBoardAttendanceList);
        p.WriteUInt(EventBoardChannel);
        _conn.Send(p);
    }

    public void SendAttendanceClaim(int slot, byte state)
    {
        if (state < AttendanceStateClaimable) return;
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte(EventBoardAttendanceClaim);
        p.WriteUInt(EventBoardChannel);
        p.WriteInt(slot);
        p.WriteByte((byte)(state - 2));
        _conn.Send(p);
    }

    public static int AttendanceBonusThreshold(int slot) => 15 + (slot - AttendanceBonusFirstSlot) * 5;
}
