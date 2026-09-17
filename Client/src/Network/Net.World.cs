
namespace LibreKO.Network;

public partial class Net
{
    public (int Hour, int Minute)? LastTime { get; private set; }
    public (int Type, int Amount)? LastWeather { get; private set; }

    private bool _zoneChanging;
    public bool ConsumeZoneChange()
    {
        var v = _zoneChanging;
        _zoneChanging = false;
        return v;
    }

    public const byte MoveEchoFinish = 0;

    private void HandleMove(Packet p)
    {
        int id = p.ReadInt();
        float x = p.ReadUShort() / 10f;
        float z = p.ReadUShort() / 10f;
        float y = p.ReadUShort() / 10f;
        short speed = p.ReadShort();
        byte echo = p.RemainingBytes >= 1 ? p.ReadByte() : MoveEchoFinish;
        EntityMoveEvent?.Invoke(id, x, z, y, speed / 10f, echo != MoveEchoFinish);
    }

    private void HandleRotate(Packet p)
    {
        int id = p.ReadInt();
        EntityRotateEvent?.Invoke(id, Coord.HeadingFromWire(p.ReadShort()));
    }

    private void HandleWarp(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        float wx = p.ReadUShort() / 10f;
        float wz = p.ReadUShort() / 10f;
        WarpEvent?.Invoke(wx, wz);
    }

