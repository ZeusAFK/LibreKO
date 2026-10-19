namespace LibreKO.Network.Requests;

public interface IAdminRequests
{
    void CommandsRequest();
    void StateRequest();
    void Cash(int amount);
    void Coins(int amount);
    void Stats(int str, int sta, int dex, int intel, int magic, int statPoints, int loyalty);
    void GiveItem(int itemId, int count);
    void SetClass(int classId);
    void Zone(int zoneId);
    void SetLevel(int level, bool reset);
    void SetSkill(int pool, IReadOnlyList<int> trees, bool reset);
    void SetLook(int nation, int race);
    void SpawnRowRequest(int row);
    void SpawnEdit(bool persist, int row, int x, int z, int direction, int count, int respawnSeconds, int range);
    void Find(int kind, string query);
    void Go(int zone, int x, int z);
    void CollectionRacesRequest();
    void CollectionRaceStart(int raceId);
    void CollectionRaceClose(int raceId);
}

public sealed class AdminRequests : IAdminRequests
{
    private readonly IPacketSink sink;

    public AdminRequests(IPacketSink sink) => this.sink = sink;

    public void CommandsRequest()
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.Commands);
        sink.Send(p);
    }

    public void StateRequest() => SendAdminByte(AdminRequestSubOpcode.State);

    public void Cash(int amount)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.Cash);
        p.WriteInt(amount);
        sink.Send(p);
    }

    public void Coins(int amount)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.Coins);
        p.WriteInt(amount);
        sink.Send(p);
    }

    public void Stats(int str, int sta, int dex, int intel, int magic, int statPoints, int loyalty)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.Stats);
        p.WriteByte(ClampStatByte(str));
        p.WriteByte(ClampStatByte(sta));
        p.WriteByte(ClampStatByte(dex));
        p.WriteByte(ClampStatByte(intel));
        p.WriteByte(ClampStatByte(magic));
        p.WriteShort(WireNumbers.ClampToShort(Math.Max(0, statPoints)));
        p.WriteInt(Math.Max(0, loyalty));
        sink.Send(p);
    }

    public void GiveItem(int itemId, int count)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.GiveItem);
        p.WriteInt(itemId);
        p.WriteShort(WireNumbers.ClampToShort(Math.Clamp(count, 1, 9999)));
        sink.Send(p);
    }

    public void SetClass(int classId)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.SetClass);
        p.WriteShort(WireNumbers.ClampToShort(classId));
        sink.Send(p);
    }

    public void Zone(int zoneId)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.Zone);
        p.WriteShort(WireNumbers.ClampToShort(zoneId));
        sink.Send(p);
    }

    public void SetLevel(int level, bool reset)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.SetLevel);
        p.WriteByte((byte)Math.Clamp(level, CharacterSheet.MinLevel, CharacterSheet.MaxLevel));
        p.WriteByte((byte)(reset ? AdminProgressMode.Reset : AdminProgressMode.Keep));
        sink.Send(p);
    }

    public void SetSkill(int pool, IReadOnlyList<int> trees, bool reset)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.SetSkill);
        p.WriteByte(ClampByte(pool));
        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
        {
            int index = tree - MasteryPoints.FirstTree;
            p.WriteByte(ClampByte(index < trees.Count ? trees[index] : 0));
        }
        p.WriteByte((byte)(reset ? AdminProgressMode.Reset : AdminProgressMode.Keep));
        sink.Send(p);
    }

    public void SetLook(int nation, int race)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.SetLook);
        p.WriteByte(ClampByte(nation));
        p.WriteByte(ClampByte(race));
        sink.Send(p);
    }

    public void SpawnRowRequest(int row) => SendAdminInt(AdminRequestSubOpcode.SpawnRow, row);

    public void SpawnEdit(bool persist, int row, int x, int z, int direction, int count, int respawnSeconds, int range)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)(persist ? AdminRequestSubOpcode.SpawnPersist : AdminRequestSubOpcode.SpawnSet));
        p.WriteInt(row);
        p.WriteInt(x);
        p.WriteInt(z);
        p.WriteInt(direction);
        p.WriteByte((byte)Math.Clamp(count, 0, byte.MaxValue));
        p.WriteShort((short)Math.Clamp(respawnSeconds, 0, short.MaxValue));
        p.WriteShort((short)Math.Clamp(range, 0, short.MaxValue));
        sink.Send(p);
    }

    public void Find(int kind, string query)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.Find);
        p.WriteByte((byte)kind);
        p.WriteUtf8String(query);
        sink.Send(p);
    }

    public void Go(int zone, int x, int z)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminRequestSubOpcode.Go);
        p.WriteByte((byte)zone);
        p.WriteUShort((ushort)Math.Clamp(x, 0, ushort.MaxValue));
        p.WriteUShort((ushort)Math.Clamp(z, 0, ushort.MaxValue));
        sink.Send(p);
    }

    public void CollectionRacesRequest() => SendAdminByte(AdminRequestSubOpcode.CollectionRaces);

    public void CollectionRaceStart(int raceId) => SendAdminInt(AdminRequestSubOpcode.CollectionRaceStart, raceId);

    public void CollectionRaceClose(int raceId) => SendAdminInt(AdminRequestSubOpcode.CollectionRaceClose, raceId);

    private void SendAdminInt(AdminRequestSubOpcode sub, int value)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)sub);
        p.WriteInt(value);
        sink.Send(p);
    }

    private void SendAdminByte(AdminRequestSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)sub);
        sink.Send(p);
    }

    private static byte ClampStatByte(int value) => (byte)Math.Clamp(value, 1, 255);

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
}
