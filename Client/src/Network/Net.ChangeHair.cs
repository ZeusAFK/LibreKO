using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte ChangeHairSubDefault = 1;
    public const byte ChangeHairResultOk = 0;
    public const byte ChangeHairResultFail = 1;
    public const byte ChangeHairOpenShop = 2;

    public event Action<bool, int, int>? ChangeHairResultEvent;
    public event Action? BeautyShopEvent;

    private int _changeHairReqFace;
    private int _changeHairReqHair;

    private void HandleChangeHair(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte result = p.ReadByte();
        if (result == ChangeHairOpenShop) { BeautyShopEvent?.Invoke(); return; }
        ChangeHairResultEvent?.Invoke(result == ChangeHairResultOk, _changeHairReqFace, _changeHairReqHair);
    }

    public void SendChangeHair(int hair, int face)
    {
        _changeHairReqFace = face;
        _changeHairReqHair = hair;

        var p = new Packet(GameOpcodes.GS_CHANGE_HAIR);
        p.WriteByte(ChangeHairSubDefault);
        p.WriteSByteString(LastEnter.Name ?? "");
        p.WriteByte((byte)face);
        p.WriteInt(hair);
        _conn.Send(p);
    }
}
