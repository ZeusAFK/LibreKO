namespace LibreKO.Network.Readers;

internal sealed class ClientSettingsReader : IPacketReader
{
    private readonly ClientSettingsSession clientSettings;

    public ClientSettingsReader(ClientSettingsSession clientSettings)
    {
        this.clientSettings = clientSettings;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CLIENT_SETTINGS, HandleClientSettings);
    }

    private void HandleClientSettings(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ClientSettingsSubOpcode)p.ReadByte();
        if (sub != ClientSettingsSubOpcode.SetLanguage) return;

        bool accepted = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
        byte raw = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)GameLanguage.English;
        clientSettings.RaiseLanguage(accepted, LibreKOProtocol.ToLanguage(raw));
    }
}
