using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<bool>? GenieHammerResult;

    public void SendGenieHammer(byte threshold)
    {
        var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        p.WriteByte(1); p.WriteByte(8); p.WriteByte(threshold);
        _conn.Send(p);
    }

    public bool GenieRunning { get; private set; }
    public event Action<bool, int>? GenieSystemState;
    public event Action<byte[]>? GenieOptionsReceived;

    public void SendGenieSystem(byte command, byte[]? options = null)
    {
        var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        p.WriteByte(1);
        p.WriteByte(command);
        if (command == 3)
            for (int i = 0; i < 100; i++) p.WriteByte(options != null && i < options.Length ? options[i] : (byte)0);
        _conn.Send(p);
    }

    public void ResetGenieSystem() => GenieRunning = false;

    private Packet GenieActionPacket(GameOpcodes opcode, byte action)
    {
        if (!GenieRunning) return new Packet(opcode);
        var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        p.WriteByte(2);
        p.WriteByte(action);
        return p;
    }

    private void HandleGenieSystem(Packet p)
    {
        if (p.RemainingBytes < 2 || p.ReadByte() != 1) return;
        byte command = p.ReadByte();
        if (command == 8)
        {
            if (p.RemainingBytes >= 1) GenieHammerResult?.Invoke(p.ReadByte() == 1);
            return;
        }
        if (command == 2)
        {
            if (p.RemainingBytes < 100) return;
            var options = new byte[100];
            for (int i = 0; i < options.Length; i++) options[i] = p.ReadByte();
            GenieOptionsReceived?.Invoke(options);
            return;
        }
        if (command is 4 or 5)
        {
            if (p.RemainingBytes < 4 || p.ReadUShort() != 1) return;
            int minutes = p.ReadUShort();
            GenieRunning = command == 4 && minutes > 0;
            GenieSystemState?.Invoke(GenieRunning, minutes);
        }
        else if ((command is 1 or 6) && p.RemainingBytes >= 2)
        {
            int minutes = p.ReadUShort();
            if (minutes == 0) GenieRunning = false;
            GenieSystemState?.Invoke(GenieRunning, minutes);
        }
    }
}
