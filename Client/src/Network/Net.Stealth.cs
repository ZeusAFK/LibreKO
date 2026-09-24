using System;

namespace LibreKO.Network;

public partial class Net
{
    public const int InvisibilityInfiltration = 3;

    public event Action<int, int>? StealthEvent;
    public event Action<float>? SightEvent;

    private bool _stealthHooked;

    public void StealthHookEvents()
    {
        if (_stealthHooked) return;
        _stealthHooked = true;
        StateChangeEvent += OnStealthStateChange;
    }

    public void StealthUnhookEvents()
    {
        if (!_stealthHooked) return;
        _stealthHooked = false;
        StateChangeEvent -= OnStealthStateChange;
    }

    private void OnStealthStateChange(int charId, int type, int value)
    {
        if (type == StateChange.Stealth) StealthEvent?.Invoke(charId, value);
    }

    private void HandleStealth(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        bool on = p.ReadByte() != 0;
        short radius = p.ReadShort();
        SightEvent?.Invoke(on ? radius : 0f);
    }

    public void SendStealth()
    {
        var p = new Packet(GameOpcodes.GS_STEALTH);
        p.WriteByte(0);
        _conn.Send(p);
    }
}
