using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public enum AdminPanelGrant : byte
{
    None = 0,
    GameMaster = 1,
    PublicDemo = 2,
}

public partial class Net
{
    private const byte AdminReqState = 1;
    private const byte AdminReqCoins = 2;
    private const byte AdminReqStats = 3;
    private const byte AdminReqGiveItem = 4;
    private const byte AdminReqSetClass = 5;
    private const byte AdminReqZone = 6;

    private const byte AdminAckState = 0x10;
    private const byte AdminAckResult = 0x11;
    private const byte AdminAckGrant = 0x12;

    public event Action<AdminState>? AdminStateEvent;

    public event Action<bool, string>? AdminResultEvent;

    public event Action<AdminPanelGrant>? AdminGrantEvent;

    public AdminPanelGrant PanelGrant { get; private set; }

    public bool GmSpeedGranted { get; private set; }

    private void HandleAdminPanel(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case AdminAckState: ParseAdminState(p); break;
            case 0x13: HandleGmFx(p); break;
            case AdminAckResult:
                bool ok = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                AdminResultEvent?.Invoke(ok, p.RemainingBytes >= 2 ? p.ReadString() : "");
                break;
            case AdminAckGrant:
                PanelGrant = p.RemainingBytes >= 1 ? (AdminPanelGrant)p.ReadByte() : AdminPanelGrant.None;
                GmSpeedGranted = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                AdminGrantEvent?.Invoke(PanelGrant);
                break;
        }
    }

    private void ParseAdminState(Packet p)
    {
        var state = new AdminState
        {
            Granted = p.RemainingBytes >= 1 && p.ReadByte() == 1,
            SkillPoints = new byte[9],
            ClassOptions = Array.Empty<int>(),
        };
        if (!state.Granted || p.RemainingBytes < 25)
        {
            state.Granted = false;
            AdminStateEvent?.Invoke(state);
            return;
        }

        state.Class = p.ReadShort();
        state.Race = p.ReadByte();
        state.Level = p.ReadByte();
        state.Str = p.ReadByte();
        state.Sta = p.ReadByte();
        state.Dex = p.ReadByte();
        state.Intel = p.ReadByte();
        state.MagicStat = p.ReadByte();
        state.StatPoints = p.ReadShort();
        state.MaxHp = p.ReadShort();
        state.MaxMp = p.ReadShort();
        state.Ap = p.ReadShort();
        state.Ac = p.ReadShort();
        state.Gold = p.ReadInt();
        for (int i = 0; i < 9 && p.RemainingBytes >= 1; i++)
            state.SkillPoints[i] = p.ReadByte();

        int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        var options = new int[Math.Min(count, p.RemainingBytes / 2)];
        for (int i = 0; i < options.Length; i++)
            options[i] = p.ReadShort();
        state.ClassOptions = options;

        if (p.RemainingBytes >= 1) state.Face = p.ReadByte();
        if (p.RemainingBytes >= 4) state.Hair = p.ReadInt();

        if (state.Class != 0)
        {
            ApplyOwnClass(state.Class);
            ApplyOwnNation(state.Class < 200 ? Nations.Karus : Nations.ElMorad);
        }
        if (state.Race != 0) ApplyOwnRace(state.Race);

        AdminStateEvent?.Invoke(state);
    }

    public void SendAdminStateRequest() => SendAdminByte(AdminReqState);

    public void SendAdminCoins(int amount)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqCoins);
        p.WriteInt(amount);
        _conn.Send(p);
    }

    public void SendAdminStats(int str, int sta, int dex, int intel, int magic, int statPoints)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqStats);
        p.WriteByte(ClampStatByte(str));
        p.WriteByte(ClampStatByte(sta));
        p.WriteByte(ClampStatByte(dex));
        p.WriteByte(ClampStatByte(intel));
        p.WriteByte(ClampStatByte(magic));
        p.WriteShort(IntToShort(Math.Max(0, statPoints)));
        _conn.Send(p);
    }

    public void SendAdminGiveItem(int itemId, int count)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqGiveItem);
        p.WriteInt(itemId);
        p.WriteShort(IntToShort(Math.Clamp(count, 1, 9999)));
        _conn.Send(p);
    }

    public void SendAdminSetClass(int classId, int race = 0, int face = -1, int hair = -1)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqSetClass);
        p.WriteShort(IntToShort(classId));
        p.WriteByte((byte)Math.Max(0, race));
        p.WriteByte(face >= 0 ? (byte)face : (byte)255);
        p.WriteInt(hair);
        _conn.Send(p);
    }

    public void SendAdminZone(int zoneId)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqZone);
        p.WriteShort(IntToShort(zoneId));
        _conn.Send(p);
    }

    private void SendAdminByte(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(sub);
        _conn.Send(p);
    }

    public void SendGmCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        var msg = command.Trim();
        if (!msg.StartsWith('+')) msg = "+" + msg;
        SendChat(msg);
    }

    public void SendOperatorCommand(byte opcode, string targetName)
    {
        var p = new Packet(GameOpcodes.GS_OPERATOR);
        p.WriteByte(opcode);
        p.WriteSByteString(targetName ?? string.Empty);
        _conn.Send(p);
    }

    private static byte ClampStatByte(int value) => (byte)Math.Clamp(value, 1, 255);
}
