using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const float FootstepEntityDist = 22f;
    private const int SoundSlots = 2;

    private readonly Dictionary<Node3D, AudioStreamPlayer3D> _fxAmbient = new();
    private string? _selfStepClip;
    private double _selfStepPos;
    private int _selfStepPhase;
    private int _strikeTargetSelf = -1;

    private void BuildAudio()
    {
        SoundCatalog.EnsureLoaded();
        Audio.SetListener(_camera);
        StartZoneBgm();
    }

    private bool _battleMusic;
    private double _battleMusicCheckAt;

    private void EnterBattleMusic()
    {
        if (_battleMusic || SoundCatalog.Zone(_zone) is not { } bgm) return;
        int id = BattleMusic.Track(bgm.KaBattle, bgm.ElBattle, Net.I.LastEnter.Nation == Nations.Karus);
        if (id == 0) return;
        _battleMusic = true;
        _battleMusicCheckAt = Now() + BattleMusic.CheckSeconds;
        Audio.Bgm(id);
    }

    private void BattleMusicTick(double now)
    {
        if (!_battleMusic || now < _battleMusicCheckAt || _self == null) return;
        _battleMusicCheckAt = now + BattleMusic.CheckSeconds;
        if (!BattleMusic.Ends(_battleMusic, NearestHostile())) return;
        _battleMusic = false;
        StartZoneBgm();
    }

    private float NearestHostile()
    {
        float nearest = float.MaxValue;
        var at = _self.GlobalPosition;
        foreach (var e in _ents.Values)
            if (e.Attackable && !e.Dead && IsInstanceValid(e.Body))
                nearest = Mathf.Min(nearest, e.Body.GlobalPosition.DistanceTo(at));
        return nearest;
    }

    private void StartZoneBgm()
    {
        bool karus = Net.I.LastEnter.Nation == Nations.Karus;
        int id = 0;
        if (SoundCatalog.Zone(_zone) is { } bgm)
        {
            id = karus ? bgm.KaAmbient : bgm.ElAmbient;
            if (id == 0) id = karus ? bgm.KaBattle : bgm.ElBattle;
        }
        if (id == 0)
        {
            GD.Print($"[sound] zone {_zone} has no BGM entry — using the town theme");
            id = Sfx.BgmTown;
        }
        Audio.Bgm(id);
    }

    private void AudioItemMove(int itemId, int from, int to)
    {
        if (from >= GridStart && to >= GridStart) { Audio.PlayUi(Sfx.UiButton); return; }
        var def = ItemData.Get(itemId);
        Audio.PlayUi(def?.Slot is 3 or 4 ? Sfx.ItemWeapon : Sfx.ItemArmor);
    }

    private void AudioFxAt(int fxId, int entityId)
    {
        int sid = SoundCatalog.FxSound(fxId);
        if (sid == 0 || MutedPlayer(entityId)) return;
        var pos = WorldPosOf(entityId) ?? _self.GlobalPosition;
        if (pos.DistanceSquaredTo(_self.GlobalPosition) > Config.EffectSoundDistance * Config.EffectSoundDistance) return;
        Audio.Play(sid, pos);
    }

    private bool MutedPlayer(int entityId) =>
        !Config.OtherPlayersSounds && entityId != _myId
        && _ents.TryGetValue(entityId, out var e) && !e.IsNpc && !e.IsMonster;

    private static bool MutedPlayer(Ent e) => !Config.OtherPlayersSounds && !e.IsNpc && !e.IsMonster;

    private void AttachFxAmbience(Node3D node, string fxName)
    {
        int sid = SoundCatalog.FxSound(fxName);
        if (sid == 0 || !SoundCatalog.TryGet(sid, out var e)) return;
        if (e.Type != SoundCatalog.Kind.ThreeD) return;
        if (Audio.Loop(sid, node) is { } player)
        {
            player.StreamPaused = true;
            _fxAmbient[node] = player;
        }
    }

    private void SetFxAmbience(Node3D node, bool audible)
    {
        if (_fxAmbient.TryGetValue(node, out var p) && GodotObject.IsInstanceValid(p))
            p.StreamPaused = !audible;
    }

    private SoundCatalog.LooksSounds? LooksOf(Ent e) =>
        SoundCatalog.Looks(e.IsNpc || e.IsMonster ? e.ModelId : e.Race);

    private void PlayEntitySound(Ent e, int soundId, SoundOwnerKind kind)
    {
        if (soundId == 0 || !GodotObject.IsInstanceValid(e.Body) || MutedPlayer(e)) return;
        Audio.PlayOwned(soundId, e.Body.GlobalPosition, e.Body.GetInstanceId(), kind, single: e.IsMonster);
    }

    private static int Pick(int a, int b) => b != 0 && GD.Randf() < 0.5f ? b : a != 0 ? a : b;

    private static int HeldWeapon(int[]? gear)
    {
        if (gear == null) return 0;
        int right = gear.Length > InventoryConstants.VisRightHand ? gear[InventoryConstants.VisRightHand] : 0;
        if (right != 0) return right;
        return gear.Length > InventoryConstants.VisLeftHand ? gear[InventoryConstants.VisLeftHand] : 0;
    }

    private static int SwingSound(int[]? gear, SoundCatalog.LooksSounds? looks)
    {
        int weapon = HeldWeapon(gear);
        return weapon != 0 ? SoundCatalog.ItemSwing(weapon) : looks?.Attack0 ?? 0;
    }

    private bool SelfSwinging() => _selfActionRank == ActionRankBasic && Now() < _selfActionUntil;

    private bool SelfStriking() =>
        _selfActionRank is ActionRankBasic or ActionRankSkill && Now() < _selfActionUntil;

    private static bool EntitySwinging(Ent e, double now) =>
        e.ActionRank == ActionRankBasic && now < e.ActionUntil;

    private static bool EntityStriking(Ent e, double now) =>
        e.ActionRank is ActionRankBasic or ActionRankSkill && now < e.ActionUntil;

    private void LatchStrikeTarget(int actorId, int targetId)
    {
        if (actorId == _myId) _strikeTargetSelf = targetId;
        else if (_ents.TryGetValue(actorId, out var a)) a.StrikeTarget = targetId;
    }

    private readonly RateBudget _gatherFxBudget = new();

    private bool RemoteGatherFxAllowed(Ent e)
    {
        var limits = CrowdModes.Of(Config.Crowd);
        if (!e.OnScreen || _self == null) return false;
        if (FlatDistance(e.Body.GlobalPosition, _self.GlobalPosition) > limits.GatherFxRange) return false;
        return _gatherFxBudget.Take(limits.GatherFxPerSecond, Now());
    }

    private void MiningStrike(Node3D body, bool audible = true)
    {
        Vector3 at = body.GlobalPosition - body.GlobalTransform.Basis.Z * MiningStrikeReach;
        if (FxGroundHeight(at.X, at.Z) is { } groundY) at.Y = groundY;
        if (Fx.NameForId(MiningStrikeFx) is { } name)
            Fx.Spawn(name, this, at, oneShot: true);
        int sid = SoundCatalog.FxSound(MiningStrikeFx);
        if (sid != 0 && audible) Audio.PlayOwned(sid, at, body.GetInstanceId(), SoundOwnerKind.Mining);
    }

    private void SelfStrike()
    {
        if (_strikeTargetSelf < 0) return;
        if (!_ents.TryGetValue(_strikeTargetSelf, out var v)) return;
        if (!GodotObject.IsInstanceValid(v.Body) || (v.Dead && !Dying(v))) return;
        SpawnHitImpact(v, _self, SelfGear(), npcAttacker: false, IsWearingTransformLook(_myId), _selfStrikeAnim);
        AudioWeaponImpact(_self.GlobalPosition, v.Body);
        FinishDying(v);
    }

    private void EntityStrike(Ent attacker)
    {
        if (attacker.StrikeTarget < 0) return;
        if (!_ents.TryGetValue(attacker.StrikeTarget, out var v)) return;
        if (!GodotObject.IsInstanceValid(v.Body) || (v.Dead && !Dying(v))) return;
        SpawnHitImpact(v, attacker.Body, attacker.Gear, attacker.IsNpc, IsWearingTransformLook(attacker.Id), attacker.StrikeAnim);
        if (!MutedPlayer(attacker) || v.Id == _myId) AudioWeaponImpact(attacker.Body.GlobalPosition, v.Body);
        FinishDying(v);
    }

    private const float ImpactSoundPullback = 6.0f;

    private static void AudioWeaponImpact(Vector3 from, Node3D target)
    {
        var away = target.GlobalPosition - from;
        float dist = away.Length();
        var at = dist <= ImpactSoundPullback
            ? from
            : from + away / dist * (dist - ImpactSoundPullback);
        Audio.PlayOwned(Sfx.WeaponImpact, at, target.GetInstanceId(), SoundOwnerKind.Impact);
    }

    private void AudioSelfSwing()
    {
        int sid = SwingSound(SelfGear(), SoundCatalog.Looks(_selfRace));
        if (sid != 0) Audio.PlayOwned(sid, _self.GlobalPosition, _self.GetInstanceId(), SoundOwnerKind.Swing);
    }

    private void AudioStruck(int victimId)
    {
        if (victimId == _myId)
        {
            if (SoundCatalog.Looks(_selfRace) is { } mine)
            {
                int sid = Pick(mine.Struck0, mine.Struck1);
                if (sid != 0) Audio.PlayOwned(sid, _self.GlobalPosition, _self.GetInstanceId(), SoundOwnerKind.Struck);
            }
            return;
        }
        if (_ents.TryGetValue(victimId, out var v) && LooksOf(v) is { } l)
            PlayEntitySound(v, Pick(l.Struck0, l.Struck1), SoundOwnerKind.Struck);
    }

    private void AudioDeath(int victimId)
    {
        if (victimId == _myId)
        {
            if (SoundCatalog.Looks(_selfRace) is { } mine)
            {
                int sid = Pick(mine.Dead0, mine.Dead1);
                if (sid != 0) Audio.PlayOwned(sid, _self.GlobalPosition, _self.GetInstanceId(), SoundOwnerKind.Death);
            }
            return;
        }
        if (_ents.TryGetValue(victimId, out var v) && LooksOf(v) is { } l)
            PlayEntitySound(v, Pick(l.Dead0, l.Dead1), SoundOwnerKind.Death);
    }

    private readonly record struct AnimBeats(int Swings, int Strikes)
    {
        public bool None => Swings == 0 && Strikes == 0;
    }

    private static AnimBeats AnimEventCrossings(AnimationPlayer? anim, ref string? clip, ref double prev)
    {
        if (anim == null) { clip = null; return default; }
        if (!CrowdAnimator.Current(anim, out string name, out double pos))
        {
            using var current = anim.CurrentAnimation;
            name = current.ToString();
            pos = name.Length == 0 ? 0 : anim.CurrentAnimationPosition;
        }
        if (name.Length == 0) { clip = null; return default; }
        if (name != clip) { clip = name; prev = pos; return default; }

        double last = prev;
        prev = pos;
        if (AnimationMetaFor(anim, name) is not { } meta) return default;

        int swings = 0, strikes = 0;
        for (int slot = 0; slot < SoundSlots; slot++)
        {
            if (Crossed(meta.SoundTime(slot), last, pos)) swings++;
            if (Crossed(meta.StrikeTime(slot), last, pos)) strikes++;
        }
        return new AnimBeats(swings, strikes);

        static bool Crossed(float at, double last, double pos) => at >= 0f && last < at && pos >= at;
    }

    private void AudioFootstepTick()
    {
        var beats = AnimEventCrossings(_selfAnim, ref _selfStepClip, ref _selfStepPos);
        if (beats.None) return;

        if (_gathering && !_gatherFishing)
        {
            for (int i = 0; i < beats.Strikes; i++) MiningStrike(_self);
            return;
        }

        if (SelfStriking())
        {
            if (SelfSwinging())
                for (int i = 0; i < beats.Swings; i++) AudioSelfSwing();
            for (int i = 0; i < beats.Strikes; i++) SelfStrike();
            return;
        }

        if (_selfDead || !_selfMoving || _selfSitting) return;

        bool running = _running && !_selfMovingBackward;
        for (int i = 0; i < beats.Swings; i++)
        {
            int sid = FootstepFor(_selfRace, running, ref _selfStepPhase);
            if (sid != 0) Audio.PlayOwned(sid, _self.GlobalPosition, _self.GetInstanceId(), SoundOwnerKind.Step);
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

    private void AudioEntityStepTick(Ent e, double now, Vector3 selfPos, Vector3 pos)
    {
        if (e.AnimPaused) return;
        bool striking = EntityStriking(e, now);
        if (!striking && !e.Gathering && !EntityMoving(e, pos)) { e.StepClip = null; return; }
        if (!GodotObject.IsInstanceValid(e.Body)) return;
        var beats = AnimEventCrossings(e.Anim, ref e.StepClip, ref e.StepPos);
        if (beats.None) return;

        if (e.Gathering && !e.GatherFishing)
        {
            for (int i = 0; i < beats.Strikes; i++)
                if (RemoteGatherFxAllowed(e)) MiningStrike(e.Body, !MutedPlayer(e));
            return;
        }

        if (striking)
        {
            if (EntitySwinging(e, now))
                for (int i = 0; i < beats.Swings; i++)
                    PlayEntitySound(e, SwingSound(e.Gear, LooksOf(e)), SoundOwnerKind.Swing);
            for (int i = 0; i < beats.Strikes; i++) EntityStrike(e);
            return;
        }

        if (!EntityMoving(e, pos)) return;
        if (pos.DistanceSquaredTo(selfPos) > FootstepEntityDist * FootstepEntityDist) return;

        bool running = e.Speed >= RunThresholdOf(e);
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

    private void TeardownAudio()
    {
        _fxAmbient.Clear();
        Audio.StopBgm();
        Audio.SetListener(null);
    }
}
