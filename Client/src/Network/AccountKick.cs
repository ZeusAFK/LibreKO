using System;
using Godot;

namespace LibreKO.Network;

public partial class AccountKick : Node
{
    private const double TimeoutSeconds = 8.0;

    private readonly KoConn _conn = new();
    private Action<AccountKickCode?> _done = null!;
    private string _account = "";
    private string _password = "";
    private bool _sent;
    private bool _finished;
    private double _elapsed;

    public static void Request(Node parent, string host, int port, string account, string password,
                               Action<AccountKickCode?> done)
    {
        var node = new AccountKick { _account = account, _password = password, _done = done };
        parent.AddChild(node);
        node._conn.Connect(host, port);
    }

    public override void _Process(double delta)
    {
        if (_finished) return;

        _elapsed += delta;
        if (_elapsed > TimeoutSeconds || _conn.ConnectFailed)
        {
            Finish(null);
            return;
        }

        if (_conn.Connected && !_sent)
        {
            _sent = true;
            var p = new Packet(GameOpcodes.GS_KICKOUT);
            p.WriteString(_account);
            p.WriteString(KoPassword.Encode(_password));
            _conn.Send(p);
        }

        while (_conn.Incoming.TryDequeue(out var packet))
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_KICKOUT) continue;
            Finish(packet.RemainingBytes > 0 ? (AccountKickCode)packet.ReadByte() : AccountKickCode.Done);
            return;
        }

        if (_sent && !_conn.Connected)
            Finish(null);
    }

    private void Finish(AccountKickCode? code)
    {
        _finished = true;
        _conn.Close();
        var done = _done;
        QueueFree();
        done(code);
    }
}
