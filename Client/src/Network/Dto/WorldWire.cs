namespace LibreKO.Network;

public enum MoveEcho : byte
{
    Finish = 0,
    Start = 1,
    Move = 3,
}

public enum NoticeSubOpcode : byte
{
    Screen = 1,
    Login = 2,
    AccountState = 3,
    EventTitles = 4,
}

public enum NoticeAccountState : byte
{
    TradeBlocked = 11,
}

public enum EventTitleSubOpcode : byte
{
    Shown = 1,
    Removed = 2,
    ShownWithEffect = 3,
}

public enum PreGameSubOpcode : byte
{
    StoryShow = 1,
    FlashBonus = 4,
}

public enum FlashBonusAction : byte
{
    Started = 1,
    Finished = 2,
}

public enum InOutType : byte
{
    In = 1,
    Out = 2,
    Respawn = 3,
    Warp = 4,
    Summon = 5,
}

public enum RegionListStage : byte
{
    Clear = 0,
    List = 1,
    End = 2,
}

public static class WorldWire
{
    public static EntitySnapshot ReadUserInfo(Packet p, int id)
    {
        var e = new EntitySnapshot { Id = id, IsNpc = false };
        e.Name = p.ReadSByteString();
        e.Nation = p.ReadByte();
        p.ReadByte(); p.ReadByte(); p.ReadByte();
        short knightsId = p.ReadShort();
        e.KnightsId = knightsId;
        e.ClanFame = p.ReadByte();
        if (knightsId > 0)
        {
            e.AllianceId = p.ReadShort();
            e.ClanName = p.ReadSByteString();
            e.ClanGrade = p.ReadByte();
            e.ClanRanking = p.ReadByte();
            e.ClanMarkVersion = p.ReadShort();
            e.CapeId = p.ReadShort();
            e.CapeR = p.ReadByte(); e.CapeG = p.ReadByte(); e.CapeB = p.ReadByte();
            p.ReadByte();
            e.ClanFlag = p.ReadByte();
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
        e.X = p.ReadShort() / Coord.TenthsPerUnit;
        e.Z = p.ReadShort() / Coord.TenthsPerUnit;
        e.Y = p.ReadShort() / Coord.TenthsPerUnit;
        e.Face = p.ReadByte();
        e.Hair = p.ReadInt();
        byte pose = p.ReadByte();
        e.Sitting = pose == UserPose.Sitting;
        e.Dead = pose == UserPose.Dead;
        e.Gathering = pose is UserPose.Mining or UserPose.Fishing;
        e.GatherFishing = pose == UserPose.Fishing;
        e.Appearance = p.ReadByte();
        e.TransformSkill = p.ReadInt();
        e.NeedParty = p.ReadByte();
        e.IsGm = p.ReadByte() == SelfSession.GameMasterAuthority;
        e.PartyLeader = p.ReadByte() != 0;
        e.Invisibility = p.ReadByte();
        e.Invisible = e.Invisibility != 0;
        e.SoccerTeam = p.ReadByte();
        p.ReadByte();
        p.ReadByte();
        e.Dir = Coord.HeadingFromWire(p.ReadShort());
        p.ReadByte();
        p.ReadByte();
        p.ReadShort();
        e.KnightsRank = p.ReadByte();
        e.PersonalRank = p.ReadByte();
        e.Gear = new int[InventoryConstants.VisualSlotCount];
        for (int i = 0; i < InventoryConstants.VisualSlotCount; i++)
        {
            int item = p.ReadInt();
            p.ReadShort(); p.ReadByte();
            e.Gear[i] = item;
        }
        p.Skip(TrailerBeforeHelmet);
        e.HelmetHidden = p.RemainingBytes > 0 && p.ReadByte() != 0;
        e.CostumeHidden = p.RemainingBytes > 0 && p.ReadByte() != 0;
        e.GenieActive = p.RemainingBytes > 0 && p.ReadByte() != 0;
        p.Skip(TrailerAfterGenie);
        e.TitleId = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
        return e;
    }

    private const int TrailerBeforeHelmet = 11;

    private const int TrailerAfterGenie = 10;
}

public enum ZoneChangeSubOpcode : byte
{
    Loading = 1,
    Ready = 2,
    Teleport = 3,
}

public readonly record struct EntityMove(int Id, float X, float Z, float Y, float Speed, bool Travelling);
