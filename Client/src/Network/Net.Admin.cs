using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public class AdminCollectionRace
{
    public int Id;
    public string Name = string.Empty;
    public byte ZoneId;
    public byte MinLevel;
    public byte MaxLevel;
    public int DurationMinutes;
    public bool AutoStart;
    public bool Active;
    public int RemainingSeconds;
    public int Completions;
    public string Schedule = string.Empty;
    public string Objectives = string.Empty;
}

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
    private const byte AdminReqSetLevel = 11;
    private const byte AdminReqSetSkill = 12;
    private const byte AdminReqSetLook = 13;

    private const byte AdminAckState = 0x10;
    private const byte AdminAckResult = 0x11;
    private const byte AdminAckGrant = 0x12;
    private const byte AdminReqCollectionRaces = 8;
    private const byte AdminReqCollectionRaceStart = 9;
    private const byte AdminReqCollectionRaceClose = 10;
    private const byte AdminAckCollectionRaces = 0x14;

    public event Action<List<AdminCollectionRace>>? AdminCollectionRacesEvent;

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
            case AdminAckCollectionRaces: ParseAdminCollectionRaces(p); break;
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

        state.Nation = p.RemainingBytes >= 1 ? p.ReadByte() : LastEnter.Nation;
        state.Race = p.RemainingBytes >= 1 ? p.ReadByte() : LastEnter.Race;

        if (state.Class != 0) ApplyOwnClass(state.Class);

        // Keep the identity blob in sync so panel labels reflect the new nation/race.
        // The 3D body model is only rebuilt on world-enter, so it needs a relog to change.
        var info = LastEnter;
        info.Nation = state.Nation;
        info.Race = state.Race;
        LastEnter = info;

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

    public void SendAdminSetClass(int classId)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqSetClass);
        p.WriteShort(IntToShort(classId));
        _conn.Send(p);
    }

    public void SendAdminZone(int zoneId)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqZone);
        p.WriteShort(IntToShort(zoneId));
        _conn.Send(p);
    }

    public void SendAdminSetLevel(int level, bool reset)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqSetLevel);
        p.WriteByte((byte)Math.Clamp(level, 1, 83));
        p.WriteByte((byte)(reset ? 1 : 0));
        _conn.Send(p);
    }

    public void SendAdminSetSkill(int pool, int tree5, int tree6, int tree7, int tree8, bool reset)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqSetSkill);
        p.WriteByte(ClampByte(pool));
        p.WriteByte(ClampByte(tree5));
        p.WriteByte(ClampByte(tree6));
        p.WriteByte(ClampByte(tree7));
        p.WriteByte(ClampByte(tree8));
        p.WriteByte((byte)(reset ? 1 : 0));
        _conn.Send(p);
    }

    public void SendAdminSetLook(int nation, int race)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqSetLook);
        p.WriteByte((byte)Math.Clamp(nation, 0, 255));
        p.WriteByte((byte)Math.Clamp(race, 0, 255));
        _conn.Send(p);
    }

    private void ParseAdminCollectionRaces(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        int count = p.ReadUShort();
        var rows = new List<AdminCollectionRace>(count);
        for (int i = 0; i < count && p.RemainingBytes >= 22; i++)
        {
            rows.Add(new AdminCollectionRace
            {
                Id = p.ReadInt(),
                Name = p.ReadSByteString(),
                ZoneId = p.ReadByte(),
                MinLevel = p.ReadByte(),
                MaxLevel = p.ReadByte(),
                DurationMinutes = p.ReadInt(),
                AutoStart = p.ReadByte() == 1,
                Active = p.ReadByte() == 1,
                RemainingSeconds = p.ReadInt(),
                Completions = p.ReadInt(),
                Schedule = p.ReadSByteString(),
                Objectives = p.ReadSByteString(),
            });
        }
        AdminCollectionRacesEvent?.Invoke(rows);
    }

    public void SendAdminCollectionRacesRequest() => SendAdminByte(AdminReqCollectionRaces);

    public void SendAdminCollectionRaceStart(int raceId) => SendAdminInt(AdminReqCollectionRaceStart, raceId);

    public void SendAdminCollectionRaceClose(int raceId) => SendAdminInt(AdminReqCollectionRaceClose, raceId);

    private void SendAdminInt(byte sub, int value)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(sub);
        p.WriteInt(value);
        _conn.Send(p);
    }

    private void SendAdminByte(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(sub);
        _conn.Send(p);
    }

    private static byte ClampStatByte(int value) => (byte)Math.Clamp(value, 1, 255);

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);
}
