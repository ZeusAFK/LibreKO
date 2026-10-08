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
    public const byte NationTransferErrorBox = 16;
    public const byte NationTransferWarRunning = 8;

    public event Action<IReadOnlyList<NationTransferCandidate>>? NationTransferOpenEvent;
    public event Action<int>? NationTransferRefusedEvent;
    public event Action? NationTransferDoneEvent;
    public event Action<int, int>? NationTransferWarEvent;
    public event Action? NationTransferResetEvent;

    private int _nationTransferTarget;
    private bool _nationTransferPending;
    private IReadOnlyList<NationTransferCandidate> _nationTransferCandidates = Array.Empty<NationTransferCandidate>();

    private void HandleNationTransfer(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        byte sub = p.ReadByte();
        byte result = p.ReadByte();
        switch (sub)
        {
            case NationTransferOpenBox when result == NationTransferAccepted:
                if (_nationTransferPending) return;
                var candidates = NationTransferWire.ReadCandidates(p);
                if (candidates == null) return;
                _nationTransferCandidates = candidates;
                _nationTransferTarget = candidates.Count == 0 ? 0 : candidates[0].Nation;
                NationTransferOpenEvent?.Invoke(candidates);
                break;
            case NationTransferSubmit when result == NationTransferAccepted:
                if (!_nationTransferPending) return;
                Nation = _nationTransferTarget;
                ClearNationTransfer();
                NationTransferDoneEvent?.Invoke();
                break;
            case NationTransferWarStatus or NationTransferErrorBox when result == NationTransferWarRunning && p.RemainingBytes >= 2:
                _nationTransferPending = false;
                NationTransferWarEvent?.Invoke(p.ReadByte(), p.ReadByte());
                break;
            case NationTransferWarStatus or NationTransferOpenBox or NationTransferSubmit or NationTransferErrorBox:
                if (!NationTransferWire.IsRefusal(result)) return;
                if (sub == NationTransferSubmit && !_nationTransferPending) return;
                _nationTransferPending = false;
                NationTransferRefusedEvent?.Invoke(result);
                break;
        }
    }

    public bool SendNationTransfer(IReadOnlyList<NationTransferPick> picks)
    {
        if (_nationTransferPending || _nationTransferTarget == 0
            || !NationTransferWire.PicksMatch(_nationTransferCandidates, picks)) return false;
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
        _nationTransferPending = true;
        _conn.Send(p);
        return true;
    }

    public void SendNationTransferCancel()
    {
        if (_nationTransferPending) return;
        ClearNationTransfer();
        var p = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        p.WriteByte(NationTransferSubmit);
        p.WriteByte(0);
        _conn.Send(p);
    }

    private void ClearNationTransfer()
    {
        _nationTransferPending = false;
        _nationTransferTarget = 0;
        _nationTransferCandidates = Array.Empty<NationTransferCandidate>();
    }

    private void ResetNationTransfer()
    {
        bool hadOperation = _nationTransferPending || _nationTransferTarget != 0;
        ClearNationTransfer();
        if (hadOperation) NationTransferResetEvent?.Invoke();
    }
}
