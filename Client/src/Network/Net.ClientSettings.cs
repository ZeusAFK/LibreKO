using System;

namespace LibreKO.Network;

public partial class Net
{
    private const byte ClientSettingsSubGetLanguage = 1;
    private const byte ClientSettingsSubSetLanguage = 2;

    public event Action<bool, GameLanguage>? LanguageEvent;

    private void HandleClientSettings(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub != ClientSettingsSubSetLanguage) return;

        bool accepted = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
        byte raw = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)GameLanguage.English;
        LanguageEvent?.Invoke(accepted, LibreKOProtocol.ToLanguage(raw));
    }

    public void SendLanguageRequest()
    {
        var p = new Packet(GameOpcodes.GS_CLIENT_SETTINGS);
        p.WriteByte(ClientSettingsSubGetLanguage);
        _conn.Send(p);
    }

    public void SendLanguage(GameLanguage language)
    {
        var p = new Packet(GameOpcodes.GS_CLIENT_SETTINGS);
        p.WriteByte(ClientSettingsSubSetLanguage);
        p.WriteByte((byte)language);
        _conn.Send(p);
    }
}
