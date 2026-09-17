using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public const int AchievementSubClaimResult = 2;
    public const int AchievementSubList = 3;
    public const int AchievementSubSummary = 4;
    public const int AchievementSubClaimReward = 6;
    public const int AchievementSubSelectDisplayTitle = 16;
    public const int AchievementSubTitleChanged = 32;

    public const int AchievementStateInProgress = 0;
    public const int AchievementStateAchieved = 4;
    public const int AchievementStateClaimed = 5;

    public const int AchievementClaimIssued = 1;
    public const int AchievementClaimNotAvailable = 0;
    public const int AchievementClaimInventoryFull = -1;
    public const int AchievementClaimItemMissing = -2;

    private const int AchievementRowBytes = 7;
    public const int AchievementRecentSlots = 3;
    public const int AchievementTabCount = 5;
    private const int AchievementSummaryBytes = 5 * 4 + AchievementRecentSlots * 2 + AchievementTabCount * 2;
    public const int AchievementScaledTarget = ushort.MaxValue;

    public event Action<List<AchievementEntry>>? AchievementListEvent;
    public event Action<AchievementSummary>? AchievementSummaryEvent;
    public event Action<int, int>? AchievementClaimEvent;
    public event Action<int, int>? TitleChangedEvent;

    public int DisplayTitleId { get; private set; }

    private void HandleAchievement(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int sub = p.ReadByte();

        if (sub == AchievementSubList)
        {
            var list = new List<AchievementEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= AchievementRowBytes; i++)
            {
                list.Add(new AchievementEntry
                {
                    Id = p.ReadUShort(),
                    State = p.ReadByte(),
                    Progress = p.ReadUShort(),
                    Target = p.ReadUShort(),
                });
            }
            AchievementListEvent?.Invoke(list);
        }
        else if (sub == AchievementSubTitleChanged)
        {
            if (p.RemainingBytes < 6) return;
            int charId = p.ReadInt();
            int titleId = p.ReadUShort();
            if (charId == MyCharId) DisplayTitleId = titleId;
            TitleChangedEvent?.Invoke(charId, titleId);
        }
        else if (sub == AchievementSubSummary)
        {
            if (p.RemainingBytes < AchievementSummaryBytes) return;
            var summary = new AchievementSummary
            {
                PlayMinutes = p.ReadInt(),
                MonstersDefeated = p.ReadInt(),
                PlayersDefeated = p.ReadInt(),
                Deaths = p.ReadInt(),
                Points = p.ReadInt(),
                RecentlyAchieved = new int[AchievementRecentSlots],
                AchievedPerTab = new int[AchievementTabCount],
            };
            for (int i = 0; i < AchievementRecentSlots; i++) summary.RecentlyAchieved[i] = p.ReadUShort();
            for (int i = 0; i < AchievementTabCount; i++) summary.AchievedPerTab[i] = p.ReadUShort();
            AchievementSummaryEvent?.Invoke(summary);
        }
        else if (sub == AchievementSubClaimResult)
        {
            int id = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            int result = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            AchievementClaimEvent?.Invoke(id, result);
        }
    }

    public void SendAchievementList()
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteByte(AchievementSubList);
        _conn.Send(p);
    }

    public void SendAchievementSummary()
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteByte(AchievementSubSummary);
        _conn.Send(p);
    }

    public void SendTitleSelect(int titleId)
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteByte(AchievementSubSelectDisplayTitle);
        p.WriteUShort((ushort)titleId);
        p.WriteUShort(0);
        _conn.Send(p);
    }

    public void SendAchievementClaim(int achievementId)
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteByte(AchievementSubClaimReward);
        p.WriteShort((short)achievementId);
        _conn.Send(p);
    }
}

public struct AchievementSummary
{
    public int PlayMinutes;
    public int MonstersDefeated;
    public int PlayersDefeated;
    public int Deaths;
    public int Points;
    public int[] RecentlyAchieved;
    public int[] AchievedPerTab;
}

public struct AchievementEntry
{
    public int Id;
    public int State;
    public int Progress;
    public int Target;

    public bool Claimable => State == Net.AchievementStateAchieved;
    public bool Claimed => State == Net.AchievementStateClaimed;
    public float Fraction => Target > 0 ? Math.Clamp((float)Progress / Target, 0f, 1f) : 0f;

    public string ProgressText => Target == Net.AchievementScaledTarget
        ? $"{Fraction * 100f:0.#}%"
        : $"{Progress} / {Target}";
}
