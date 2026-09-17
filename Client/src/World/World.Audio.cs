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
        if (sid == 0) return;
        var pos = WorldPosOf(entityId);
        Audio.Play(sid, pos ?? _self.GlobalPosition);
    }

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

    private void PlayEntitySound(Ent e, int soundId)
    {
        if (soundId == 0 || !GodotObject.IsInstanceValid(e.Body)) return;
        Audio.Play(soundId, e.Body.GlobalPosition);
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

    private void MiningStrike(Node3D body)
    {
        Vector3 at = body.GlobalPosition - body.GlobalTransform.Basis.Z * MiningStrikeReach;
        if (FxGroundHeight(at.X, at.Z) is { } groundY) at.Y = groundY;
        if (Fx.NameForId(MiningStrikeFx) is { } name)
            Fx.Spawn(name, this, at, oneShot: true);
        int sid = SoundCatalog.FxSound(MiningStrikeFx);
        if (sid != 0) Audio.Play(sid, at);
    }

    private void SelfStrike()
    {
        if (_strikeTargetSelf < 0) return;
        if (!_ents.TryGetValue(_strikeTargetSelf, out var v)) return;
        if (!GodotObject.IsInstanceValid(v.Body) || v.Dead) return;
        SpawnHitImpact(v, v.Body.GlobalPosition - _self.GlobalPosition, SelfWeaponItem());
        AudioWeaponImpact(_self.GlobalPosition, v.Body.GlobalPosition);
    }

    private void EntityStrike(Ent attacker)
    {
        if (attacker.StrikeTarget < 0) return;
        if (!_ents.TryGetValue(attacker.StrikeTarget, out var v)) return;
        if (!GodotObject.IsInstanceValid(v.Body) || v.Dead) return;
        int weapon = HeldWeapon(attacker.Gear);
        SpawnHitImpact(v, (v.Body.GlobalPosition - attacker.Body.GlobalPosition).Normalized(), weapon);
        AudioWeaponImpact(attacker.Body.GlobalPosition, v.Body.GlobalPosition);
    }

    private const float ImpactSoundPullback = 6.0f;

    private static void AudioWeaponImpact(Vector3 from, Vector3 to)
    {
        var away = to - from;
        float dist = away.Length();
        var at = dist <= ImpactSoundPullback
            ? from
            : from + away / dist * (dist - ImpactSoundPullback);
        Audio.Play(Sfx.WeaponImpact, at);
    }

    private void AudioSelfSwing()
    {
        int sid = SwingSound(SelfGear(), SoundCatalog.Looks(_selfRace));
        if (sid != 0) Audio.Play(sid, _self.GlobalPosition);
    }

    private void AudioStruck(int victimId)
    {
        if (victimId == _myId)
        {
            if (SoundCatalog.Looks(_selfRace) is { } mine)
            {
                int sid = Pick(mine.Struck0, mine.Struck1);
                if (sid != 0) Audio.Play(sid, _self.GlobalPosition);
            }
            return;
        }
        if (_ents.TryGetValue(victimId, out var v) && LooksOf(v) is { } l)
            PlayEntitySound(v, Pick(l.Struck0, l.Struck1));
    }

    private void AudioDeath(int victimId)
    {
        if (victimId == _myId)
        {
            if (SoundCatalog.Looks(_selfRace) is { } mine)
            {
                int sid = Pick(mine.Dead0, mine.Dead1);
                if (sid != 0) Audio.Play(sid, _self.GlobalPosition);
            }
            return;
        }
        if (_ents.TryGetValue(victimId, out var v) && LooksOf(v) is { } l)
            PlayEntitySound(v, Pick(l.Dead0, l.Dead1));
    }

    private readonly record struct AnimBeats(int Swings, int Strikes)
    {
        public bool None => Swings == 0 && Strikes == 0;
    }

    private static AnimBeats AnimEventCrossings(AnimationPlayer? anim, ref string? clip, ref double prev)
    {
        if (anim == null) { clip = null; return default; }
        string name = anim.CurrentAnimation.ToString();
        if (name.Length == 0) { clip = null; return default; }
        double pos = anim.CurrentAnimationPosition;
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
            if (sid != 0) Audio.Play(sid, _self.GlobalPosition);
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

    private void AudioEntityStepTick(Ent e, double now, Vector3 selfPos)
    {
        if (!GodotObject.IsInstanceValid(e.Body)) return;
        var beats = AnimEventCrossings(e.Anim, ref e.StepClip, ref e.StepPos);
        if (beats.None) return;

        if (e.Gathering && !e.GatherFishing)
        {
            for (int i = 0; i < beats.Strikes; i++) MiningStrike(e.Body);
            return;
        }

        if (EntityStriking(e, now))
        {
            if (EntitySwinging(e, now))
                for (int i = 0; i < beats.Swings; i++)
                    PlayEntitySound(e, SwingSound(e.Gear, LooksOf(e)));
            for (int i = 0; i < beats.Strikes; i++) EntityStrike(e);
            return;
        }

        if (!EntityMoving(e)) return;
        if (e.Body.GlobalPosition.DistanceSquaredTo(selfPos) > FootstepEntityDist * FootstepEntityDist) return;

        bool running = e.Speed >= RunThreshold;
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
            PlayEntitySound(e, sid);
        }
    }

    private void TeardownAudio()
    {
        _fxAmbient.Clear();
        Audio.StopBgm();
        Audio.SetListener(null);
    }
}
