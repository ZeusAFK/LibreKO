using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte RentalNpcSub = 3;
    private const int RentalNpcStateLength = 6;

    public event Action? RentalUnavailableEvent;

    private void HandleRental(Packet p)
    {
        if (p.RemainingBytes < 1 || p.ReadByte() != RentalNpcSub || p.RemainingBytes < RentalNpcStateLength) return;
        short state = p.ReadShort();
        p.ReadInt();
        if (state < 0) RentalUnavailableEvent?.Invoke();
    }
}
