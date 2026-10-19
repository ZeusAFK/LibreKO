namespace LibreKO.Network.Readers;

internal sealed class CombatReader : IPacketReader
{
    private readonly CombatSession combat;
    private readonly DisguiseSession disguise;
    private readonly EntitiesSession entities;
    private readonly SelfSession self;

    private const byte AttackChainedFlag = 1;
    private const byte StaminaBar = 1;
    private const byte StaminaBarShown = 1;

    public CombatReader(CombatSession combat, DisguiseSession disguise, EntitiesSession entities, SelfSession self)
    {
        this.combat = combat;
        this.disguise = disguise;
        this.entities = entities;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_TARGET_HP, HandleTargetHp);
        dispatcher.On(GameOpcodes.GS_ATTACK, HandleAttack);
        dispatcher.On(GameOpcodes.GS_MAGIC_PROCESS, HandleMagicProcess);
        dispatcher.On(GameOpcodes.GS_DEAD, HandleDead);
        dispatcher.On(GameOpcodes.GS_HP_CHANGE, HandleHpChange);
        dispatcher.On(GameOpcodes.GS_MSP_CHANGE, HandleMspChange);
        dispatcher.On(GameOpcodes.GS_SP_CHANGE, HandleStaminaChange);
        dispatcher.On(GameOpcodes.GS_REGENE, HandleRegene);
        dispatcher.On(GameOpcodes.GS_SKILLDATA, HandleSkillData);
    }

    private void HandleTargetHp(Packet p)
    {
        int id = p.ReadInt();
        var echo = (TargetHpEcho)p.ReadByte();
        int maxHp = p.ReadInt();
        int hp = p.ReadInt();
        int damage = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        if (p.RemainingBytes >= 4) p.ReadInt();
        var drops = TargetQuestDrop.ReadList(p);
        if (echo == TargetHpEcho.Poll)
        {
            entities.RaiseEntityHpSync(id, hp, maxHp);
            combat.RaiseTargetQuestDrops(id, drops);
        }
        else
            entities.RaiseEntityHp(id, hp, maxHp, damage);
    }

    private void HandleAttack(Packet p)
    {
        byte attackType = p.ReadByte();
        byte result = p.ReadByte();
        int attackerId = p.ReadInt();
        int targetId = p.ReadInt();
        bool chained = p.RemainingBytes >= 1 && p.ReadByte() == AttackChainedFlag;
        combat.RaiseAttack(new AttackReply(AttackType: attackType, Result: result, AttackerId: attackerId, TargetId: targetId, Chained: chained));
    }

    private void HandleMagicProcess(Packet p)
    {
        byte sub = p.ReadByte();
        if (HandleTransformationMagic(sub, p)) return;
        if (sub == MagicSub.DurationExpired)
        {
            if (p.RemainingBytes >= 1) combat.RaiseBuffExpired(p.ReadByte());
            return;
        }
        if (sub == MagicSub.ExtendDuration)
        {
            if (p.RemainingBytes >= 4) combat.RaiseBuffExtended(p.ReadInt());
            return;
        }
        if (p.RemainingBytes < 12)
        {
            combat.RaiseMagic(new MagicReply(Sub: sub, SkillId: 0, CasterId: 0, TargetId: 0, Data: new short[MagicData.Length]));
            return;
        }

        int skillId = p.ReadInt();
        int casterId = p.ReadInt();
        int targetId = p.ReadInt();
        var data = new short[MagicData.Length];
        for (int i = 0; i < data.Length && p.RemainingBytes >= 4; i++)
            data[i] = (short)p.ReadInt();
        combat.RaiseMagic(new MagicReply(Sub: sub, SkillId: skillId, CasterId: casterId, TargetId: targetId, Data: data));
    }

    private void HandleDead(Packet p)
    {
        int victim = p.ReadInt();
        int killer = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
        if (entities.TryGet(victim, out var known)) known.Dead = true;
        combat.RaiseDead(victim, killer);
    }

    private void HandleHpChange(Packet p)
    {
        int maxHp = p.ReadShort();
        int hp = p.ReadShort();
        int attackerId = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
        self.ApplyHp(hp, maxHp, attackerId);
    }

    private void HandleMspChange(Packet p)
    {
        int maxMp = p.ReadShort();
        int mp = p.ReadShort();
        self.ApplyMp(mp, maxMp);
    }

    private void HandleStaminaChange(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        byte kind = p.ReadByte();
        byte shown = p.ReadByte();
        if (kind != StaminaBar || shown != StaminaBarShown) return;
        int maxSp = p.ReadByte();
        int sp = p.ReadByte();
        self.ApplyStamina(sp, maxSp);
    }

    private void HandleRegene(Packet p)
    {
        if (RegeneReply.Read(p) is not { } reply) return;
        switch (reply.Type)
        {
            case RespawnType.ResurrectOffer:
                combat.RaiseResurrectOffer(reply.CasterId, reply.SkillId);
                break;
            case RespawnType.Refused:
                combat.RaiseRegeneRefused(reply.Code, reply.ItemId, reply.Amount);
                break;
            default:
                self.RaiseRegene(reply.X, reply.Z);
                break;
        }
    }

    private void HandleSkillData(Packet p)
    {
        if (p.RemainingBytes < 2) { combat.RaiseSkillData(System.Array.Empty<int>()); return; }
        int count = p.ReadShort();
        if (count <= 0)
        {
            combat.ClearSkillBar();
            return;
        }
        var ids = new int[count];
        for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            ids[i] = p.ReadInt();
        combat.ApplySkillBar(ids);
    }

    private bool HandleTransformationMagic(int sub, Packet p)
    {
        if (sub == MagicSub.TransformationList)
        {
            if (p.RemainingBytes >= 4) disguise.RaiseTransformationList(p.ReadInt());
            return true;
        }
        if (sub == MagicSub.TransformationRefused)
        {
            if (p.RemainingBytes >= 1) disguise.RaiseTransformationRefused(p.ReadByte());
            return true;
        }
        return false;
    }
}
