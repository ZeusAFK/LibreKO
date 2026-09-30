using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte GenieInfoRequest = 1;
    public const byte GenieUpdateRequest = 2;
    public const byte GenieUseSpiritPotion = 1;
    public const byte GenieLoadOptions = 2;
    public const byte GenieSaveOptions = 3;
    public const byte GenieStart = 4;
    public const byte GenieStop = 5;
    public const byte GenieRemainingTime = 6;
    public const byte GenieUseHammer = 8;
    public const byte GenieMove = 1;
    public const byte GenieRotate = 2;
    public const byte GenieMainAttack = 3;
    public const byte GenieMagic = 4;
    public const byte GenieAcknowledged = 1;
    public const byte GenieActive = 1;
    public const int GenieOptionBytes = 100;

    public event Action<bool>? GenieHammerResult;

    public void SendGenieHammer(byte threshold)
    {
        var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        p.WriteByte(GenieInfoRequest); p.WriteByte(GenieUseHammer); p.WriteByte(threshold);
        _conn.Send(p);
    }

    public bool GenieRunning { get; private set; }
    public event Action<bool, int>? GenieSystemState;
    public event Action<byte[]>? GenieOptionsReceived;

    public void SendGenieSystem(byte command, byte[]? options = null)
    {
        var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        p.WriteByte(GenieInfoRequest);
        p.WriteByte(command);
        if (command == GenieSaveOptions)
            for (int i = 0; i < GenieOptionBytes; i++) p.WriteByte(options != null && i < options.Length ? options[i] : (byte)0);
        _conn.Send(p);
    }

    public void ResetGenieSystem() => GenieRunning = false;

    private Packet GenieActionPacket(GameOpcodes opcode, byte action)
    {
        if (!GenieRunning) return new Packet(opcode);
        var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        p.WriteByte(GenieUpdateRequest);
        p.WriteByte(action);
        return p;
    }

    private void HandleGenieSystem(Packet p)
    {
        if (p.RemainingBytes < 2 || p.ReadByte() != GenieInfoRequest) return;
        byte command = p.ReadByte();
        if (command == GenieUseHammer)
        {
            if (p.RemainingBytes >= 1) GenieHammerResult?.Invoke(p.ReadByte() == GenieActive);
            return;
        }
        if (command == GenieLoadOptions)
        {
            if (p.RemainingBytes < GenieOptionBytes) return;
            var options = new byte[GenieOptionBytes];
            for (int i = 0; i < options.Length; i++) options[i] = p.ReadByte();
            GenieOptionsReceived?.Invoke(options);
            return;
        }
        if (command is GenieStart or GenieStop)
        {
            if (p.RemainingBytes < 4 || p.ReadUShort() != GenieAcknowledged) return;
            int minutes = p.ReadUShort();
            GenieRunning = command == GenieStart && minutes > 0;
            GenieSystemState?.Invoke(GenieRunning, minutes);
        }
        else if ((command is GenieUseSpiritPotion or GenieRemainingTime) && p.RemainingBytes >= 2)
        {
            int minutes = p.ReadUShort();
            if (minutes == 0) GenieRunning = false;
            GenieSystemState?.Invoke(GenieRunning, minutes);
        }
    }
}
