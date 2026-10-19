namespace LibreKO.Network.Readers;

internal sealed class WorldReader : IPacketReader
{
    private readonly GameSession session;
    private readonly EntitiesSession entities;
    private readonly SelfSession self;
    private readonly ZoneSession zone;
    private readonly IZoneRequests zoneRequests;

    private const int TextTradeBlocked = 7696;
    private const int NpcRegionListMax = 999;

    public WorldReader(GameSession session, EntitiesSession entities, SelfSession self, ZoneSession zone, IZoneRequests zoneRequests)
    {
        this.session = session;
        this.entities = entities;
        this.self = self;
        this.zone = zone;
        this.zoneRequests = zoneRequests;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_MOVE, HandleMove);
        dispatcher.On(GameOpcodes.GS_ROTATE, HandleRotate);
        dispatcher.On(GameOpcodes.GS_WARP, HandleWarp);
        dispatcher.On(GameOpcodes.GS_USER_INOUT, HandleUserInOut);
        dispatcher.On(GameOpcodes.GS_TIME, HandleTime);
        dispatcher.On(GameOpcodes.GS_WEATHER, HandleWeather);
        dispatcher.On(GameOpcodes.GS_NOTICE, HandleNotice);
        dispatcher.On(GameOpcodes.GS_REGIONCHANGE, ParseRegionUserList);
        dispatcher.On(GameOpcodes.GS_REQ_USERIN, ParseUserSnapshot);
        dispatcher.On(GameOpcodes.GS_NPC_REGION, ParseNpcRegionList);
        dispatcher.On(GameOpcodes.GS_REQ_NPCIN, ParseNpcSnapshot);
        dispatcher.On(GameOpcodes.GS_NPC_INOUT, HandleNpcInOut);
        dispatcher.On(GameOpcodes.GS_NPC_MOVE, HandleNpcMove);
        dispatcher.On(GameOpcodes.GS_STORY, HandleStory);
    }

    private void HandleMove(Packet p)
    {
        int id = p.ReadInt();
        float x = p.ReadUShort() / Coord.TenthsPerUnit;
        float z = p.ReadUShort() / Coord.TenthsPerUnit;
        float y = p.ReadUShort() / Coord.TenthsPerUnit;
        short speed = p.ReadShort();
        var echo = p.RemainingBytes >= 1 ? (MoveEcho)p.ReadByte() : MoveEcho.Finish;
        if (entities.TryGet(id, out var known)) { known.X = x; known.Z = z; known.Y = y; }
        entities.RaiseEntityMove(new EntityMove(Id: id, X: x, Z: z, Y: y, Speed: speed / Coord.TenthsPerUnit, Travelling: echo != MoveEcho.Finish));
    }

    private void HandleRotate(Packet p)
    {
        int id = p.ReadInt();
        float heading = Coord.HeadingFromWire(p.ReadShort());
        if (entities.TryGet(id, out var known) && !known.IsNpc) known.Dir = heading;
        entities.RaiseEntityRotate(id, heading);
    }

    private void HandleWarp(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        float wx = p.ReadUShort() / Coord.TenthsPerUnit;
        float wz = p.ReadUShort() / Coord.TenthsPerUnit;
        zone.RaiseWarp(wx, wz);
    }

    private void HandleUserInOut(Packet p)
    {
        var type = (InOutType)p.ReadByte();
        p.ReadByte();
        int id = p.ReadInt();
        if (type == InOutType.Out)
            session.EntityLeft(id);
        else
            session.EntityEntered(WorldWire.ReadUserInfo(p, id));
    }

    private void HandleNpcInOut(Packet p)
    {
        var type = (InOutType)p.ReadByte();
        int id = p.ReadInt();
        if (type == InOutType.Out)
            session.EntityLeft(id);
        else
            RaiseNpcSpawn(ReadNpcInfo(p, id));
    }

    private void RaiseNpcSpawn(EntitySnapshot npc)
    {
        if (!npc.Vacant) session.EntityEntered(npc);
    }

    private void HandleNpcMove(Packet p)
    {
        p.ReadByte();
        int id = p.ReadInt();
        float x = p.ReadShort() / Coord.TenthsPerUnit;
        float z = p.ReadShort() / Coord.TenthsPerUnit;
        float y = p.ReadShort() / Coord.TenthsPerUnit;
        ushort rate = p.ReadUShort();
        entities.RaiseEntityMove(new EntityMove(Id: id, X: x, Z: z, Y: y, Speed: rate / Coord.TenthsPerUnit, Travelling: true));
    }

    private void HandleTime(Packet p)
    {
        p.ReadShort(); p.ReadShort(); p.ReadShort();
        int hour = p.ReadShort();
        int minute = p.ReadShort();
        zone.ApplyTime(hour, minute);
    }

    private void HandleWeather(Packet p)
    {
        int wtype = p.ReadByte();
        int amount = p.ReadUShort();
        zone.ApplyWeather(wtype, amount);
    }

    private void HandleStory(Packet p)
    {
        if (p.RemainingBytes < 1 || (PreGameSubOpcode)p.ReadByte() != PreGameSubOpcode.FlashBonus) return;
        if (FlashBonus.Read(p) is not { } flash) return;
        zone.ShowFlashBonus(!flash.Ended && flash.Value != 0 && FlashTitle.TextFor(flash.Kind) is { } text
            ? FlashTitle.Format(ItemData.Text(text.Id, text.Fallback), flash.Value)
            : "");
    }

    private void HandleNotice(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var style = (NoticeSubOpcode)p.ReadByte();
        if (style == NoticeSubOpcode.AccountState)
        {
            if ((NoticeAccountState)p.ReadByte() == NoticeAccountState.TradeBlocked)
                zone.RaiseTradeBlocked(ItemData.Text(TextTradeBlocked, "The character is blocked for trading."));
            return;
        }
        if (style == NoticeSubOpcode.EventTitles)
        {
            if (EventTitleNotice.Read(p) is not { } notice) return;
            if (notice.Removed) zone.EventTitles.Remove(notice.Title);
            else zone.EventTitles.Show(notice.Title, notice.Message);
            zone.RaiseEventTitlesChanged();
            return;
        }
        if (style is not (NoticeSubOpcode.Screen or NoticeSubOpcode.Login)) return;
        int count = p.ReadByte();

        for (int i = 0; i < count && p.RemainingBytes > 0; i++)
        {
            if (style == NoticeSubOpcode.Login)
            {
                string title = p.ReadString();
                string message = p.RemainingBytes >= sizeof(ushort) ? p.ReadString() : "";
                zone.EventTitles.Show(title.Length > 0 ? title : message, message);
                continue;
            }
            string line = p.ReadSByteString();
            if (line.Length > 0) zone.RaiseNotice(line);
        }
        if (style == NoticeSubOpcode.Login) zone.RaiseEventTitlesChanged();
    }

    private void ParseRegionUserList(Packet p)
    {
        var stage = (RegionListStage)p.ReadByte();
        if (stage != RegionListStage.List) return;
        int count = p.ReadUShort();
        var visible = new HashSet<int>();
        for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            visible.Add(p.ReadInt());

        DropUnlistedPlayers(visible);
        if (visible.Count == 0) return;

        zoneRequests.UsersIn(visible);
    }

    // The list is the whole set of visible players, so anything else we hold is a lost-INOUT_OUT ghost.
    private void DropUnlistedPlayers(HashSet<int> visible)
    {
        List<int>? stale = null;
        foreach (var known in entities.All)
        {
            if (known.IsNpc || known.Id == self.MyCharId || visible.Contains(known.Id)) continue;
            (stale ??= new List<int>()).Add(known.Id);
        }

        if (stale == null) return;
        foreach (int id in stale)
            session.EntityLeft(id);
    }

    private void ParseNpcRegionList(Packet p)
    {
        int count = p.ReadShort();
        var listed = new List<int>(Math.Max(count, 0));
        for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            listed.Add(p.ReadInt());

        if (count < NpcRegionListMax) DropUnlistedNpcs(listed);
        if (listed.Count == 0) return;

        zoneRequests.NpcsIn(listed);
    }

    private void DropUnlistedNpcs(List<int> listed)
    {
        var visible = new HashSet<int>(listed);
        List<int>? stale = null;
        foreach (var known in entities.All)
        {
            if (!known.IsNpc || known.Dead || visible.Contains(known.Id)) continue;
            (stale ??= new List<int>()).Add(known.Id);
        }

        if (stale == null) return;
        foreach (int id in stale)
            session.EntityLeft(id);
    }

    private void ParseUserSnapshot(Packet p)
    {
        int count = p.ReadShort();
        for (int i = 0; i < count && p.RemainingBytes > 0; i++)
        {
            p.ReadByte();
            int id = p.ReadInt();
            session.EntityEntered(WorldWire.ReadUserInfo(p, id));
        }
    }

    private void ParseNpcSnapshot(Packet p)
    {
        int count = p.ReadShort();
        for (int i = 0; i < count && p.RemainingBytes > 0; i++)
        {
            int id = p.ReadInt();
            RaiseNpcSpawn(ReadNpcInfo(p, id));
        }
    }

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
        string petName = "";
        if (e.NpcType == NpcTypes.Pet)
        {
            e.PetOwner = p.ReadSByteString();
            petName = p.ReadSByteString();
            p.ReadInt();
        }
        e.Nation = p.ReadByte();
        e.Level = p.ReadByte();
        e.X = p.ReadShort() / Coord.TenthsPerUnit;
        e.Z = p.ReadShort() / Coord.TenthsPerUnit;
        e.Y = p.ReadShort() / Coord.TenthsPerUnit;
        e.GateOpen = p.ReadInt();
        e.ObjectType = p.ReadByte();
        p.ReadShort(); p.ReadShort();
        e.Dir = p.ReadByte();
        p.ReadByte();
        if (w1 > 0 || w2 > 0)
            e.Gear = new[] { 0, 0, 0, 0, 0, 0, w1, w2 };
        e.Name = e.NpcType == NpcTypes.Pet
            ? petName
            : NpcNames.Name(e.NpcId, e.IsMonster);
        if (RankerStatue.Is(e.NpcType))
            ReadStatue(p, e);
        return e;
    }

    private static void ReadStatue(Packet p, EntitySnapshot e)
    {
        string ranker = p.ReadString();
        if (ranker.Length == 0)
        {
            e.Vacant = true;
            return;
        }
        e.Name = ranker;
        e.Race = p.ReadByte();
        e.Class = p.ReadShort();
        e.Face = p.ReadByte();
        e.Hair = p.ReadInt();
        int head = p.ReadInt(), breast = p.ReadInt(), leg = p.ReadInt(), glove = p.ReadInt(), foot = p.ReadInt();
        int right = p.ReadInt(), left = p.ReadInt();
        e.Gear = RankerStatue.Dress(e.Class, head, breast, leg, glove, foot, right, left);
    }
}
