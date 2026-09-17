using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte NameChangeCharSub = 0;
    public const byte NameChangeClanSub = 16;
    public const byte NameChangeShowDialog = 1;
    public const byte NameChangeInvalid = 2;
    public const byte NameChangeSuccess = 3;
    public const byte NameChangeInClan = 4;

    public const byte ClanNameChangeShowDialog = 1;
    public const byte ClanNameChangeInvalid = 2;
    public const byte ClanNameChangeNotInClan = 4;
    public const byte ClanNameChangeSuccess = 16;

    public event Action<int>? NameChangeResultEvent;
    public event Action<string>? NameChangeSuccessEvent;
    public event Action<int>? ClanNameChangeResultEvent;
    public event Action<string>? ClanRenamedEvent;

    private void HandleNameChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte code = p.ReadByte();

        if (code == NameChangeClanSub)
        {
            HandleClanNameChange(p);
            return;
        }

        if (code == NameChangeSuccess)
            NameChangeSuccessEvent?.Invoke("");
        else
            NameChangeResultEvent?.Invoke(code);
    }

    private void HandleClanNameChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte code = p.ReadByte();

        if (code != ClanNameChangeSuccess)
        {
            ClanNameChangeResultEvent?.Invoke(code);
            return;
        }

        ClanRenamedEvent?.Invoke(p.RemainingBytes >= 2 ? p.ReadString() : "");
    }

    public void SendNameChangeRequest()
    {
        var p = new Packet(GameOpcodes.GS_NAME_CHANGE);
        p.WriteByte(NameChangeCharSub);
        _conn.Send(p);
    }

    public void SendNameChangeConfirm(string newName)
    {
        var p = new Packet(GameOpcodes.GS_NAME_CHANGE);
        p.WriteByte(NameChangeCharSub);
        p.WriteString(newName);
        _conn.Send(p);
    }
}
