using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public const byte ChangeHairSubDefault = 1;
    public const byte ChangeHairResultOk = 0;
    public const byte ChangeHairResultFail = 1;
    public const byte ChangeHairOpenShop = 2;

    public event Action<bool, int, int>? ChangeHairResultEvent;
    public event Action? BeautyShopEvent;

    private readonly HairChangeRequest _changeHairRequest = new();

    private void HandleChangeHair(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte result = p.ReadByte();
        if (result == ChangeHairOpenShop) { BeautyShopEvent?.Invoke(); return; }
        if (result is not ChangeHairResultOk and not ChangeHairResultFail || !_changeHairRequest.TryFinish()) return;
        if (result == ChangeHairResultOk)
        {
            var me = LastEnter;
            me.Face = _changeHairRequest.Face;
            me.Hair = _changeHairRequest.Hair;
            LastEnter = me;
        }
        ChangeHairResultEvent?.Invoke(result == ChangeHairResultOk, _changeHairRequest.Face, _changeHairRequest.Hair);
    }

    public bool SendChangeHair(int hair, int face)
    {
        if (string.IsNullOrEmpty(LastEnter.Name) || !_changeHairRequest.TryBegin(face, hair)) return false;

        var p = new Packet(GameOpcodes.GS_CHANGE_HAIR);
        p.WriteByte(ChangeHairSubDefault);
        p.WriteSByteString(LastEnter.Name ?? "");
        p.WriteByte((byte)face);
        p.WriteInt(hair);
        _conn.Send(p);
        return true;
    }

    private void ResetChangeHair()
    {
        if (_changeHairRequest.TryFinish())
            ChangeHairResultEvent?.Invoke(false, _changeHairRequest.Face, _changeHairRequest.Hair);
    }
}
