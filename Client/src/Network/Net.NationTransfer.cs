using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public sealed record NationTransferCandidate(int Slot, string Name, int Race, int Nation, int Class, int Face, int Hair);

public readonly record struct NationTransferPick(int Slot, string Name, int Race, int Face, int Hair);

public partial class Net
{
    public const byte NationTransferWarStatus = 1;
    public const byte NationTransferOpenBox = 2;
    public const byte NationTransferSubmit = 3;
    public const byte NationTransferAccepted = 1;
    public const byte NationTransferMovedAgain = 2;
    public const byte NationTransferErrorBox = 16;
    public const byte NationTransferWarRunning = 8;

    public event Action<IReadOnlyList<NationTransferCandidate>>? NationTransferOpenEvent;
    public event Action<int>? NationTransferRefusedEvent;
    public event Action? NationTransferDoneEvent;
    public event Action<int, int>? NationTransferWarEvent;

    private int _nationTransferTarget;

    private void HandleNationTransfer(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        byte sub = p.ReadByte();
        byte result = p.ReadByte();
        switch (sub)
        {
            case NationTransferOpenBox when result == NationTransferAccepted:
                NationTransferOpenEvent?.Invoke(ReadNationTransferCandidates(p));
                break;
            case NationTransferSubmit when result is NationTransferAccepted or NationTransferMovedAgain:
                if (_nationTransferTarget != 0) Nation = _nationTransferTarget;
                NationTransferDoneEvent?.Invoke();
                break;
            case NationTransferWarStatus or NationTransferErrorBox when result == NationTransferWarRunning && p.RemainingBytes >= 2:
                NationTransferWarEvent?.Invoke(p.ReadByte(), p.ReadByte());
                break;
            case NationTransferWarStatus or NationTransferOpenBox or NationTransferSubmit or NationTransferErrorBox:
                NationTransferRefusedEvent?.Invoke(result);
                break;
        }
    }

    private List<NationTransferCandidate> ReadNationTransferCandidates(Packet p)
    {
        var list = new List<NationTransferCandidate>();
        if (p.RemainingBytes < 1) return list;
        int count = p.ReadByte();
        for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
        {
            int slot = p.ReadShort();
            string name = p.ReadString();
            if (p.RemainingBytes < 9) break;
            int race = p.ReadByte();
            int nation = p.ReadByte();
            int cls = p.ReadShort();
            int face = p.ReadByte();
            int hair = p.ReadInt();
            list.Add(new NationTransferCandidate(slot, name, race, nation, cls, face, hair));
        }
        if (list.Count > 0) _nationTransferTarget = list[0].Nation;
        return list;
    }

    public void SendNationTransfer(IReadOnlyList<NationTransferPick> picks)
    {
        var p = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        p.WriteByte(NationTransferSubmit);
        p.WriteByte(NationTransferAccepted);
        p.WriteByte((byte)picks.Count);
        foreach (var pick in picks)
        {
            p.WriteShort((short)pick.Slot);
            p.WriteString(pick.Name);
            p.WriteByte((byte)pick.Race);
            p.WriteByte((byte)pick.Face);
            p.WriteInt(pick.Hair);
        }
        _conn.Send(p);
    }

    public void SendNationTransferCancel()
    {
        var p = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        p.WriteByte(NationTransferSubmit);
        p.WriteByte(0);
        _conn.Send(p);
    }
}
