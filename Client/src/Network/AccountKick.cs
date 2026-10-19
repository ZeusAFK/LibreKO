using System;
using Godot;

namespace LibreKO.Network;

public partial class AccountKick : Node
{
    private const double TimeoutSeconds = 8.0;

    private readonly GameConn conn = new();
    private Action<AccountKickCode?> done = null!;
    private string account = "";
    private string password = "";
    private bool sent;
    private bool finished;
    private double elapsed;

    public static void Request(Node parent, string host, int port, string account, string password,
                               Action<AccountKickCode?> done)
    {
        var node = new AccountKick { account = account, password = password, done = done };
        parent.AddChild(node);
        node.conn.Connect(host, port);
    }

    public override void _Process(double delta)
    {
        if (finished) return;

        elapsed += delta;
        if (elapsed > TimeoutSeconds || conn.ConnectFailed)
        {
            Finish(null);
            return;
        }

        if (conn.Connected && !sent)
        {
            sent = true;
            var p = new Packet(GameOpcodes.GS_KICKOUT);
            p.WriteString(account);
            p.WriteString(LoginPassword.Encode(password));
            conn.Send(p);
        }

        while (conn.Incoming.TryDequeue(out var packet))
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_KICKOUT) continue;
            Finish(packet.RemainingBytes > 0 ? (AccountKickCode)packet.ReadByte() : AccountKickCode.Done);
            return;
        }

        if (sent && !conn.Connected)
            Finish(null);
    }

    private void Finish(AccountKickCode? code)
    {
        finished = true;
        conn.Close();
        var done = this.done;
        QueueFree();
        done(code);
    }
}
