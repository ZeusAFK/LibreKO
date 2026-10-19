namespace LibreKO.Network.Requests;

public interface IDevelopRequests
{
    bool RebirthStatChange(byte[] picks);
    void RebirthStatReset(byte[] picks);
    void SkillPointChange(int treeType);
    void StatReset();
    void SkillReset();
    void ResetCostQuery(ResetKind kind);
    void ClassEligibilityQuery();
}

public sealed class DevelopRequests : IDevelopRequests
{
    private readonly IPacketSink sink;
    private readonly DevelopSession develop;

    public DevelopRequests(IPacketSink sink, DevelopSession develop)
    {
        this.sink = sink;
        this.develop = develop;
    }

    public void SkillPointChange(int treeType) => SendClassByte(GameOpcodes.GS_SKILLPT_CHANGE, (byte)treeType);

    public void StatReset() => SendClassByte(GameOpcodes.GS_CLASS_CHANGE, (byte)ClassChangeSubOpcode.StatReset);

    public void SkillReset() => SendClassByte(GameOpcodes.GS_CLASS_CHANGE, (byte)ClassChangeSubOpcode.SkillReset);

    public void ResetCostQuery(ResetKind kind)
    {
        var p = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        p.WriteByte((byte)ClassChangeSubOpcode.StatResetCost);
        p.WriteByte((byte)kind);
        sink.Send(p);
    }

    public void ClassEligibilityQuery() => SendClassByte(GameOpcodes.GS_CLASS_CHANGE, (byte)ClassChangeSubOpcode.Eligibility);

    private void SendClassByte(GameOpcodes opcode, byte sub)
    {
        var p = new Packet(opcode);
        p.WriteByte(sub);
        sink.Send(p);
    }

    private const int RebirthStatBytes = 5;

    public bool RebirthStatChange(byte[] picks)
    {
        if (develop.RebirthPending || !RebirthPick.IsAllocation(picks)) return false;
        develop.RebirthPending = true;
        SendRebirthPicks(ClassChangeSubOpcode.RebirthStatChange, picks);
        return true;
    }

    public void RebirthStatReset(byte[] picks) => SendRebirthPicks(ClassChangeSubOpcode.RebirthStatReset, picks);

    private void SendRebirthPicks(ClassChangeSubOpcode sub, byte[] picks)
    {
        var p = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        p.WriteByte((byte)sub);
        for (int i = 0; i < RebirthStatBytes; i++) p.WriteByte(i < picks.Length ? picks[i] : (byte)0);
        sink.Send(p);
    }
}
