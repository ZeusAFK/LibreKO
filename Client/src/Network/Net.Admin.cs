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
    public int MaxWinners;
    public string Schedule = string.Empty;
    public string Objectives = string.Empty;
}

public class AdminFindHit
{
    public int Id;
    public int SpawnRow;
    public string Name = string.Empty;
    public int Level;
    public int Zone;
    public int X;
    public int Z;
    public bool Monster;
    public bool Bot;
}

public class AdminSpawnRow
{
    public bool CanPersist;
    public int Index;
    public int NpcId;
    public string Name = string.Empty;
    public int Zone;
    public bool Monster;
    public int X;
    public int Z;
    public float Y;
    public int Direction;
    public int Count;
    public int RespawnSeconds;
    public int SpawnRange;
    public int Alive;
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
    private const byte AdminReqFind = 14;
    private const byte AdminReqGo = 15;
    private const byte AdminReqSpawnRow = 16;
    private const byte AdminReqSpawnSet = 17;
    private const byte AdminReqSpawnPersist = 18;
    private const byte AdminKeepProgress = 0;
    private const byte AdminResetProgress = 1;

    private const byte AdminAckState = 0x10;
    private const byte AdminAckResult = 0x11;
    private const byte AdminAckGrant = 0x12;
    private const byte AdminReqCollectionRaces = 8;
    private const byte AdminReqCollectionRaceStart = 9;
    private const byte AdminReqCollectionRaceClose = 10;
    private const byte AdminAckCollectionRaces = 0x14;
    private const byte AdminAckFind = 0x15;
    private const byte AdminAckSpawnRow = 0x16;
    public const int AdminFindNpcs = 0;
    public const int AdminFindMonsters = 1;
    public const int AdminFindPlayers = 2;
    private const int AdminFindMonsterFlag = 1;
    private const int AdminFindBotFlag = 2;
    private const int AdminFindRowMinBytes = 17;
    private const int AdminSpawnRowMinBytes = 31;

    public event Action<List<AdminCollectionRace>>? AdminCollectionRacesEvent;

    public event Action<int, int, List<AdminFindHit>>? AdminFindEvent;

    public event Action<AdminSpawnRow>? AdminSpawnRowEvent;

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
            case AdminAckFind: ParseAdminFind(p); break;
            case AdminAckSpawnRow: ParseAdminSpawnRow(p); break;
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
        state.Loyalty = p.RemainingBytes >= 4 ? p.ReadInt() : 0;

        if (state.Class != 0) ApplyOwnClass(state.Class);

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

    public void SendAdminStats(int str, int sta, int dex, int intel, int magic, int statPoints, int loyalty)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqStats);
        p.WriteByte(ClampStatByte(str));
        p.WriteByte(ClampStatByte(sta));
        p.WriteByte(ClampStatByte(dex));
        p.WriteByte(ClampStatByte(intel));
        p.WriteByte(ClampStatByte(magic));
        p.WriteShort(IntToShort(Math.Max(0, statPoints)));
        p.WriteInt(Math.Max(0, loyalty));
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
        p.WriteByte((byte)Math.Clamp(level, CharacterSheet.MinLevel, CharacterSheet.MaxLevel));
        p.WriteByte(reset ? AdminResetProgress : AdminKeepProgress);
        _conn.Send(p);
    }

    public void SendAdminSetSkill(int pool, IReadOnlyList<int> trees, bool reset)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqSetSkill);
        p.WriteByte(ClampByte(pool));
        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
        {
            int index = tree - MasteryPoints.FirstTree;
            p.WriteByte(ClampByte(index < trees.Count ? trees[index] : 0));
        }
        p.WriteByte(reset ? AdminResetProgress : AdminKeepProgress);
        _conn.Send(p);
    }

    public void SendAdminSetLook(int nation, int race)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqSetLook);
        p.WriteByte(ClampByte(nation));
        p.WriteByte(ClampByte(race));
        _conn.Send(p);
    }

    private const int AdminCollectionRaceRowMinBytes = 26;

    private void ParseAdminCollectionRaces(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        int count = p.ReadUShort();
        var rows = new List<AdminCollectionRace>(count);
        for (int i = 0; i < count && p.RemainingBytes >= AdminCollectionRaceRowMinBytes; i++)
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
                MaxWinners = p.ReadInt(),
                Schedule = p.ReadSByteString(),
                Objectives = p.ReadSByteString(),
            });
        }
        AdminCollectionRacesEvent?.Invoke(rows);
    }

    private void ParseAdminFind(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int kind = p.ReadByte();
        int total = p.ReadUShort();
        int count = p.ReadUShort();
        var hits = new List<AdminFindHit>(count);
        for (int i = 0; i < count && p.RemainingBytes >= AdminFindRowMinBytes; i++)
        {
            var hit = new AdminFindHit { Id = p.ReadInt(), SpawnRow = p.ReadInt(), Name = p.ReadSByteString() };
            hit.Level = p.ReadShort();
            hit.Zone = p.ReadByte();
            hit.X = p.ReadUShort();
            hit.Z = p.ReadUShort();
            int flags = p.ReadByte();
            hit.Monster = (flags & AdminFindMonsterFlag) != 0;
            hit.Bot = (flags & AdminFindBotFlag) != 0;
            hits.Add(hit);
        }
        AdminFindEvent?.Invoke(kind, total, hits);
    }

    private void ParseAdminSpawnRow(Packet p)
    {
        if (p.RemainingBytes < AdminSpawnRowMinBytes) return;
        var row = new AdminSpawnRow { CanPersist = p.ReadByte() == 1, Index = p.ReadInt(), NpcId = p.ReadInt(), Name = p.ReadSByteString() };
        row.Zone = p.ReadByte();
        row.Monster = p.ReadByte() == 1;
        row.X = p.ReadInt();
        row.Z = p.ReadInt();
        row.Y = p.ReadInt() / 10f;
        row.Direction = p.ReadInt();
        row.Count = p.ReadByte();
        row.RespawnSeconds = p.ReadShort();
        row.SpawnRange = p.ReadShort();
        row.Alive = p.ReadInt();
        AdminSpawnRowEvent?.Invoke(row);
    }

    public void SendAdminSpawnRowRequest(int row) => SendAdminInt(AdminReqSpawnRow, row);

    public void SendAdminSpawnEdit(bool persist, int row, int x, int z, int direction, int count, int respawnSeconds, int range)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(persist ? AdminReqSpawnPersist : AdminReqSpawnSet);
        p.WriteInt(row);
        p.WriteInt(x);
        p.WriteInt(z);
        p.WriteInt(direction);
        p.WriteByte((byte)Math.Clamp(count, 0, byte.MaxValue));
        p.WriteShort((short)Math.Clamp(respawnSeconds, 0, short.MaxValue));
        p.WriteShort((short)Math.Clamp(range, 0, short.MaxValue));
        _conn.Send(p);
    }

    public void SendAdminFind(int kind, string query)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqFind);
        p.WriteByte((byte)kind);
        p.WriteUtf8String(query);
        _conn.Send(p);
    }

    public void SendAdminGo(int zone, int x, int z)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(AdminReqGo);
        p.WriteByte((byte)zone);
        p.WriteUShort((ushort)Math.Clamp(x, 0, ushort.MaxValue));
        p.WriteUShort((ushort)Math.Clamp(z, 0, ushort.MaxValue));
        _conn.Send(p);
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

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
}