    private void HandleZoneChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte cmd = p.ReadByte();
        if (cmd == 3 && p.RemainingBytes >= 10)
        {
            int zone = p.ReadShort();
            p.ReadShort();
            float x = p.ReadUShort() / 10f;
            float z = p.ReadUShort() / 10f;
            float y = p.ReadUShort() / 10f;
            var e = LastEnter; e.Zone = zone; e.X = x; e.Z = z; e.Y = y; LastEnter = e;
            _known.Clear();
            ForgetKnownStalls();
            _zoneChanging = true;
            GetTree().ChangeSceneToFile(WorldScene);
        }
        else if (cmd == 2)
        {
            SendZoneFinish();
        }
    }

    private void HandleUserInOut(Packet p)
    {
        byte type = p.ReadByte();
        p.ReadByte();
        int id = p.ReadInt();
        if (type == 2)
            RaiseOut(id);
        else
            RaiseSpawn(ReadUserInfo(p, id));
    }

    private void HandleNpcInOut(Packet p)
    {
        byte type = p.ReadByte();
        int id = p.ReadInt();
        if (type == 2)
            RaiseOut(id);
        else
            RaiseSpawn(ReadNpcInfo(p, id));
    }

    private void HandleNpcMove(Packet p)
    {
        p.ReadByte();
        int id = p.ReadInt();
        float x = p.ReadShort() / 10f;
        float z = p.ReadShort() / 10f;
        float y = p.ReadShort() / 10f;
        ushort rate = p.ReadUShort();
        EntityMoveEvent?.Invoke(id, x, z, y, rate / 10f, true);
    }

    private void HandleTime(Packet p)
    {
        p.ReadShort(); p.ReadShort(); p.ReadShort();
        int hour = p.ReadShort();
        int minute = p.ReadShort();
        LastTime = (hour, minute);
        TimeEvent?.Invoke(hour, minute);
    }

    private void HandleWeather(Packet p)
    {
        int wtype = p.ReadByte();
        int amount = p.ReadUShort();
        LastWeather = (wtype, amount);
        WeatherEvent?.Invoke(wtype, amount);
    }

    private const byte NoticeStyleLogin = 2;

    private void HandleNotice(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        byte style = p.ReadByte();
        int count = p.ReadByte();

        for (int i = 0; i < count && p.RemainingBytes > 0; i++)
        {
            string line = style == NoticeStyleLogin
                ? NoticeEntry(p)
                : p.ReadSByteString();

            if (line.Length > 0) NoticeEvent?.Invoke(line);
        }
    }

    private static string NoticeEntry(Packet p)
    {
        string title = p.ReadString();
        string message = p.RemainingBytes >= 2 ? p.ReadString() : "";
        return title.Length > 0 && message.Length > 0 ? $"{title}: {message}" : title + message;
    }

    private void ParseRegionUserList(Packet p)
    {
        byte sub = p.ReadByte();
        if (sub != 1) return;
        int count = p.ReadUShort();
        var visible = new HashSet<int>();
        for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            visible.Add(p.ReadInt());

        DropUnlistedPlayers(visible);
        if (visible.Count == 0) return;

        var req = new Packet(GameOpcodes.GS_REQ_USERIN);
        req.WriteShort((short)visible.Count);
        foreach (int id in visible)
            req.WriteInt(id);
        _conn.Send(req);
    }

    // The list is the whole set of visible players, so anything else we hold is a lost-INOUT_OUT ghost.
    private void DropUnlistedPlayers(HashSet<int> visible)
    {
        List<int>? stale = null;
        foreach (var known in _known.Values)
        {
            if (known.IsNpc || known.Id == MyCharId || visible.Contains(known.Id)) continue;
            (stale ??= new List<int>()).Add(known.Id);
        }

        if (stale == null) return;
        foreach (int id in stale)
            RaiseOut(id);
    }

    private void ParseNpcRegionList(Packet p)
    {
        int count = p.ReadShort();
        if (count <= 0) return;
        var req = new Packet(GameOpcodes.GS_REQ_NPCIN);
        req.WriteShort((short)count);
        for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            req.WriteInt(p.ReadInt());
        _conn.Send(req);
    }

    private void ParseUserSnapshot(Packet p)
    {
        int count = p.ReadShort();
        for (int i = 0; i < count && p.RemainingBytes > 0; i++)
        {
            p.ReadByte();
            int id = p.ReadInt();
            RaiseSpawn(ReadUserInfo(p, id));
        }
    }

    private void ParseNpcSnapshot(Packet p)
    {
        int count = p.ReadShort();
        for (int i = 0; i < count && p.RemainingBytes > 0; i++)
        {
            int id = p.ReadInt();
            RaiseSpawn(ReadNpcInfo(p, id));
        }
    }

    private static EntitySnapshot ReadUserInfo(Packet p, int id)
    {
        var e = new EntitySnapshot { Id = id, IsNpc = false };
        e.Name = p.ReadSByteString();
        e.Nation = p.ReadByte();
        p.ReadByte(); p.ReadByte(); p.ReadByte();
        short knightsId = p.ReadShort();
        e.KnightsId = knightsId;
        p.ReadByte();
        if (knightsId > 0)
        {
            p.ReadShort();
            e.ClanName = p.ReadSByteString();
            e.ClanGrade = p.ReadByte();
            p.ReadByte();
            p.ReadShort();
            e.CapeId = p.ReadShort();
            e.CapeR = p.ReadByte(); e.CapeG = p.ReadByte(); e.CapeB = p.ReadByte();
            p.ReadByte(); p.ReadByte();
        }
        else
        {
            p.ReadInt(); p.ReadShort(); p.ReadByte();
            e.CapeId = p.ReadUShort();
            p.ReadInt(); p.ReadByte();
        }
        e.Level = p.ReadByte();
        e.Race = p.ReadByte();
        e.Class = p.ReadShort();
        e.X = p.ReadShort() / 10f;
        e.Z = p.ReadShort() / 10f;
        e.Y = p.ReadShort() / 10f;
        e.Face = p.ReadByte();
        e.Hair = p.ReadInt();
        byte pose = p.ReadByte();
        e.Sitting = pose == UserPose.Sitting;
        e.Dead = pose == UserPose.Dead;
        e.Gathering = pose is UserPose.Mining or UserPose.Fishing;
        e.GatherFishing = pose == UserPose.Fishing;
        p.ReadByte();
        p.ReadInt();
        p.ReadByte();
        e.IsGm = p.ReadByte() == 0;
        p.ReadByte();
        e.Invisible = p.ReadByte() != 0;
        p.ReadByte();
        p.ReadByte();
        p.ReadByte();
        e.Dir = Coord.HeadingFromWire(p.ReadShort());
        p.ReadByte();
        p.ReadByte();
        p.ReadShort();
        p.ReadByte(); p.ReadByte();
        e.Gear = new int[InventoryConstants.VisualSlotCount];
        for (int i = 0; i < InventoryConstants.VisualSlotCount; i++)
        {
            int item = p.ReadInt();
            p.ReadShort(); p.ReadByte();
            e.Gear[i] = item;
        }
        SkipBytes(p, TrailerBeforeHelmet);
        e.HelmetHidden = p.RemainingBytes > 0 && p.ReadByte() != 0;
        SkipBytes(p, TrailerAfterHelmet);
        e.TitleId = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
        return e;
    }

    private const int TrailerBeforeHelmet = 11;
    private const int TrailerAfterHelmet = 12;

    private static EntitySnapshot ReadNpcInfo(Packet p, int id)
    {
        var e = new EntitySnapshot { Id = id, IsNpc = true };
        e.NpcId = p.ReadShort();
        byte kind = p.ReadByte();
        e.IsMonster = kind == NpcTypes.Kind.Monster;
        e.ModelId = p.ReadShort();
        p.ReadInt();
        e.NpcType = p.ReadByte();
        p.ReadInt();
        e.Size = p.ReadShort();
        int w1 = p.ReadInt();
        int w2 = p.ReadInt();
        e.Nation = p.ReadByte();
        e.Level = p.ReadByte();
        e.X = p.ReadShort() / 10f;
        e.Z = p.ReadShort() / 10f;
        e.Y = p.ReadShort() / 10f;
        p.ReadInt();
        e.ObjectType = p.ReadByte();
        p.ReadShort(); p.ReadShort();
        e.Dir = p.ReadByte();
        p.ReadByte();
        if (w1 > 0 || w2 > 0)
            e.Gear = new[] { 0, 0, 0, 0, 0, 0, w1, w2 };
        e.Name = GameData.I != null
            ? GameData.I.NpcName(e.NpcId, e.IsMonster)
            : (e.IsMonster ? "Mob #" : "NPC #") + e.NpcId;
        return e;
    }
}
