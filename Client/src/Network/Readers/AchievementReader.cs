namespace LibreKO.Network.Readers;

internal sealed class AchievementReader : IPacketReader
{
    private readonly GameSession session;
    private readonly AchievementSession achievement;

    private const int RowBytes = 7;
    private const int SummaryBytes = 5 * 4 + AchievementWire.RecentSlots * 2 + AchievementWire.TabCount * 2;

    public AchievementReader(GameSession session, AchievementSession achievement)
    {
        this.session = session;
        this.achievement = achievement;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ACHIEVEMENT, HandleAchievement);
    }

    private void HandleAchievement(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (AchievementSubOpcode)p.ReadByte();

        if (sub == AchievementSubOpcode.List)
        {
            var list = new List<AchievementEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= RowBytes; i++)
            {
                list.Add(new AchievementEntry
                {
                    Id = p.ReadUShort(),
                    State = (AchievementState)p.ReadByte(),
                    Progress = p.ReadUShort(),
                    Target = p.ReadUShort(),
                });
            }
            achievement.ApplyList(list);
        }
        else if (sub == AchievementSubOpcode.TitleChanged)
        {
            if (p.RemainingBytes < 6) return;
            session.TitleChanged(p.ReadInt(), p.ReadUShort());
        }
        else if (sub == AchievementSubOpcode.Summary)
        {
            if (p.RemainingBytes < SummaryBytes) return;
            var summary = new AchievementSummary
            {
                PlayMinutes = p.ReadInt(),
                MonstersDefeated = p.ReadInt(),
                PlayersDefeated = p.ReadInt(),
                Deaths = p.ReadInt(),
                Points = p.ReadInt(),
                RecentlyAchieved = new int[AchievementWire.RecentSlots],
                AchievedPerTab = new int[AchievementWire.TabCount],
            };
            for (int i = 0; i < AchievementWire.RecentSlots; i++) summary.RecentlyAchieved[i] = p.ReadUShort();
            for (int i = 0; i < AchievementWire.TabCount; i++) summary.AchievedPerTab[i] = p.ReadUShort();
            achievement.ApplySummary(summary);
        }
        else if (AchievementTitleAnswer.Handles(sub))
        {
            if (AchievementTitleAnswer.Read(sub, p) is not { } answer) return;
            achievement.ApplyTitleAnswer(answer);
        }
        else if (sub == AchievementSubOpcode.ClaimResult)
        {
            int id = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            var result = p.RemainingBytes >= 2 ? (AchievementClaimResult)p.ReadShort() : AchievementClaimResult.NotAvailable;
            achievement.RaiseAchievementClaim(id, result);
        }
    }
}
