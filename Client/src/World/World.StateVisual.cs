using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private readonly HashSet<int> _stealthedIds = new();
    private readonly HashSet<int> _sittingIds = new();
    private readonly HashSet<int> _stanceIds = new();
    private readonly Dictionary<int, Node3D> _sleepFx = new();
    private bool _selfSitting;
    private bool _selfSitVisual;
    private bool _sitRequestPending;

    private void StateVisualForgetEntity(int id)
    {
        _stealthedIds.Remove(id);
        _sittingIds.Remove(id);
        _stanceIds.Remove(id);
        ApplySleepState(id, false);
    }

    private const float StealthAlpha = 0.18f;
    private const float SitDrop = 0.42f;
    private const float SitTiltDeg = -14f;
    private const int SleepFxId = 13042;
    private const float SleepFxLift = 0.4f;

    private void StateVisualInit()
    {
        Net.I.StateChangeEvent += OnStateChange;
    }

    private void StateVisualDispose()
    {
        Net.I.StateChangeEvent -= OnStateChange;
    }

    private void OnStateChange(int charId, int type, int value)
    {
        switch (type)
        {
            case StateChange.Pose:
                if (value is UserPose.Asleep or UserPose.Awake)
                    ApplySleepState(charId, value == UserPose.Asleep);
                else
                    ApplySitState(charId, value == UserPose.Sitting);
                break;
            case StateChange.Abnormal: ApplyTransformState(charId, value); break;
            case StateChange.Transformation: ApplyTransformState(charId, value); break;
            case StateChange.Stealth: ApplyStealthState(charId, value != 0); break;
            case StateChange.CombatStance: ApplyCombatStanceState(charId, value != StateChange.StanceRelaxed); break;
        }
    }

    private void ApplySleepState(int charId, bool asleep)
    {
        if (_sleepFx.Remove(charId, out var running) && IsInstanceValid(running))
            running.QueueFree();

        if (!asleep
            || !_ents.TryGetValue(charId, out var e)
            || !IsInstanceValid(e.Body)
            || Fx.NameForId(SleepFxId) is not { } fxName)
            return;

        var fx = Fx.Spawn(fxName, e.Body, new Vector3(0, HeadHeightOf(charId) + SleepFxLift, 0));
        if (fx != null)
            _sleepFx[charId] = fx;
    }

    private void ApplyCombatStanceState(int charId, bool ready)
    {
        if (charId == _myId) return;
        if (ready ? !_stanceIds.Add(charId) : !_stanceIds.Remove(charId)) return;
        if (!_ents.TryGetValue(charId, out var e) || e.IsNpc) return;
        ApplyEntityCombatStance(e, ready);
    }

    private static void ApplyEntityCombatStance(Ent e, bool ready)
    {
        if (e.CombatStance == ready) return;
        e.CombatStance = ready;
        e.Clip = null;
    }

    private void ToggleSitting()
    {
        if (_selfDead || _sitRequestPending) return;
        _sitRequestPending = true;
        Net.I.SendSitting(!_selfSitting);
    }

    private void ApplySitState(int charId, bool sitting)
    {
        bool self = charId == _myId;

        if (sitting ? !_sittingIds.Add(charId) : !_sittingIds.Remove(charId))
        {
            return;
        }

        if (self)
        {
            _sitRequestPending = false;
            _selfSitting = sitting;
            if (sitting)
            {
                _hasMoveTarget = false;
                _terrainMoveHeld = false;
                _autoMoveForward = false;
                StopAutoAttack();
            }
            ApplySelfSitVisual(sitting);
        }
        else if (_ents.TryGetValue(charId, out var ent) && GodotObject.IsInstanceValid(ent.Body))
        {
            ApplyEntitySitVisual(ent, sitting);
        }

        if (self)
            CombatLogAdd(
                sitting ? "You sit down." : "You stand up.",
                CombatLogKind.Status);
    }

    private void ApplyEntitySitVisual(Ent e, bool sitting)
    {
        if (e.Sitting == sitting) return;
        e.Sitting = sitting;
        e.Clip = null;

        if (e.Anim != null && Pick(e.Anim, sitting ? SitDownClips : StandUpClips) != null)
        {
            PlayEntityAction(e, sitting ? SitDownClips : StandUpClips, ActionRankPosture);
            return;
        }

        var pos = e.Body.Position;
        pos.Y += sitting ? -SitDrop : SitDrop;
        e.Body.Position = pos;
    }

    private void ApplySelfSitVisual(bool sitting)
    {
        if (!GodotObject.IsInstanceValid(_selfVisual)) return;
        if (_selfSitVisual == sitting) return;
        _selfSitVisual = sitting;

        _selfVisual.Transform = _selfStandingVisualTransform;
        if (!sitting)
        {
            if (_selfAnim != null)
            {
                double len = PlayActionOn(_selfAnim, StandUpClips);
                if (len > 0)
                {
                    BeginSelfAction(len, ActionRankPosture, Now());
                }
            }
            return;
        }

        if (_selfAnim != null && Pick(_selfAnim, SitDownClips) != null)
        {
            double len = PlayActionOn(_selfAnim, SitDownClips);
            BeginSelfAction(len, ActionRankPosture, Now());
            return;
        }

        var seated = _selfStandingVisualTransform;
        seated.Origin += Vector3.Down * SitDrop;
        seated.Basis = seated.Basis.Rotated(Vector3.Right, Mathf.DegToRad(SitTiltDeg));
        _selfVisual.Transform = seated;
    }

    private void StandUp()
    {
        _selfSitting = false;
        _selfSitVisual = false;
        _sitRequestPending = true;
        if (GodotObject.IsInstanceValid(_selfVisual))
            _selfVisual.Transform = _selfStandingVisualTransform;
        _selfActionUntil = 0;
        _selfClip = null;
        Net.I.SendSitting(false);
    }

    private void ApplyTransformState(int charId, int skillId)
    {
        bool ending = skillId <= 0;

        if (ending)
        {
            RestoreOwnLook(charId);
            return;
        }

        WearMonsterLook(charId, skillId);
    }

    private const string TransformNodeName = "TransformModel";

    private Node3D? TransformHost(int charId) =>
        charId == _myId ? _selfBody : _ents.TryGetValue(charId, out var e) ? e.Body : null;

    private void RearmWornLook(Node3D host, int[]? gear)
    {
        if (host.GetNodeOrNull<Node3D>(TransformNodeName) is { } worn)
            AttachWeapons(worn, gear);
    }

    private void WearMonsterLook(int charId, int skillId)
    {
        var host = TransformHost(charId);
        if (host == null) return;

        var skill = SkillData.Get(skillId);
        if (skill == null || skill.TransformModelId <= 0) return;

        var scene = ResolveMobScene(skill.TransformModelId);
        if (scene == null)
        {
            GD.Print($"[transform] no baked model for {skill.TransformModelId} (skill {skillId})");
            return;
        }

        RestoreOwnLook(charId);

        var (monster, anim) = MakeAnimatedEntity(scene, "", skill.TransformScale);
        monster.Name = TransformNodeName;
        host.AddChild(monster);

        if (charId == _myId)
        {
            _selfRigAnim ??= _selfAnim;
            _selfAnim = anim;
            _selfClip = null;
            _selfTransformSkill = skillId;
            AttachWeapons(monster, SelfGear());
        }
        else if (_ents.TryGetValue(charId, out var e))
        {
            e.RigAnim ??= e.Anim;
            e.Anim = anim;
            e.Clip = null;
            AttachWeapons(monster, e.Gear);
        }

        SetOwnLookVisible(host, false);
    }

    private void RestoreOwnLook(int charId)
    {
        var host = TransformHost(charId);
        if (host == null) return;

        if (host.GetNodeOrNull<Node3D>(TransformNodeName) is not { } worn)
            return;

        worn.QueueFree();

        if (charId == _myId)
        {
            if (_selfRigAnim != null) { _selfAnim = _selfRigAnim; _selfRigAnim = null; }
            _selfClip = null;
            DropBuffChip(_selfTransformSkill);
            _selfTransformSkill = 0;
        }
        else if (_ents.TryGetValue(charId, out var e))
        {
            if (e.RigAnim != null) { e.Anim = e.RigAnim; e.RigAnim = null; }
            e.Clip = null;
        }

        SetOwnLookVisible(host, true);
    }

    private AnimationPlayer? _selfRigAnim;
    private int _selfTransformSkill;

    private static void SetOwnLookVisible(Node3D host, bool visible)
    {
        foreach (var child in host.GetChildren())
        {
            if (child is not Node3D node || node.Name == TransformNodeName) continue;
            if (node.FindChild(ModelNodeName, true, false) is Node3D model)
                model.Visible = visible;
            else if (node.Name == ModelNodeName)
                node.Visible = visible;
        }
    }



    private void ApplyStealthState(int charId, bool stealth)
    {
        if (stealth ? !_stealthedIds.Add(charId) : !_stealthedIds.Remove(charId))
            return;

        if (charId == _myId)
            Chat.Info(stealth ? "You vanish into stealth." : "You return to sight.");

        if (_ents.TryGetValue(charId, out var ent) && GodotObject.IsInstanceValid(ent.Body))
            SetBodyAlpha(ent.Body, stealth ? StealthAlpha : 1f);
    }

    private static void SetBodyAlpha(Node3D body, float alpha)
    {
        foreach (var node in Descendants(body))
        {
            if (node is not GeometryInstance3D gi) continue;
            gi.Transparency = Mathf.Clamp(1f - alpha, 0f, 1f);
        }
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var d in Descendants(child))
                yield return d;
        }
    }
}
