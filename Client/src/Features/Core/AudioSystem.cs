using Godot;

namespace LibreKO.Features.Core;

internal sealed class AudioSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly SelfSession self;
    private readonly EntityActionSystem entityAction;
    private readonly ScenerySystem scenery;
    private readonly EntityLooksSystem entityLooks;
    private readonly EntityStatesSystem entityStates;

    public AudioSystem(IWorldContext context, SelfSession self, SelfAvatar avatar, EntityActionSystem entityAction, ScenerySystem scenery, EntityLooksSystem entityLooks, EntityStatesSystem entityStates)
    {
        this.context = context;
        this.self = self;
        this.avatar = avatar;
        this.entityAction = entityAction;
        this.scenery = scenery;
        this.entityLooks = entityLooks;
        this.entityStates = entityStates;
    }

    public void Dispose()
    {
        entityAction.DeathCry -= Death;
        Teardown();
    }

    private const float FootstepEntityDist = 22f;

    private string? selfStepClip;
    private double selfStepPos;
    private int selfStepPhase;
    public void Build()
    {
        entityAction.DeathCry += Death;
        SoundCatalog.EnsureLoaded();
        Audio.SetListener(context.Camera);
        StartZoneBgm();
    }

    private bool battleMusic;
    private double battleMusicCheckAt;

    internal void EnterBattleMusic()
    {
        if (battleMusic || SoundCatalog.Zone(context.Zone) is not { } bgm) return;
        int id = BattleMusic.Track(bgm.KaBattle, bgm.ElBattle, self.LastEnter.Nation == Nations.Karus);
        if (id == 0) return;
        battleMusic = true;
        battleMusicCheckAt = context.Now + BattleMusic.CheckSeconds;
        Audio.Bgm(id);
    }

    internal void BattleMusicTick(double now)
    {
        if (!battleMusic || now < battleMusicCheckAt || avatar.Body == null) return;
        battleMusicCheckAt = now + BattleMusic.CheckSeconds;
        if (!BattleMusic.Ends(battleMusic, NearestHostile())) return;
        battleMusic = false;
        StartZoneBgm();
    }

    private float NearestHostile()
    {
        float nearest = float.MaxValue;
        var at = avatar.Body.GlobalPosition;
        foreach (var e in context.Entities.Views.Values)
            if (e.Attackable && !e.Dead && GodotObject.IsInstanceValid(e.Body))
                nearest = Mathf.Min(nearest, e.Body.GlobalPosition.DistanceTo(at));
        return nearest;
    }

    private void StartZoneBgm()
    {
        bool karus = self.LastEnter.Nation == Nations.Karus;
        int id = 0;
        if (SoundCatalog.Zone(context.Zone) is { } bgm)
        {
            id = karus ? bgm.KaAmbient : bgm.ElAmbient;
            if (id == 0) id = karus ? bgm.KaBattle : bgm.ElBattle;
        }
        if (id == 0)
        {
            GD.Print($"[sound] zone {context.Zone} has no BGM entry — using the town theme");
            id = Sfx.BgmTown;
        }
        Audio.Bgm(id);
    }

    internal void FxAt(int fxId, int entityId)
    {
        int sid = SoundCatalog.FxSound(fxId);
        if (sid == 0 || MutedPlayer(entityId)) return;
        var pos = context.Entities.WorldPosOf(entityId) ?? avatar.Body.GlobalPosition;
        if (pos.DistanceSquaredTo(avatar.Body.GlobalPosition) > Config.EffectSoundDistance * Config.EffectSoundDistance) return;
        Audio.Play(sid, pos);
    }

    private bool MutedPlayer(int entityId) =>
        !Config.OtherPlayersSounds && entityId != context.Self.CharId
        && context.Entities.Views.TryGetValue(entityId, out var e) && !e.IsNpc && !e.IsMonster;

    internal SoundCatalog.LooksSounds? LooksOf(EntityView e) =>
        SoundCatalog.Looks(e.IsNpc || e.IsMonster ? e.ModelId : e.Race);

    internal void PlayEntitySound(EntityView e, int soundId, SoundOwnerKind kind)
    {
        if (soundId == 0 || !GodotObject.IsInstanceValid(e.Body) || SoundCues.MutedPlayer(e)) return;
        Audio.PlayOwned(soundId, e.Body.GlobalPosition, e.Body.GetInstanceId(), kind, single: e.IsMonster);
    }

    internal bool SelfSwinging() => avatar.ActionRank == ModelAnimation.ActionRankBasic && context.Now < avatar.ActionUntil;

    private bool SelfStriking() =>
        avatar.ActionRank is ModelAnimation.ActionRankBasic or ModelAnimation.ActionRankSkill && context.Now < avatar.ActionUntil;

    private readonly RateBudget gatherFxBudget = new();

    internal bool RemoteGatherFxAllowed(EntityView e)
    {
        var limits = CrowdModes.Of(Config.Crowd);
        if (!e.OnScreen || avatar.Body == null) return false;
        if (Coord.FlatDistance(e.Body.GlobalPosition, avatar.Body.GlobalPosition) > limits.GatherFxRange) return false;
        return gatherFxBudget.Take(limits.GatherFxPerSecond, context.Now);
    }

    private void MiningStrike(Node3D body, bool audible = true)
    {
        Vector3 at = body.GlobalPosition - body.GlobalTransform.Basis.Z * GatherSystem.MiningStrikeReach;
        if (scenery.FxGroundHeight(at.X, at.Z) is { } groundY) at.Y = groundY;
        if (Fx.NameForId(GatherSystem.MiningStrikeFx) is { } name)
            Fx.Spawn(name, context.Root, at, oneShot: true);
        int sid = SoundCatalog.FxSound(GatherSystem.MiningStrikeFx);
        if (sid != 0 && audible) Audio.PlayOwned(sid, at, body.GetInstanceId(), SoundOwnerKind.Mining);
    }

    internal void SelfStrike()
    {
        if (avatar.StrikeTarget < 0) return;
        if (!context.Entities.Views.TryGetValue(avatar.StrikeTarget, out var v)) return;
        if (!GodotObject.IsInstanceValid(v.Body) || (v.Dead && !EntityActionSystem.Dying(v))) return;
        SkillFxRig.SpawnHitImpact(context.Root, v, avatar.Body, entityLooks.SelfGear(), npcAttacker: false, entityStates.IsWearingTransformLook(context.Self.CharId), avatar.StrikeAnim);
        SoundCues.AudioWeaponImpact(avatar.Body.GlobalPosition, v.Body);
        entityAction.FinishDying(v);
    }

    internal void EntityStrike(EntityView attacker)
    {
        if (attacker.StrikeTarget < 0) return;
        if (!context.Entities.Views.TryGetValue(attacker.StrikeTarget, out var v)) return;
        if (!GodotObject.IsInstanceValid(v.Body) || (v.Dead && !EntityActionSystem.Dying(v))) return;
        SkillFxRig.SpawnHitImpact(context.Root, v, attacker.Body, attacker.Gear, attacker.IsNpc, entityStates.IsWearingTransformLook(attacker.Id), attacker.StrikeAnim);
        if (!SoundCues.MutedPlayer(attacker) || v.Id == context.Self.CharId) SoundCues.AudioWeaponImpact(attacker.Body.GlobalPosition, v.Body);
        entityAction.FinishDying(v);
    }

    internal void MoveAttackTick(EntityView e, double now)
    {
        if (e.MoveSwingAt >= 0 && now >= e.MoveSwingAt)
        {
            e.MoveSwingAt = -1;
            PlayEntitySound(e, SoundCues.SwingSound(e.Gear, LooksOf(e)), SoundOwnerKind.Swing);
        }
        if (e.MoveStrikeAt >= 0 && now >= e.MoveStrikeAt)
        {
            e.MoveStrikeAt = -1;
            EntityStrike(e);
        }
    }

    internal void SelfSwing()
    {
        int sid = SoundCues.SwingSound(entityLooks.SelfGear(), SoundCatalog.Looks(self.Race));
        if (sid != 0) Audio.PlayOwned(sid, avatar.Body.GlobalPosition, avatar.Body.GetInstanceId(), SoundOwnerKind.Swing);
    }

    internal void Struck(int victimId)
    {
        if (victimId == context.Self.CharId)
        {
            if (SoundCatalog.Looks(self.Race) is { } mine)
            {
                int sid = SoundCues.PickVariant(mine.Struck0, mine.Struck1);
                if (sid != 0) Audio.PlayOwned(sid, avatar.Body.GlobalPosition, avatar.Body.GetInstanceId(), SoundOwnerKind.Struck);
            }
            return;
        }
        if (context.Entities.Views.TryGetValue(victimId, out var v) && LooksOf(v) is { } l)
            PlayEntitySound(v, SoundCues.PickVariant(l.Struck0, l.Struck1), SoundOwnerKind.Struck);
    }

    internal void Death(int victimId)
    {
        if (victimId == context.Self.CharId)
        {
            if (SoundCatalog.Looks(self.Race) is { } mine)
            {
                int sid = SoundCues.PickVariant(mine.Dead0, mine.Dead1);
                if (sid != 0) Audio.PlayOwned(sid, avatar.Body.GlobalPosition, avatar.Body.GetInstanceId(), SoundOwnerKind.Death);
            }
            return;
        }
        if (context.Entities.Views.TryGetValue(victimId, out var v) && LooksOf(v) is { } l)
            PlayEntitySound(v, SoundCues.PickVariant(l.Dead0, l.Dead1), SoundOwnerKind.Death);
    }

    internal void FootstepTick()
    {
        var beats = SoundCues.AnimEventCrossings(avatar.Anim, ref selfStepClip, ref selfStepPos);
        if (beats.None) return;

        if (context.Entities.Views.TryGetValue(context.Self.CharId, out var selfView) && selfView.Gathering && !selfView.GatherFishing)
        {
            for (int i = 0; i < beats.Strikes; i++) MiningStrike(avatar.Body);
            return;
        }

        if (SelfStriking())
        {
            if (SelfSwinging())
                for (int i = 0; i < beats.Swings; i++) SelfSwing();
            for (int i = 0; i < beats.Strikes; i++) SelfStrike();
            return;
        }

        if (avatar.Dead || !avatar.Moving || avatar.Sitting) return;

        bool running = avatar.Running && !avatar.MovingBackward;
        for (int i = 0; i < beats.Swings; i++)
        {
            int sid = FootstepFor(self.Race, running, ref selfStepPhase);
            if (sid != 0) Audio.PlayOwned(sid, avatar.Body.GlobalPosition, avatar.Body.GetInstanceId(), SoundOwnerKind.Step);
        }
    }

    private int FootstepFor(int race, bool running, ref int phase)
    {
        if (SoundCatalog.Steps(race) is not { } s)
            return 0;
        phase++;
        if (!running)
            return (phase & 1) == 0 ? s.Walk1 : s.Walk2;
        return (phase % 3) switch { 0 => s.Run1, 1 => s.Run2, _ => s.Run3 };
    }

    internal void EntityStepTick(EntityView e, double now, Vector3 selfPos, Vector3 pos)
    {
        if (e.AnimPaused) return;
        bool striking = SoundCues.EntityStriking(e, now);
        if (!striking && !e.Gathering && !EntityRig.EntityMoving(e, pos)) { e.StepClip = null; return; }
        if (!GodotObject.IsInstanceValid(e.Body)) return;
        var beats = SoundCues.AnimEventCrossings(e.Anim, ref e.StepClip, ref e.StepPos);
        if (beats.None) return;

        if (e.Gathering && !e.GatherFishing)
        {
            for (int i = 0; i < beats.Strikes; i++)
                if (RemoteGatherFxAllowed(e)) MiningStrike(e.Body, !SoundCues.MutedPlayer(e));
            return;
        }

        if (striking)
        {
            if (SoundCues.EntitySwinging(e, now))
                for (int i = 0; i < beats.Swings; i++)
                    PlayEntitySound(e, SoundCues.SwingSound(e.Gear, LooksOf(e)), SoundOwnerKind.Swing);
            for (int i = 0; i < beats.Strikes; i++) EntityStrike(e);
            return;
        }

        if (!EntityRig.EntityMoving(e, pos)) return;
        if (pos.DistanceSquaredTo(selfPos) > FootstepEntityDist * FootstepEntityDist) return;

        bool running = e.Speed >= EntityRig.RunThresholdOf(e);
        for (int i = 0; i < beats.Swings; i++)
        {
            int sid;
            if (e.IsNpc || e.IsMonster)
            {
                if (LooksOf(e) is not { } l || l.Move == 0) return;
                sid = l.Move;
            }
            else
            {
                int phase = e.StepPhase;
                sid = FootstepFor(e.Race, running, ref phase);
                e.StepPhase = phase;
            }
            PlayEntitySound(e, sid, SoundOwnerKind.Step);
        }
    }

    private void Teardown()
    {
        Audio.StopBgm();
        Audio.SetListener(null);
    }
}
