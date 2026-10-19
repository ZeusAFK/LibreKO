namespace LibreKO.Network;

public enum ClanAdvanceType : byte
{
    Status = 1,
    Rows = 2,
}

public enum WishBoardType : byte
{
    Page = 0,
    Requests = 1,
    Intro = 2,
    Request = 3,
    Online = 4,
    Accept = 5,
    Promote = 6,
}

public enum WishPageKind : byte
{
    Rows = 0,
    Status = 1,
    TooSoon = 4,
}

public enum ClanAdvanceRequest : byte
{
    Page = 0,
    Open = 1,
}

public static class ClanBoardsWire
{
    public static ClanBoardStatus? ReadBoardStatus(Packet p)
    {
        if (p.RemainingBytes < 3) return null;
        var state = (ClanBoardState)p.ReadByte();
        int count = p.ReadShort();
        var applicants = new List<ClanApplicant>();
        if (state == ClanBoardState.Staff)
        {
            for (int i = 0; i < count && p.RemainingBytes >= 6; i++)
                applicants.Add(new ClanApplicant(p.ReadByte(), p.ReadByte(), p.ReadString(), p.ReadString()));
            return new ClanBoardStatus(state, applicants, 0, 0, "");
        }
        if (count == 0 || p.RemainingBytes < 6) return new ClanBoardStatus(state, applicants, 0, 0, "");
        int clanId = p.ReadShort();
        int mark = p.ReadShort();
        return new ClanBoardStatus(state, applicants, clanId, mark, p.ReadString());
    }

    public static List<ClanAdvert> ReadAdverts(Packet p)
    {
        var rows = new List<ClanAdvert>();
        if (p.RemainingBytes < 2) return rows;
        int count = p.ReadShort();
        for (int i = 0; i < count && i < ClanBoards.RowsPerPage && p.RemainingBytes >= 12; i++)
            rows.Add(new ClanAdvert(p.ReadShort(), p.ReadString(), p.ReadByte(), p.ReadByte(), p.ReadString(), p.ReadShort(), p.ReadString()));
        return rows;
    }

    public static List<WishPost> ReadWishPosts(Packet p)
    {
        var rows = new List<WishPost>();
        if (p.RemainingBytes < 2) return rows;
        int count = p.ReadShort();
        if (count > ClanBoards.RowsPerPage) return rows;
        for (int i = 0; i < count && p.RemainingBytes >= 14; i++)
            rows.Add(new WishPost(p.ReadString(), p.ReadString(), p.ReadByte(), p.ReadByte(), p.ReadByte(), p.ReadByte() != 0,
                p.ReadInt(), p.ReadString()));
        return rows;
    }

    public static WishStatus ReadWishStatus(Packet p)
    {
        var role = p.RemainingBytes >= 1 ? (WishBoardRole)p.ReadByte() : WishBoardRole.Recruiter;
        if (role == WishBoardRole.Recruiter)
            return new WishStatus(role, p.RemainingBytes >= 2 ? p.ReadString() : "", 0);
        if (role == WishBoardRole.Member) return new WishStatus(role, "", 0);
        return new WishStatus(role, "", p.RemainingBytes >= 1 ? p.ReadByte() : 0);
    }

    public static List<WishRequest> ReadWishRequests(Packet p)
    {
        var rows = new List<WishRequest>();
        if (p.RemainingBytes < 1) return rows;
        int count = p.ReadByte();
        for (int i = 0; i < count && i < ClanBoards.MaxRequests && p.RemainingBytes >= 12; i++)
            rows.Add(new WishRequest(p.ReadShort(), p.ReadString(), p.ReadString(), p.ReadByte(), p.ReadByte(), p.ReadShort(), p.ReadString()));
        return rows;
    }
}

public enum ClanBoardAction : byte
{
    Register = 0,
    Delete = 1,
}

public enum ClanBoardState : byte
{
    Staff = 0,
    Member = 1,
    Clanless = 2,
}

public enum WishBoardRole : byte
{
    Recruiter = 0,
    Member = 1,
    Seeker = 2,
}
