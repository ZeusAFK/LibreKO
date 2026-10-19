namespace LibreKO.Network.Readers;

internal sealed class FishingHallReader : IPacketReader
{
    private readonly FishingHallSession fishingHall;

    public FishingHallReader(FishingHallSession fishingHall)
    {
        this.fishingHall = fishingHall;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_FISHING_HALL, HandleFishingHall);
    }

    private void HandleFishingHall(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (FishingHallSubOpcode)p.ReadByte();
        if (sub == FishingHallSubOpcode.List)
        {
            var list = new List<FishingHallEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
            {
                int rank = p.ReadUShort();
                string name = p.ReadSByteString();
                int score = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                list.Add(new FishingHallEntry { Rank = rank, Name = name, Score = score });
            }
            fishingHall.RaiseFishingHallList(list);
        }
    }
}
