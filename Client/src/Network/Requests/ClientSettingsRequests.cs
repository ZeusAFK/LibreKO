namespace LibreKO.Network.Requests;

public interface IClientSettingsRequests
{
    void LanguageRequest();
    void Language(GameLanguage language);
}

public sealed class ClientSettingsRequests : IClientSettingsRequests
{
    private readonly IPacketSink sink;

    public ClientSettingsRequests(IPacketSink sink) => this.sink = sink;

    public void LanguageRequest()
    {
        var p = new Packet(GameOpcodes.GS_CLIENT_SETTINGS);
        p.WriteByte((byte)ClientSettingsSubOpcode.GetLanguage);
        sink.Send(p);
    }

    public void Language(GameLanguage language)
    {
        var p = new Packet(GameOpcodes.GS_CLIENT_SETTINGS);
        p.WriteByte((byte)ClientSettingsSubOpcode.SetLanguage);
        p.WriteByte((byte)language);
        sink.Send(p);
    }
}
