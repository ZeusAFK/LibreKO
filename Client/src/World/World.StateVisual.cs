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
            case StateChange.Stealth: ApplyStealthState(charId, value); break;
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
        if (sitting) e.Hover = ResolveHoverClips(e.Body, e.Anim);

        if (e.NameTag != null && GodotObject.IsInstanceValid(e.NameTag))
        {
            var tagPos = e.NameTag.Position;
            tagPos.Y = sitting && e.Hover == null
                ? SittingNameTagHeight
                : (e.OriginalTagY > 0 ? e.OriginalTagY : StandingNameTagHeight);
            e.NameTag.Position = tagPos;
        }

        var posture = sitting ? e.Hover?.Start ?? SitDownClips : e.Hover?.End ?? StandUpClips;
        if (e.Anim != null && Pick(e.Anim, posture) != null)
        {
            PlayEntityAction(e, posture, ActionRankPosture);
            return;
        }

        var pos = e.Body.Position;
        pos.Y += sitting ? -SitDrop : SitDrop;
        e.Body.Position = pos;
    }

    private void ApplySelfSitVisual(bool sitting)
    {
        if (_selfNameTag == null || !GodotObject.IsInstanceValid(_selfNameTag))
        {
            _selfNameTagFound = true;
            _selfNameTag = _self != null ? FindFirst<Label3D>(_self) : null;
        }

        if (sitting && _selfSitVisual != sitting) _selfHover = ResolveHoverClips(_self, _selfAnim);

        if (_selfNameTag != null && GodotObject.IsInstanceValid(_selfNameTag))
        {
            var tagPos = _selfNameTag.Position;
            tagPos.Y = sitting && _selfHover == null ? SittingNameTagHeight : StandingSelfNameTagHeight;
            _selfNameTag.Position = tagPos;
        }

        if (!GodotObject.IsInstanceValid(_selfVisual)) return;
        if (_selfSitVisual == sitting) return;
        _selfSitVisual = sitting;

        _selfVisual.Transform = _selfStandingVisualTransform;
        if (!sitting)
        {
            if (_selfAnim != null)
            {
                double len = PlayActionOn(_selfAnim, _selfHover?.End ?? StandUpClips);
                if (len > 0)
                {
                    BeginSelfAction(len, ActionRankPosture, Now());
                }
            }
            return;
        }

        var sitDown = _selfHover?.Start ?? SitDownClips;
        if (_selfAnim != null && Pick(_selfAnim, sitDown) != null)
        {
            double len = PlayActionOn(_selfAnim, sitDown);
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
    private const string WornAccessoriesMeta = "worn_accessories";

    private Node3D? TransformHost(int charId) =>
        charId == _myId ? _selfBody : _ents.TryGetValue(charId, out var e) ? e.Body : null;

    private void RearmWornLook(Node3D host, int[]? gear)
    {
        if (host.GetNodeOrNull<Node3D>(TransformNodeName) is { } worn)
            AttachWeapons(worn, gear);
    }

    private static Node3D? WornLookOf(Node3D body)
    {
        if (body.Name == TransformNodeName) return body;
        if (body.GetNodeOrNull<Node3D>(TransformNodeName) is { } worn) return worn;
        return body.GetParent() is CharacterBody3D host ? host.GetNodeOrNull<Node3D>(TransformNodeName) : null;
    }

    private static (Node3D? Body, int Race) AccessoryHostFor(Node3D body, int race)
    {
        if (WornLookOf(body) is not { } worn) return (body, race);
        return worn.GetMeta(WornAccessoriesMeta, false).AsBool() ? (worn, PlayerRig.StandardRace(race)) : (null, race);
    }

    private bool WearsAccessories(SkillData.Skill skill, Node3D ownBody, int race, Node3D worn) =>
        TransformationUse.WearsAccessories(skill.TransformUse)
        && FindFirst<Skeleton3D>(worn) is { } wornRig
        && PlayerRig.SameBones(StandardRigBones(ownBody, race), BoneNames(wornRig));

    private string[]? _standardRigBones;

    private string[] StandardRigBones(Node3D ownBody, int race)
    {
        if (!PlayerRig.IsKurian(race))
            return FindFirst<Skeleton3D>(ownBody) is { } own ? BoneNames(own) : System.Array.Empty<string>();
        if (_standardRigBones != null) return _standardRigBones;
        var names = System.Array.Empty<string>();
        if (ResolvePlayerScene(PlayerRig.StandardRace(race)) is { } scene)
        {
            var rig = scene.Instantiate<Node3D>();
            if (FindFirst<Skeleton3D>(rig) is { } skel) names = BoneNames(skel);
            rig.Free();
        }
        return _standardRigBones = names;
    }

    private static string[] BoneNames(Skeleton3D skel)
    {
        var names = new string[skel.GetBoneCount()];
        for (int i = 0; i < names.Length; i++) names[i] = skel.GetBoneName(i);
        return names;
    }

    private AnimationPlayer?[] DressAccessories(Node3D body, int[]? gear, int race, bool shineShadow = false)
    {
        var (look, lookRace) = AccessoryHostFor(body, race);
        var anims = AttachWings(body, look == body ? gear : null, race, _zone, shineShadow: shineShadow);
        AttachHandFx(body, look == body ? gear : null, race, _zone);
        if (look == null || look == body) return anims;
        AttachHandFx(look, gear, lookRace, _zone);
        return AttachWings(look, gear, lookRace, _zone, shineShadow: shineShadow);
    }

    private static void DressClanGauntlet(Node3D body, int race, int clanGrade, int clanRanking)
    {
        var (look, lookRace) = AccessoryHostFor(body, race);
        if (look != body) AttachClanGauntlet(body, race, 0, 0);
        if (look != null) AttachClanGauntlet(look, lookRace, clanGrade, clanRanking);
    }

    private void DressSelfLook()
    {
        RerenderSelfEquipment();
        DressSelfCape();
        DressClanGauntlet(_self, _selfRace, MyClan.InClan ? MyClan.Grade : 0, MyClan.InClan ? MyClan.Ranking : 0);
    }

    private void DressEntityLook(Ent e)
    {
        RedressEntity(e);
        DressCape(e.Body, e.CapeId, e.CapeR, e.CapeG, e.CapeB, e.IsGm, e.Race);
        DressClanGauntlet(e.Body, e.Race, e.ClanGrade, e.ClanRanking);
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

        int race = charId == _myId ? _selfRace : _ents.TryGetValue(charId, out var wearer) ? wearer.Race : 0;
        var (monster, anim) = MakeAnimatedEntity(scene, "", skill.TransformScale);
        monster.Name = TransformNodeName;
        monster.SetMeta(WornAccessoriesMeta, WearsAccessories(skill, host, race, monster));
        host.AddChild(monster);

        if (charId == _myId)
        {
            _selfRigAnim ??= _selfAnim;
            _selfAnim = anim;
            _selfClip = null;
            _selfTransformSkill = skillId;
            DressSelfLook();
        }
        else if (_ents.TryGetValue(charId, out var e))
        {
            e.RigAnim ??= e.Anim;
            e.Anim = anim;
            e.Clip = null;
            DressEntityLook(e);
        }

        SetOwnLookVisible(host, false);
    }

    private void RestoreOwnLook(int charId)
    {
        var host = TransformHost(charId);
        if (host == null) return;

        if (host.GetNodeOrNull<Node3D>(TransformNodeName) is not { } worn)
            return;

        host.RemoveChild(worn);
        worn.QueueFree();
        SetOwnLookVisible(host, true);

        if (charId == _myId)
        {
            if (_selfRigAnim != null) { _selfAnim = _selfRigAnim; _selfRigAnim = null; }
            _selfClip = null;
            DropBuffChip(_selfTransformSkill);
            _selfTransformSkill = 0;
            DressSelfLook();
        }
        else if (_ents.TryGetValue(charId, out var e))
        {
            if (e.RigAnim != null) { e.Anim = e.RigAnim; e.RigAnim = null; }
            e.Clip = null;
            DressEntityLook(e);
        }
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



    private void ApplyStealthState(int charId, int value)
    {
        bool stealth = value != 0;
        if (stealth ? !_stealthedIds.Add(charId) : !_stealthedIds.Remove(charId))
            return;
        if (value == Net.InvisibilityInfiltration)
            return;

        if (charId == _myId)
            CombatNotice(stealth ? "You vanish into stealth." : "You return to sight.");

        if (_ents.TryGetValue(charId, out var ent) && GodotObject.IsInstanceValid(ent.Body))
            SetBodyAlpha(ent.Body, stealth ? StealthAlpha : 1f);
    }

    private static void SetBodyAlpha(Node3D body, float alpha)
    {
        foreach (var node in Descendants(body))
        {
            if (node is not GeometryInstance3D gi) continue;
            Fx.SetTransparency(gi, Mathf.Clamp(1f - alpha, 0f, 1f));
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
