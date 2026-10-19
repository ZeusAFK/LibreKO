using System.Runtime.CompilerServices;

namespace LibreKO.Network;

internal sealed class PacketDispatcher
{
    private const int OpcodeCount = byte.MaxValue + 1;

    private readonly Action<Packet>?[] whole = new Action<Packet>?[OpcodeCount];
    private readonly SubTable?[] subs = new SubTable?[OpcodeCount];

    private sealed class SubTable
    {
        public readonly Action<Packet>?[] Handlers = new Action<Packet>?[OpcodeCount];
        public Action<Packet, byte>? Others;
    }

    public void On(GameOpcodes opcode, Action<Packet> handler)
    {
        if (whole[(byte)opcode] != null || subs[(byte)opcode] != null)
            throw new InvalidOperationException($"{opcode} already has a reader");
        whole[(byte)opcode] = handler;
    }

    public void On<TSub>(GameOpcodes opcode, TSub sub, Action<Packet> handler) where TSub : unmanaged, Enum
    {
        var table = SubsOf(opcode);
        byte key = SubKey(sub);
        if (table.Handlers[key] != null) throw new InvalidOperationException($"{opcode} {sub} already has a reader");
        table.Handlers[key] = handler;
    }

    public void OnOtherSubs<TSub>(GameOpcodes opcode, Action<Packet, TSub> handler) where TSub : unmanaged, Enum
    {
        var table = SubsOf(opcode);
        if (table.Others != null) throw new InvalidOperationException($"{opcode} already reads its other sub-opcodes");
        if (Unsafe.SizeOf<TSub>() != sizeof(byte)) throw new ArgumentException($"{typeof(TSub).Name} is not a byte enum");
        table.Others = (p, sub) => handler(p, Unsafe.As<byte, TSub>(ref sub));
    }

    public bool Handles(GameOpcodes opcode) => whole[(byte)opcode] != null || subs[(byte)opcode] != null;

    public bool Dispatch(Packet p)
    {
        byte opcode = p.GetOpcode();
        if (whole[opcode] is { } handler)
        {
            handler(p);
            return true;
        }
        if (subs[opcode] is not { } table) return false;
        if (p.RemainingBytes < 1) return true;
        byte sub = p.ReadByte();
        if (table.Handlers[sub] is { } subHandler) subHandler(p);
        else table.Others?.Invoke(p, sub);
        return true;
    }

    private SubTable SubsOf(GameOpcodes opcode)
    {
        if (whole[(byte)opcode] != null) throw new InvalidOperationException($"{opcode} already has a reader");
        return subs[(byte)opcode] ??= new SubTable();
    }

    private static byte SubKey<TSub>(TSub sub) where TSub : unmanaged, Enum
    {
        if (Unsafe.SizeOf<TSub>() != sizeof(byte)) throw new ArgumentException($"{typeof(TSub).Name} is not a byte enum");
        return Unsafe.As<TSub, byte>(ref sub);
    }
}
