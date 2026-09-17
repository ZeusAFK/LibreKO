using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int KindPickaxe = 61, KindFishingRod = 63;
    private const int MiningClip = 63, FishingCastClip = 151, FishingWaitClip = 152;
    private const int FishingWaitFx = 30732;
    private const int MiningStrikeFx = 13080;
    private const float MiningStrikeReach = 1.1f;
    private const double GatherActionRearm = 0.5;
    private const int RightHandGearSlot = 6, LeftHandGearSlot = 7;
    private const double GatherInterval = 5.0;
    private const float GatherFxHeight = 1.2f;

    private static readonly string[] MiningClips =
        { "attack_Twoblunt0_A", "attack_Twoblunt1_A", "attack0", "attack" };
    private static readonly string[] FishingCastClips = { "fishing1", "fishing2", "breath" };
    private static readonly string[] FishingWaitClips = { "fishing2", "fishing1", "breath" };

    private readonly Dictionary<int, bool> _gatherers = new();
    private readonly HashSet<int> _fishingCast = new();

    private bool _gathering;
    private bool _gatherFishing;
    private int _gatherToolSlot = -1;
    private double _gatherNextAttempt;
    private Node3D? _gatherFx;

    private void GatherInit()
    {
        Net.I.GatherStartEvent += OnGatherStart;
        Net.I.GatherResultEvent += OnGatherResult;
        Net.I.GatherStopEvent += OnGatherStop;
        Net.I.ItemDurationEvent += OnItemDuration;
    }

    private void GatherDispose()
    {
        Net.I.GatherStartEvent -= OnGatherStart;
        Net.I.GatherResultEvent -= OnGatherResult;
        Net.I.GatherStopEvent -= OnGatherStop;
        Net.I.ItemDurationEvent -= OnItemDuration;
        _gatherers.Clear();
        _fishingCast.Clear();
    }

    private int FindGatherTool(out int kind)
    {
        kind = 0;
        if (Inv == null) return -1;

        foreach (int slot in new[] { InventoryConstants.RightHand, InventoryConstants.LeftHand })
        {
            if (Inv.Length <= slot) continue;
            var def = ItemData.Get(Inv[slot].ItemId);
            if (def == null) continue;
            if (def.Kind != KindPickaxe && def.Kind != KindFishingRod) continue;
            kind = def.Kind;
            return slot;
        }

        return -1;
    }

    private bool SelfInGatherArea(bool fishing)
    {
        var (koX, koZ) = Coord.ToKo(_self.GlobalPosition.X, _self.GlobalPosition.Z);
        return fishing
            ? GatherZones.IsFishingArea(_zone, koX, koZ)
            : GatherZones.IsMiningArea(_zone, koX, koZ);
    }

    private bool TryStartGather()
    {
        if (_gathering) { StopGather(sendStop: true); return true; }
        if (_selfDead) return false;

        int slot = FindGatherTool(out int kind);
        if (slot < 0) return false;

        bool fishing = kind == KindFishingRod;
        if (!SelfInGatherArea(fishing)) return false;

        if (Inv[slot].Durability <= 0)
        {
            CombatNotice(fishing
                ? "Durability of fishing rod becomes 0."
                : "The durability of pickaxe is 0");
            return true;
        }

        _gatherFishing = fishing;
        _gatherToolSlot = slot;
        if (fishing) Net.I.SendFishingStart(); else Net.I.SendMiningStart();
        return true;
    }

    private void StopGather(bool sendStop)
    {
        if (!_gathering) return;
        _gathering = false;
        SetGatherClipLoop(_myId, loop: false);
        _selfActionUntil = 0;
        _gatherers.Remove(_myId);
        _fishingCast.Remove(_myId);
        if (_ents.TryGetValue(_myId, out var self)) self.Gathering = false;
        ClearGatherFx();
        if (sendStop) { if (_gatherFishing) Net.I.SendFishingStop(); else Net.I.SendMiningStop(); }
    }

    private void ClearGatherFx()
    {
        if (_gatherFx == null) return;
        if (IsInstanceValid(_gatherFx)) _gatherFx.QueueFree();
        _gatherFx = null;
    }

    private AnimationPlayer? GatherAnimOf(int entityId) =>
        entityId == _myId ? _selfAnim
        : _ents.TryGetValue(entityId, out var e) ? e.Anim
        : null;

    private void SetGatherClipLoop(int entityId, bool loop)
    {
        var anim = GatherAnimOf(entityId);
        string name = anim?.CurrentAnimation.ToString() ?? "";
        if (anim == null || name.Length == 0) return;
        var res = anim.GetAnimation(name);
        if (res != null)
            res.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
    }

    private void PlayGatherCast(int entityId)
    {
        PlaySkillAction(entityId, FishingCastClip, FishingCastClips, ActionRankPosture);
        SetGatherClipLoop(entityId, loop: false);
        _fishingCast.Add(entityId);
        if (entityId == _myId) ClearGatherFx();
    }

    private void PlayGatherHold(int entityId, bool fishing)
    {
        _fishingCast.Remove(entityId);
        PlaySkillAction(entityId, fishing ? FishingWaitClip : MiningClip,
                        fishing ? FishingWaitClips : MiningClips, ActionRankPosture);
        SetGatherClipLoop(entityId, loop: true);
        HoldGatherAction(entityId);
        if (fishing && entityId == _myId) SpawnFishingLine();
    }

    private Node3D? GatherToolAttachment()
    {
        int gearSlot = _gatherToolSlot == InventoryConstants.LeftHand ? LeftHandGearSlot : RightHandGearSlot;
        var skel = FindFirst<Skeleton3D>(_self);
        if (skel == null) return null;
        string want = $"{WeaponNodePrefix}{gearSlot}";
        foreach (var child in skel.GetChildren())
            if (child is BoneAttachment3D ba && ba.Name.ToString() == want)
                return ba;
        return null;
    }

    private bool TryGatherToolTip(out Vector3 tip)
    {
        tip = Vector3.Zero;
        var attach = GatherToolAttachment();
        if (attach == null || !IsInstanceValid(attach)) return false;
        var mesh = FindFirst<MeshInstance3D>(attach);
        if (mesh == null || mesh.Mesh == null) return false;

        var aabb = mesh.GetAabb();
        var toWorld = mesh.GlobalTransform;
        Vector3 hand = attach.GlobalPosition;
        float best = -1f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = toWorld * aabb.GetEndpoint(i);
            float d = corner.DistanceSquaredTo(hand);
            if (d > best) { best = d; tip = corner; }
        }
        return best >= 0f;
    }

    private void AnchorFishingLine()
    {
        if (_gatherFx == null || !IsInstanceValid(_gatherFx)) return;
        if (TryGatherToolTip(out var tip)) _gatherFx.GlobalPosition = tip;
    }

    private void SpawnFishingLine()
    {
        if (Fx.NameForId(FishingWaitFx) is not { } waitFx) return;
        ClearGatherFx();
        _gatherFx = Fx.Spawn(waitFx, _self, new Vector3(0, GatherFxHeight, 0));
        if (_gatherFx == null) return;
        _gatherFx.RotationDegrees = new Vector3(0, 180f, 0);
        AnchorFishingLine();
        AudioFxAt(FishingWaitFx, _myId);
    }

    private void HoldGatherAction(int entityId)
    {
        double now = Now();
        if (entityId == _myId) BeginSelfAction(ActionClipCap, ActionRankPosture, now);
        else if (_ents.TryGetValue(entityId, out var e)) BeginEntityAction(e, ActionClipCap, ActionRankPosture, now);
    }

    private void GatherTick(double nowSec)
    {
        if (_gatherers.Count == 0) return;

        foreach (var (entityId, fishing) in _gatherers)
        {
            double until = entityId == _myId ? _selfActionUntil
                : _ents.TryGetValue(entityId, out var e) ? e.ActionUntil
                : 0;

            if (_fishingCast.Contains(entityId))
            {
                if (nowSec >= until) PlayGatherHold(entityId, fishing);
            }
            else if (nowSec >= until - GatherActionRearm)
            {
                HoldGatherAction(entityId);
            }
        }

        if (_gathering && _gatherFishing) AnchorFishingLine();

        if (!_gathering || nowSec < _gatherNextAttempt) return;
        _gatherNextAttempt = nowSec + GatherInterval;
        if (_selfDead) { StopGather(sendStop: true); return; }
        if (_gatherFishing) Net.I.SendFishingAttempt(); else Net.I.SendMiningAttempt();
    }

    private void OnGatherStart(int sub, int code, int charId)
    {
        bool fishing = sub == Net.SubFishingStart;

        if (code != Net.MiningSuccess)
        {
            ReportGatherFailure(sub, code);
            return;
        }

        _gatherers[charId] = fishing;
        if (_ents.TryGetValue(charId, out var gatherer))
        {
            gatherer.Gathering = true;
            gatherer.GatherFishing = fishing;
        }
        if (charId == _myId)
        {
            _gathering = true;
            _gatherFishing = fishing;
            _gatherNextAttempt = Now() + GatherInterval;
        }

        if (fishing) PlayGatherCast(charId);
        else PlayGatherHold(charId, fishing: false);
    }

    private void OnGatherResult(int sub, int code, int charId, int effect)
    {
        if (code == Net.MiningSuccess)
        {
            if (effect > 0 && Fx.NameForId(effect) is { } fxName)
            {
                SpawnFxOn(charId, fxName, GatherFxHeight);
                AudioFxAt(effect, charId);
            }
            if (_gatherers.TryGetValue(charId, out bool wasFishing) && wasFishing)
                PlayGatherCast(charId);
            if (charId == _myId) _gatherNextAttempt = Now() + GatherInterval;
            return;
        }

        if (!_gathering) return;

        if (code is Net.MiningNothing or Net.MiningPreparing)
        {
            _gatherNextAttempt = Now() + GatherInterval;
            return;
        }

        ReportGatherFailure(sub, code);
    }

    private void ReportGatherFailure(int sub, int code)
    {
        bool fishing = sub is Net.SubFishingStart or Net.SubFishingAttempt or Net.SubFishingStop;
        CombatNotice(code switch
        {
            Net.MiningAlready => fishing ? "On fishing." : "You are mining already",
            Net.MiningNotArea => fishing ? "This is not a fishing area." : "Not mining area",
            Net.MiningNoTool => fishing ? "Fishing rod is not equipped." : "A pickaxe is not equipped",
            Net.MiningNoBait => "No Earthworm.",
            _ => fishing ? "Fishing has been failed." : "Mining failed",
        });
        StopGather(sendStop: false);
    }

    private void BeginRemoteGather(int entityId, bool fishing)
    {
        if (entityId == _myId) return;
        _gatherers[entityId] = fishing;
        if (_ents.TryGetValue(entityId, out var e))
        {
            e.Gathering = true;
            e.GatherFishing = fishing;
        }
        if (fishing) PlayGatherCast(entityId);
        else PlayGatherHold(entityId, fishing: false);
    }

    private void EndRemoteGather(int entityId)
    {
        SetGatherClipLoop(entityId, loop: false);
        _gatherers.Remove(entityId);
        _fishingCast.Remove(entityId);
        if (_ents.TryGetValue(entityId, out var e)) { e.Gathering = false; e.ActionUntil = 0; }
    }

    private void OnGatherStop(int sub, int charId, bool personalAck)
    {
        if (personalAck) { StopGather(sendStop: false); return; }
        EndRemoteGather(charId);
    }

    private void OnItemDuration(int position, short dura)
    {
        if (position >= 0 && position < Inv.Length)
        {
            Inv[position] = new ItemSlot { ItemId = Inv[position].ItemId, Count = Inv[position].Count, Durability = dura };
            Net.I.MirrorInventorySlot(position, Inv[position]);
            if (CharTabOpen()) RefreshInventoryUI();
        }

        if (_gathering && position == _gatherToolSlot && dura <= 0)
        {
            CombatNotice(_gatherFishing
                ? "Durability of fishing rod becomes 0."
                : "The durability of pickaxe is 0");
            StopGather(sendStop: true);
        }
    }
}
