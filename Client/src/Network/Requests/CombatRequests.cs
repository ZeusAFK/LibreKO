namespace LibreKO.Network.Requests;

public interface ICombatRequests
{
    void Attack(int targetId, short swingDelay, int attackType = (int)AttackType.Melee, bool chained = false);
    void Magic(int subOpcode, int skillId, int targetId, short[]? data = null);
    void CastFailed(int skillId);
    void TargetHpRequest(int targetId);
    void Regene();
    void ResurrectAccept(int casterId);
    void ResurrectChoice(ResurrectionChoice choice, int casterId);
    void SkillDataSave(int[] slotIds);
    void SkillDataLoad();
}

public sealed class CombatRequests : ICombatRequests
{
    private readonly IPacketSink sink;
    private readonly SelfSession self;
    private readonly GenieSession genie;
    private readonly CombatSession combat;

    public CombatRequests(IPacketSink sink, SelfSession self, GenieSession genie, CombatSession combat)
    {
        this.sink = sink;
        this.self = self;
        this.genie = genie;
        this.combat = combat;
    }

    public void Attack(int targetId, short swingDelay, int attackType = (int)AttackType.Melee, bool chained = false)
    {
        var p = GenieRequests.ActionPacket(GameOpcodes.GS_ATTACK, GenieSystemAction.MainAttack, genie.GenieRunning);
        p.WriteByte((byte)attackType);
        p.WriteByte(0);
        p.WriteInt(targetId);
        p.WriteShort(swingDelay);
        p.WriteShort(0);
        p.WriteByte(chained ? (byte)1 : (byte)0);
        p.WriteByte(0);
        sink.Send(p);
    }

    public void Magic(int subOpcode, int skillId, int targetId, short[]? data = null)
    {
        var p = GenieRequests.ActionPacket(GameOpcodes.GS_MAGIC_PROCESS, GenieSystemAction.Magic, genie.GenieRunning);
        p.WriteByte((byte)subOpcode);
        p.WriteInt(skillId);
        p.WriteInt(self.MyCharId);
        p.WriteInt(targetId);
        for (int i = 0; i < MagicData.Length; i++)
            p.WriteInt(data != null && i < data.Length ? data[i] : 0);
        sink.Send(p);
    }

    public void CastFailed(int skillId)
    {
        var data = new short[MagicFail.ResultSlot + 1];
        data[MagicFail.ResultSlot] = MagicFail.Casting;
        Magic(MagicSub.Fail, skillId, self.MyCharId, data);
    }

    public void TargetHpRequest(int targetId)
    {
        var p = new Packet(GameOpcodes.GS_TARGET_HP);
        p.WriteInt(targetId);
        p.WriteByte((byte)TargetHpEcho.Poll);
        sink.Send(p);
    }

    public void Regene()
    {
        var p = new Packet(GameOpcodes.GS_REGENE);
        p.WriteByte((byte)RegeneSubOpcode.Town);
        sink.Send(p);
    }

    public void ResurrectAccept(int casterId)
    {
        var p = new Packet(GameOpcodes.GS_REGENE);
        p.WriteByte((byte)RegeneSubOpcode.ResurrectAccept);
        p.WriteInt(casterId);
        sink.Send(p);
    }

    public void ResurrectChoice(ResurrectionChoice choice, int casterId)
    {
        var p = new Packet(GameOpcodes.GS_REGENE);
        p.WriteByte((byte)RegeneSubOpcode.ResurrectChoice);
        p.WriteByte((byte)choice);
        p.WriteInt(casterId);
        sink.Send(p);
    }

    public void SkillDataSave(int[] slotIds)
    {
        int count = System.Math.Clamp(slotIds.Length, 1, CombatWire.SkillBarMaxSlots);
        var p = new Packet(GameOpcodes.GS_SKILLDATA);
        p.WriteByte((byte)SkillBarSubOpcode.Save);
        p.WriteShort((short)count);
        var bar = new int[count];
        for (int i = 0; i < count; i++)
        {
            bar[i] = i < slotIds.Length ? slotIds[i] : 0;
            p.WriteInt(bar[i]);
        }
        sink.Send(p);
        combat.KeepSkillBar(bar);
    }

    public void SkillDataLoad()
    {
        var p = new Packet(GameOpcodes.GS_SKILLDATA);
        p.WriteByte((byte)SkillBarSubOpcode.Load);
        sink.Send(p);
    }
}
