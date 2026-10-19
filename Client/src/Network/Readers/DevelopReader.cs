namespace LibreKO.Network.Readers;

internal sealed class DevelopReader : IPacketReader
{
    private readonly DevelopSession develop;
    private readonly ProgressSession progress;
    private readonly SelfSession self;

    public DevelopReader(DevelopSession develop, ProgressSession progress, SelfSession self)
    {
        this.develop = develop;
        this.progress = progress;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_SKILLPT_CHANGE, HandleSkillPointChange);
        dispatcher.On(GameOpcodes.GS_CLASS_CHANGE, HandleClassChange);
    }

    private void HandleSkillPointChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int type = p.ReadByte();
        int value = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        develop.RaiseSkillPointChange(type, value);
    }

    private void HandleClassChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ClassChangeSubOpcode)p.ReadByte();
        switch (sub)
        {
            case ClassChangeSubOpcode.Eligibility: develop.RaiseClassChangeNpc(); break;
            case ClassChangeSubOpcode.JobChangeState: develop.RaiseClassEligibility(p.RemainingBytes >= 1 ? (JobChangeEligibility)p.ReadByte() : default); break;
            case ClassChangeSubOpcode.StatReset: ParseStatReset(p); break;
            case ClassChangeSubOpcode.SkillReset: ParseSkillReset(p); break;
            case ClassChangeSubOpcode.StatResetCost: develop.RaiseResetCost(p.RemainingBytes >= 4 ? p.ReadInt() : 0); break;
            // Two shapes share sub 6: the job-change panel answers [byte result], the quest promotion
            // (ScriptCharacterService.ApplyPromotion) sends [short newClass][int charId].
            case ClassChangeSubOpcode.JobChangeResult:
                if (p.RemainingBytes >= 6)
                {
                    int newClass = p.ReadShort();
                    int charId = p.ReadInt();
                    if (charId == self.MyCharId) self.ApplyClassChange(newClass);
                    develop.RaiseClassPromoted(charId, newClass);
                }
                else develop.RaiseJobChangeResult(p.RemainingBytes >= 1 ? (JobChangeOutcome)p.ReadByte() : default);
                break;
            case ClassChangeSubOpcode.RebirthStatChange:
                if (!develop.RebirthPending) break;
                develop.ApplyRebirthStatChange(sub, RebirthWire.ReadOrBusy(p));
                break;
            case ClassChangeSubOpcode.RebirthStatReset: develop.RaiseRebStatChange(sub, p.RemainingBytes >= 1 ? p.ReadByte() : 0); break;
        }
    }

    private void ParseSkillReset(Packet p)
    {
        var result = p.RemainingBytes >= 1 ? (ResetResult)p.ReadByte() : ResetResult.Refused;
        if (result == ResetResult.Success)
        {
            int money = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
            int pool = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            if (money >= 0) progress.ApplyGold(money);
            develop.RaiseSkillReset(true, money, pool);
        }
        else
        {
            int cost = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            develop.RaiseResetRefused(result, cost);
            develop.RaiseSkillReset(false, cost, 0);
        }
    }

    private void ParseStatReset(Packet p)
    {
        var result = p.RemainingBytes >= 1 ? (ResetResult)p.ReadByte() : ResetResult.Refused;
        if (result == ResetResult.Success && p.RemainingBytes >= 4)
        {
            int money = p.ReadInt();
            var stats = new int[5];
            for (int i = 0; i < 5; i++) stats[i] = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            int maxHp = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            int maxMp = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            int ap = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            if (p.RemainingBytes >= 4) p.ReadInt();
            int statPoints = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            progress.ApplyGold(money);
            develop.RaiseStatReset(new StatResetReply(Succeeded: true, Money: money, Stats: stats, MaxHp: maxHp, MaxMp: maxMp, Ap: ap, StatPoints: statPoints));
        }
        else
        {
            int cost = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            develop.RaiseResetRefused(result, cost);
            develop.RaiseStatReset(new StatResetReply(Succeeded: false, Money: cost, Stats: System.Array.Empty<int>(), MaxHp: 0, MaxMp: 0, Ap: 0, StatPoints: 0));
        }
    }
}
