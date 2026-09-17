using System;

namespace LibreKO.Network;

public partial class Net
{
    private const string EvictedMessage =
        "You were disconnected because this account signed in from somewhere else.";

    public event Action<AccountInUse>? AccountInUseEvent;
    public event Action<AccountKickCode>? KickResultEvent;

    public bool Evicted { get; private set; }

    private bool _reconnectKickSent;

    public void SendKickOut()
    {
        var p = new Packet(GameOpcodes.GS_KICKOUT);
        p.WriteString(_account);
        p.WriteString(KoPassword.Encode(_password));
        _conn.Send(p);
    }

    public void RetryLogin() => Login(_account, _password);

    private void HandleKickOut(Packet p)
    {
        var code = p.RemainingBytes > 0 ? (AccountKickCode)p.ReadByte() : AccountKickCode.Evicted;

        if (code == AccountKickCode.Evicted)
        {
            Evicted = true;
            AutoReconnect = false;
            CancelReconnect();
            MyCharId = 0;
            Disconnect(expected: true);
            LibreKO.Login.PendingNotice = EvictedMessage;
            GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
            return;
        }

        if (_reconnectKickSent && ReconnectState is not (ReconnectPhase.Idle or ReconnectPhase.Failed))
        {
            RetryLogin();
            return;
        }

        KickResultEvent?.Invoke(code);
    }

    private bool HandleLoginDenied(Packet p)
    {
        var occupant = ReadAccountInUse(p);
        if (occupant == null)
            return false;

        if (ReconnectState is not (ReconnectPhase.Idle or ReconnectPhase.Failed))
        {
            if (_reconnectKickSent)
                GiveUpReconnect("Your account has been connected from somewhere else.");
            else
            {
                _reconnectKickSent = true;
                SendKickOut();
            }
            return true;
        }

        AccountInUseEvent?.Invoke(occupant);
        return true;
    }

    private static AccountInUse? ReadAccountInUse(Packet p)
    {
        const int headerBytes = 6;
        if (p.RemainingBytes < headerBytes)
            return null;

        try
        {
            if (p.ReadUInt() != LibreKOProtocol.AccountLockMagic
                || p.ReadByte() != LibreKOProtocol.ExtensionVersion
                || p.ReadByte() != (byte)GameLoginDenial.AccountInUse)
                return null;

            return new AccountInUse
            {
                Stage = (AccountClaimStage)p.ReadByte(),
                ServerId = p.ReadShort(),
                ServerName = p.ReadString(),
                CharacterName = p.ReadString(),
            };
        }
        catch (Exception e)
        {
            Diag.Report("account-in-use parse", e);
            return null;
        }
    }
}
