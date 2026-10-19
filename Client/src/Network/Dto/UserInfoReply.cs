namespace LibreKO.Network;

public sealed record UserInfoReply(
    string Name,
    int Level,
    int Class,
    int Loyalty,
    int MonthlyLoyalty,
    int ClanId,
    int ClanMarkVersion,
    int ClanFlag,
    int ClanGrade,
    string ClanName,
    string ClanChief,
    int ClanRanking,
    int RebirthLevel,
    int PartyMarker,
    int[] RankPlaces,
    IReadOnlyList<RankMark> Marks)
{
    public bool InParty => PartyMarker != UserInfoCard.NoParty;

    public static (byte Result, UserInfoReply? Info) Read(Packet p)
    {
        if (p.RemainingBytes < 1) return (0, null);
        var result = (PlayerInspectResult)p.ReadByte();
        if (result != PlayerInspectResult.Accepted) return ((byte)result, null);

        string name = p.ReadSByteString();
        int level = p.ReadByte();
        int cls = p.ReadShort();
        int loyalty = p.ReadInt();
        int monthly = p.ReadInt();
        p.ReadByte();
        int clanId = p.ReadShort();
        int markVersion = p.ReadShort();
        int clanFlag = p.ReadByte();
        int clanGrade = p.ReadByte();
        string clanName = p.ReadSByteString();
        string clanChief = p.ReadSByteString();
        int clanRanking = p.ReadByte();
        int rebirth = p.ReadByte();
        int party = p.ReadUShort();
        var places = new int[UserInfoCard.RankPlaces];
        for (int i = 0; i < places.Length && p.RemainingBytes >= 4; i++) places[i] = p.ReadInt();

        var marks = new List<RankMark>();
        if (p.RemainingBytes >= 1)
        {
            int count = p.ReadByte();
            for (int i = 0; i < count && p.RemainingBytes >= 8; i++)
            {
                int type = p.ReadUShort();
                var mark = new RankMark(type, p.ReadShort(), p.ReadShort(), p.ReadShort());
                if (type != UserInfoCard.DroppedMarkType) marks.Add(mark);
            }
        }
        if (p.RemainingBytes >= 1)
        {
            int count = p.ReadByte();
            for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            {
                int id = p.ReadUShort();
                int amount = p.ReadUShort();
                if (amount > 0) marks.Add(new RankMark(id + UserInfoCard.SpecialMarkFirst, 0, 0, amount));
            }
        }
        return ((byte)result, new UserInfoReply(name, level, cls, loyalty, monthly, clanId, markVersion, clanFlag, clanGrade,
            clanName, clanChief, clanRanking, rebirth, party, places, marks));
    }
}
