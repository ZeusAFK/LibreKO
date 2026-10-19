namespace LibreKO.Network.Requests;

public interface IPreGameRequests
{
    void VersionCheck();
    void Login(string account, string password);
    void RetryLogin();
    void KickOut();
    void CharacterList();
    void SelectNation(int nation);
    void CreateCharacter(int slot, string name, int race, int cls, int face, int hair,
        int str, int sta, int dex, int intel, int mag);
    void SelectCharacter(string name);
    void ChangeRequiredName(int slot, string currentName, string newName);
    void ResendSelect();
    void GameStart(GameStartSubOpcode sub);
    void Ready();
}

public sealed class PreGameRequests : IPreGameRequests
{
    private const byte LoadingLogin = 1;
    private const byte SelectFromList = 1;

    private readonly IPacketSink sink;
    private readonly SelfSession self;
    private readonly LoginState login;

    internal PreGameRequests(IPacketSink sink, SelfSession self, LoginState login)
    {
        this.sink = sink;
        this.self = self;
        this.login = login;
    }

    public void VersionCheck()
    {
        var p = new Packet(GameOpcodes.GS_VERSION_CHECK);
        p.WriteUShort((ushort)Config.ServerVersion);
        p.WriteString(Build.Version);
        sink.Send(p);
    }

    public void Login(string account, string password)
    {
        login.Remember(account, password);
        var p = new Packet(GameOpcodes.GS_LOGIN);
        p.WriteString(account);
        p.WriteString(LoginPassword.Encode(password));
        sink.Send(p);
    }

    public void RetryLogin() => Login(login.Account, login.Password);

    public void KickOut()
    {
        var p = new Packet(GameOpcodes.GS_KICKOUT);
        p.WriteString(login.Account);
        p.WriteString(LoginPassword.Encode(login.Password));
        sink.Send(p);
    }

    public void CharacterList()
    {
        var loading = new Packet(GameOpcodes.GS_LOADING_LOGIN);
        loading.WriteByte(LoadingLogin);
        sink.Send(loading);

        var p = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        p.WriteByte((byte)AllCharacterInfoOpcode.CharacterList);
        sink.Send(p);
    }

    public void SelectNation(int nation)
    {
        var p = new Packet(GameOpcodes.GS_NATION_SELECT);
        p.WriteByte((byte)nation);
        sink.Send(p);
    }

    public void CreateCharacter(int slot, string name, int race, int cls, int face, int hair,
            int str, int sta, int dex, int intel, int mag)
    {
        var p = new Packet(GameOpcodes.GS_CREATE_CHARACTER);
        p.WriteByte((byte)slot);
        p.WriteString(name);
        p.WriteByte((byte)race);
        p.WriteShort((short)cls);
        p.WriteByte((byte)face);
        p.WriteInt(hair);
        p.WriteByte((byte)str);
        p.WriteByte((byte)sta);
        p.WriteByte((byte)dex);
        p.WriteByte((byte)intel);
        p.WriteByte((byte)mag);
        sink.Send(p);
    }

    public void SelectCharacter(string name)
    {
        self.ApplySelectedCharacter(name);
        login.SelectRetries = 0;
        ResendSelect();
    }

    public void ChangeRequiredName(int slot, string currentName, string newName)
    {
        var p = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        p.WriteByte((byte)AllCharacterInfoOpcode.NameChange);
        p.WriteShort((short)slot);
        p.WriteString(currentName);
        p.WriteString(newName);
        sink.Send(p);
    }

    public void ResendSelect()
    {
        var p = new Packet(GameOpcodes.GS_SELECT_CHARACTER);
        p.WriteString(login.Account);
        p.WriteString(self.SelectedChar);
        p.WriteByte(SelectFromList);
        sink.Send(p);
    }

    public void GameStart(GameStartSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_GAMESTART);
        p.WriteByte((byte)sub);
        p.WriteByte((byte)(Config.TradeRequestsBlocked ? 1 : 0));
        p.WriteSByteString(self.SelectedChar);
        sink.Send(p);
    }

    public void Ready() => GameStart(GameStartSubOpcode.Ready);
}
