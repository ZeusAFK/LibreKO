using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<NationTaxStatus>? NationTaxStatusEvent;

    private void HandleNationTax(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            byte sellPct = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
            byte zonePct = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
            int treasury = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            NationTaxStatusEvent?.Invoke(new NationTaxStatus
            {
                NationTaxSellPct = sellPct,
                NationTaxZonePct = zonePct,
                NationTaxTreasury = treasury,
            });
        }
    }

    public void SendNationTaxStatus()
    {
        var p = new Packet(GameOpcodes.GS_NATION_TAX);
        p.WriteByte(1);
        _conn.Send(p);
    }
}

public struct NationTaxStatus
{
    public byte NationTaxSellPct;
    public byte NationTaxZonePct;
    public int NationTaxTreasury;
}
