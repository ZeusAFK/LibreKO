using Godot;

namespace LibreKO.Features.Core;

internal sealed class CombatSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly CombatSession combat;
    private readonly ItemsSession items;
    private readonly SelfSession self;
    private readonly VendorSession vendor;
    private readonly IPlayerRequests playerRequests;
    private readonly AudioSystem audio;
    private readonly EntitySystem entities;
    private readonly EntityActionSystem entityAction;
    private readonly EntityStatesSystem entityStates;
    private readonly EntityPlatesSystem entityPlates;
    private readonly BuffSystem buff;
    private readonly SkillCastSystem skillCast;
    private readonly BasicAttackSystem basicAttack;

    public CombatSystem(IWorldContext context, CombatSession combat, ItemsSession items, SelfSession self, VendorSession vendor, IPlayerRequests playerRequests, SelfAvatar avatar, AudioSystem audio, EntitySystem entities, EntityActionSystem entityAction, EntityStatesSystem entityStates, EntityPlatesSystem entityPlates, BuffSystem buff, SkillCastSystem skillCast, BasicAttackSystem basicAttack)
    {
        this.context = context;
        this.combat = combat;
        this.items = items;
        this.self = self;
        this.vendor = vendor;
        this.playerRequests = playerRequests;
        this.avatar = avatar;
        this.audio = audio;
        this.entities = entities;
        this.entityAction = entityAction;
        this.entityStates = entityStates;
        this.entityPlates = entityPlates;
        this.buff = buff;
        this.skillCast = skillCast;
        this.basicAttack = basicAttack;
    }

    public void Build()
    {
        skillCast.SkillRevived += Revive;
    }

    internal void Attach()
    {
        combat.AttackEvent += basicAttack.OnAttack;
        combat.MagicEvent += skillCast.OnMagic;
        combat.BuffExpiredEvent += buff.OnBuffExpired;
        combat.DeadEvent += OnDead;
        self.SelfHpEvent += OnSelfHp;
        self.SelfMpEvent += OnSelfMp;
        self.SelfStaminaEvent += OnSelfStamina;
        self.RegeneEvent += OnRegene;
    }

    public void Dispose()
    {
        skillCast.SkillRevived -= Revive;
        combat.AttackEvent -= basicAttack.OnAttack;
        combat.MagicEvent -= skillCast.OnMagic;
        combat.BuffExpiredEvent -= buff.OnBuffExpired;
        combat.DeadEvent -= OnDead;
        self.SelfHpEvent -= OnSelfHp;
        self.SelfMpEvent -= OnSelfMp;
        self.SelfStaminaEvent -= OnSelfStamina;
        self.RegeneEvent -= OnRegene;
        items.ItemStatsEvent -= OnItemStats;
    }

    internal void Start()
    {
        SkillData.EnsureLoaded();
        items.ItemStatsEvent += OnItemStats;
        selfStance = false;
        playerRequests.CombatStance(false);
        buff.RestoreBuffs();
    }

    internal void Update(double now)
    {
        if (!context.Self.Dead && self.Vitals.Known && self.Vitals.Hp <= 0) EnterSelfDeath();
        skillCast.Tick(now);
        audio.BattleMusicTick(now);
        basicAttack.Tick(now);
        ReapCorpses(now);
    }

    private readonly List<int> corpseScratch = new();

    private void ReapCorpses(double now)
    {
        corpseScratch.Clear();
        foreach (var (id, e) in context.Entities.Views)
            if (e.Dead && e.CorpseRemoveAt > 0 && now >= e.CorpseRemoveAt)
                corpseScratch.Add(id);
        foreach (var id in corpseScratch)
        {
            if (context.Selection.SelectedId == id) context.Selection.Deselect();
            entities.RemoveEntity(id);
        }
    }

    private void OnDead(int victimId, int killerId)
    {
        if (victimId == context.Self.CharId)
        {
            audio.Death(victimId);
            if (context.Entities.Views.TryGetValue(killerId, out var killer) && !killer.IsNpc && killer.Name.Length > 0)
                context.Log.Add(TextTemplate.Fill(GameText.System(CombatLogLines.KilledByText, "You've been killed by %s"), killer.Name),
                    CombatLogKind.Incoming, KilledByColour);
            EnterSelfDeath();
            return;
        }
        if (context.Entities.Views.TryGetValue(victimId, out var e))
        {
            entityAction.BeginDying(e);
            if (basicAttack.AutoAttack && basicAttack.AutoTargetId == victimId) basicAttack.StopAutoAttack();
        }
    }

    private void EnterSelfDeath()
    {
        if (context.Self.Dead) return;
        avatar.Dead = true;
        vendor.EndTrade();
        self.ClearStun();
        basicAttack.StopAutoAttack();
        context.Selection.Deselect();
        skillCast.ForgetPendingCasts();
        avatar.Flinch?.Stop();
        double len = ActionAnimation.PlayActionOn(avatar.Anim,
            entityAction.WearsMonsterLook(context.Self.CharId) ? DeathPoses.MonsterKeepClip : DeathPoses.PlayerKeepClip, ActionAnimation.DeathClips);
        avatar.ActionUntil = FrameClock.Now() + Mathf.Max((float)len, SelfDeathActionMinSeconds);
        avatar.ActionRank = ModelAnimation.ActionRankDeath;
        SelfDied?.Invoke();
    }

    internal event Action? SelfDied;

    internal event Action? SelfResurrected;

    internal event Action? SelfRespawned;

    internal event Action? SelfMaximaApplied;

    private void OnSelfHp(SelfHpChange change)
    {
        var (hp, maxHp, dmg, recovered, attackerId) = change;
        if (context.Self.Dead && hp > 0) OnSelfResurrected();
        if (dmg > 0)
        {
            int shown = CombatLogLines.LoggedHp(context.Zone, dmg);
            context.Log.Add(context.Entities.Views.TryGetValue(attackerId, out var hitter) && !hitter.IsNpc && hitter.Name.Length > 0
                ? TextTemplate.Fill(GameText.System(CombatLogLines.PlayerDamageText, "%s is inflicted with %d HP damage."), entityPlates.ShownName(hitter), shown)
                : TextTemplate.Fill(GameText.System(CombatLogLines.HpDamageText, "%d HP Damage"), shown), CombatLogKind.Incoming, tab: LogTab.State);
        }
        else if (recovered > 0)
            context.Log.Add(TextTemplate.Fill(GameText.System(CombatLogLines.HpRecoveredText, "%d HP Recovered"),
                CombatLogLines.LoggedHp(context.Zone, recovered)), CombatLogKind.Recovery, tab: LogTab.State);
        if (dmg > 0 && context.Self.Body != null)
        {
            context.Floaters?.Wound(dmg);
            if (!context.Self.Dead)
            {
                if (avatar.Sitting) entityStates.StandUp();
                audio.Struck(context.Self.CharId);
            }
        }
        else if (recovered > 0 && context.Self.Body != null)
        {
            if (attackerId >= 0) context.Floaters?.Cure(context.Self.CharId, recovered);
            else context.Floaters?.RegenHp(recovered);
        }
    }

    private void OnSelfStamina(VitalChange change)
    {
        var (sp, maxSp, delta) = change;
        if (delta < 0)
            context.Log.Add(TextTemplate.Fill(GameText.System(VpUsedText, "%d SP Used"), -delta), CombatLogKind.Spent, tab: LogTab.State);
    }

    private const int VpUsedText = 3014;
    private const float SelfDeathActionMinSeconds = 2.5f;
    private static readonly Color KilledByColour = new("ff8080");

    private void OnSelfMp(VitalChange change)
    {
        var (mp, maxMp, delta) = change;
        if (delta > 0)
        {
            context.Log.Add(TextTemplate.Fill(GameText.System(CombatLogLines.ManaText(self.Class, recovered: true), "%d MP Recovered"), delta),
                CombatLogKind.Resource, tab: LogTab.State);
            context.Floaters?.RegenMp(delta);
        }
        else if (delta < 0)
            context.Log.Add(TextTemplate.Fill(GameText.System(CombatLogLines.ManaText(self.Class, recovered: false), "%d MP Used"), -delta),
                CombatLogKind.Spent, tab: LogTab.State);
    }

    private void OnItemStats(DerivedStats stats)
    {
        self.Vitals.ApplyMaxima(stats.MaxHp, stats.MaxMp);
        SelfMaximaApplied?.Invoke();
    }

    private void Revive(int id)
    {
        if (id == context.Self.CharId) { OnSelfResurrected(); return; }
        if (!context.Entities.Views.TryGetValue(id, out var e) || !e.Dead) return;
        e.Dead = false;
        e.DeathDueAt = 0;
        e.CorpseRemoveAt = 0;
        e.ActionUntil = 0;
        e.ActionRank = 0;
        e.ActionClip = null;
        e.Clip = null;
        NpcGatesFeature.RefreshGateBlocker(e);
    }

    private void OnSelfResurrected()
    {
        if (!context.Self.Dead) return;
        avatar.Dead = false;
        avatar.ActionUntil = 0;
        avatar.Clip = null;
        SelfResurrected?.Invoke();
        context.Log.Add("You have been resurrected.", CombatLogKind.Recovery);
    }

    private void OnRegene(float mapX, float mapZ)
    {
        if (context.Self.Body == null) return;
        float mapY = avatar.MapY;
        var pos = context.Terrain.GroundPos(mapX, mapZ, mapY, context.Self.Lift);
        context.Self.Body.Position = pos;
        avatar.LastFreePosition = pos;
        avatar.MapX = mapX; avatar.MapZ = mapZ; avatar.MapY = mapY;
        avatar.Dead = false;
        avatar.ActionUntil = 0;
        skillCast.EndCast(0);
        buff.ClearAllBuffs();
        avatar.Clip = null;
        SelfRespawned?.Invoke();
        context.Log.Add("You returned to the battlefield.", CombatLogKind.Status);
    }

    private bool selfStance;

    internal bool SelfStanceReady() =>
        !context.Self.Dead
        && context.Selection.SelectedId >= 0
        && context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var target)
    && target.Attackable
    && !target.Dead;

    internal void StanceTick()
    {
        bool ready = SelfStanceReady();
        if (ready == selfStance) return;
        selfStance = ready;
        playerRequests.CombatStance(ready);
    }

}
