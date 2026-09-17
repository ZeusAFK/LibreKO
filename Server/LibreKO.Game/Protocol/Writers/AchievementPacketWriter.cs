using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol.Writers;

public sealed class AchievementPacketWriter
{

    public const sbyte ClaimIssued = 1;
    public const sbyte ClaimNotAvailable = 0;
    public const sbyte ClaimInventoryFull = -1;
    public const sbyte ClaimItemMissing = -2;

    public const ushort ScaledTarget = ushort.MaxValue;

    public readonly record struct Row(int AchievementId, AchievementProgressState State, int Progress, int Target);

    public static Packet List(IReadOnlyCollection<Row> rows)
    {
        var packet = Sub(AchievementSubOpcode.List);
        packet.WriteUShort((ushort)rows.Count);

        foreach (var row in rows)
        {
            var (progress, target) = Fit(row.Progress, row.Target);
            packet.WriteUShort((ushort)row.AchievementId);
            packet.WriteByte((byte)row.State);
            packet.WriteUShort(progress);
            packet.WriteUShort(target);
        }

        return packet;
    }

    public const int PushedRowsMax = 5;
    public const int RecentlyAchievedSlots = 3;
    public const int TabCount = 5;

    public readonly record struct Summary(
        int PlayMinutes,
        int MonstersDefeated,
        int PlayersDefeated,
        int Deaths,
        int Points,
        IReadOnlyList<int> RecentlyAchieved,
        IReadOnlyList<int> AchievedPerTab);

    public static Packet ProfileSummary(Summary summary)
    {
        var packet = Sub(AchievementSubOpcode.Summary);
        packet.WriteInt(summary.PlayMinutes);
        packet.WriteInt(summary.MonstersDefeated);
        packet.WriteInt(summary.PlayersDefeated);
        packet.WriteInt(summary.Deaths);
        packet.WriteInt(summary.Points);

        for (var index = 0; index < RecentlyAchievedSlots; index++)
            packet.WriteUShort(index < summary.RecentlyAchieved.Count
                ? (ushort)summary.RecentlyAchieved[index]
                : (ushort)0);

        for (var index = 0; index < TabCount; index++)
            packet.WriteUShort(index < summary.AchievedPerTab.Count
                ? (ushort)summary.AchievedPerTab[index]
                : (ushort)0);

        return packet;
    }

    public static Packet TitleChanged(int characterId, int titleId)
    {
        var packet = Sub(AchievementSubOpcode.TitleChanged);
        packet.WriteInt(characterId);
        packet.WriteUShort((ushort)titleId);
        return packet;
    }

    public static Packet ClaimResult(int achievementId, sbyte result)
    {
        var packet = Sub(AchievementSubOpcode.ClaimResult);
        packet.WriteUShort((ushort)achievementId);
        packet.WriteShort(result);
        return packet;
    }

    private static (ushort Progress, ushort Target) Fit(int progress, int target)
    {
        if (target <= ScaledTarget)
            return (Clamp(progress), Clamp(target));

        return (Clamp((int)((long)progress * ScaledTarget / target)), ScaledTarget);
    }

    private static ushort Clamp(int value) =>
        (ushort)Math.Clamp(value, 0, ScaledTarget);

    private static Packet Sub(AchievementSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
