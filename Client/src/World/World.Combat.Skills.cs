using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private void CastSkill(int id)
    {
        if (_selfDead) return;
        var s = SkillData.Get(id);
        if (s == null) return;

        if (!SkillRequirementMet(s))
        {
            CombatNotice($"You have not learned {s.Name}. {SkillRequirementText(s)}.");
            return;
        }

        double now = Now();
        if (SkillOnCooldown(s, now)) return;
        if (GroupCooldownBlocker(s, now) is { } blocker)
        {
            CombatNotice(GroupCooldownNotice(blocker, now));
            return;
        }
        if (!s.IsNonAction && SelfCasting(now)) return;
        if (HasPendingCast(id)) return;
        if (!Vitals.HasMana(s.Msp))
        {
            CombatNotice("Not enough mana.");
            return;
        }
        if (!CanCastWithGear(s)) return;

        if (s.IsGroundArea && BeginAreaCast(s)) return;
        if (s.IsCasterArea) { SendAreaCast(s, _self.GlobalPosition); return; }

        int target;
        if (s.IsEnemy)
        {
            bool selectedInRange =
                _selectedId >= 0
                && _ents.TryGetValue(_selectedId, out var te)
                && te.Attackable
                && !te.Dead
                && InSkillRange(_selectedId, s);
            target = selectedInRange ? _selectedId : -1;
            if (target < 0) return;
            FaceToward(_myId, target);
        }
        else if (s.IsDeadFriend)
        {
            target = ResurrectTarget(s);
            if (target < 0) return;
            FaceToward(_myId, target);
        }
        else
        {
            target = FriendlyCastTarget(s);
            if (target != _myId) FaceToward(_myId, target);
        }

        Net.I.SendMagic(1, id, target);
        QueuePendingStage(id, target, s.HasFlyingStage ? PendingFlying : PendingEffecting, CastDelay(s));
        BeginLocalCast(s);
    }

    private int FriendlyCastTarget(SkillData.Skill s)
    {
        if (!s.IsFriendly || _selectedId < 0 || _selectedId == _myId) return _myId;
        if (!_ents.TryGetValue(_selectedId, out var e)) return _myId;
        if (e.IsNpc || e.Dead || e.Attackable) return _myId;
        return InSkillRange(_selectedId, s) ? _selectedId : _myId;
    }

    private int ResurrectTarget(SkillData.Skill s)
    {
        if (_selectedId < 0 || _selectedId == _myId) return -1;
        if (!_ents.TryGetValue(_selectedId, out var e)) return -1;
        if (e.IsNpc || !e.Dead || e.Attackable) return -1;
        return InSkillRange(_selectedId, s) ? _selectedId : -1;
    }

    private bool CanCastWithGear(SkillData.Skill s)
    {
        var gear = SelfGear();
        switch (WeaponAnimation.CheckGear(s.ItemGroup, HeldItemClass(gear, 6), HeldItemClass(gear, 7)))
        {
            case WeaponAnimation.GearCheck.NoWeapon:
                return false;
            case WeaponAnimation.GearCheck.WrongWeapon:
                return false;
        }
        if (s.UseItem != 0 && !s.IsResurrect && ItemData.Get(s.UseItem) != null)
        {
            if (CountInBackpack(s.UseItem) < 1)
                return false;
            int need = s.IsRanged ? Mathf.Max(1, s.NeedArrow) : 1;
            if (CountInBackpack(s.ConsumedItem) < need)
                return false;
        }
        return true;
    }

    private int CountInBackpack(int itemId)
    {
        int n = 0;
        for (int abs = GridStart; abs < Inv.Length; abs++)
            if (Inv[abs].ItemId == itemId) n += Mathf.Max((int)Inv[abs].Count, 1);
        return n;
    }

    private bool InSkillRange(int id, SkillData.Skill s)
    {
        if (!_ents.TryGetValue(id, out var e)) return false;
        float reach = SkillCastRange(s) + e.Radius + 1f;
        return e.Body.GlobalPosition.DistanceTo(_self.GlobalPosition) <= reach;
    }

    private float SkillCastRange(SkillData.Skill s)
    {
        if (s.Range > 0) return s.Range;
        if (s.IsMelee) return Mathf.Max(MeleeReach, MeleeWeaponReach());
        if (s.IsRanged && RangedWeaponItem() is var bow && bow != 0)
        {
            float itemRange = (ItemData.Get(bow)?.Range ?? 0) / 10f;
            float scale = s.AddRange > 0 ? s.AddRange / 100f : 1f;
            if (itemRange > 0) return Mathf.Max(DefaultSkillRange, itemRange * scale);
        }
        return DefaultSkillRange;
    }

    private static double CastDelay(SkillData.Skill s) => Mathf.Max(0f, s.CastSeconds);

    private void QueuePendingStage(int skillId, int targetId, int stage, double delay, short[]? data = null, bool replace = true)
    {
        double when = Now() + delay;
        var pending = new PendingCast
        { EffectTime = when, SkillId = skillId, TargetId = targetId, Stage = stage, Data = data };
        for (int i = 0; replace && i < _pendingCasts.Count; i++)
        {
            if (_pendingCasts[i].SkillId != skillId) continue;
            _pendingCasts[i] = pending;
            return;
        }
        _pendingCasts.Add(pending);
    }

    private bool ResolvePendingReply(int skillId)
    {
        int fallback = -1;
        for (int i = 0; i < _pendingCasts.Count; i++)
        {
            if (_pendingCasts[i].SkillId != skillId) continue;
            if (_pendingCasts[i].Stage == PendingAwaitServer)
            {
                _pendingCasts.RemoveAt(i);
                return true;
            }
            if (fallback < 0) fallback = i;
        }
        if (fallback < 0) return false;
        _pendingCasts.RemoveAt(fallback);
        return true;
    }

    private bool HasPendingCast(int skillId)
    {
        foreach (var pc in _pendingCasts)
            if (pc.SkillId == skillId)
                return true;
        return false;
    }

    private bool PendingCastPreEffect(int skillId)
    {
        foreach (var pc in _pendingCasts)
            if (pc.SkillId == skillId)
                return pc.Stage != PendingAwaitServer;
        return false;
    }

    private void ClearPendingCast(int skillId)
    {
        for (int i = _pendingCasts.Count - 1; i >= 0; i--)
            if (_pendingCasts[i].SkillId == skillId)
                _pendingCasts.RemoveAt(i);
    }

    private void BeginLocalCast(SkillData.Skill s)
    {
        StartSkillCooldown(s);
        if (s.IsPotion) AudioFxAt(s.TargetFxId, _myId);
        BeginCast(s);
        if (s.IsNonAction) return;
        PlaySkillAction(_myId, SkillAnim(_myId, s, false), ClipsForCast(s), ActionRankSkill);
        StretchCastAnimation(s);
    }

    private bool SelfCasting(double now) =>
        IsRootedByCast() || (_selfActionRank == ActionRankSkill && now < _selfActionUntil);

    private const double PotionCooldownSeconds = 2.0;

    private void StartSkillCooldown(SkillData.Skill s)
    {
        if (s.IsPotion)
        {
            Net.I.PotionReadyAt = Now() + PotionCooldownSeconds;
            return;
        }
        if (s.Recast <= 0) return;
        _skillReady[s.Id] = Now() + s.RecastSeconds;
    }

    private void EnsureSkillCooldown(SkillData.Skill s)
    {
        if (SkillOnCooldown(s, Now())) return;
        StartSkillCooldown(s);
    }

    private void CancelSkillCooldown(SkillData.Skill s)
    {
        if (!s.IsPotion) _skillReady.Remove(s.Id);
    }

    private bool SkillOnCooldown(SkillData.Skill s, double now) =>
        s.IsPotion
            ? now < Net.I.PotionReadyAt
            : s.Recast > 0 && _skillReady.TryGetValue(s.Id, out double until) && now < until;

    private SkillData.Skill? GroupCooldownBlocker(SkillData.Skill s, double now)
    {
        if (s.CooldownGroup == 0) return null;
        foreach (var (id, until) in _skillReady)
        {
            if (id == s.Id || now >= until) continue;
            var other = SkillData.Get(id);
            if (other != null && other.CooldownGroup == s.CooldownGroup) return other;
        }
        return null;
    }

    private string GroupCooldownNotice(SkillData.Skill blocker, double now) =>
        $"{blocker.Name} is available in {Mathf.CeilToInt((float)(_skillReady[blocker.Id] - now))} seconds";

    private bool SkillReady(SkillData.Skill s, double now) =>
        !HasPendingCast(s.Id)
        && !SkillOnCooldown(s, now)
        && GroupCooldownBlocker(s, now) == null
        && Vitals.HasMana(s.Msp);

    private float SkillCooldown(SkillData.Skill s, double now)
    {
        if (s.IsPotion)
            return Mathf.Clamp((float)((Net.I.PotionReadyAt - now) / PotionCooldownSeconds), 0f, 1f);
        if (s.Recast <= 0 || !_skillReady.TryGetValue(s.Id, out double until)) return 0f;
        double left = until - now;
        if (left <= 0) return 0f;
        return Mathf.Clamp((float)(left / s.RecastSeconds), 0f, 1f);
    }

    private static string SkillRequirementText(SkillData.Skill s)
    {
        int mastery = SkillData.MasteryType(s.Tree);
        return mastery > 0
            ? $"Requires {SkillData.PageName(SkillData.ClassPrefix(s.Id), mastery)} mastery {s.Level}"
            : $"Requires level {s.Level}";
    }

    private static string SkillTooltip(SkillData.Skill s)
    {
        var lines = new List<string> { s.Name };
        if (!string.IsNullOrWhiteSpace(s.Desc))
            lines.Add(s.Desc);

        if (s.Level > 0)
            lines.Add(SkillRequirementText(s));

        var cost = new List<string>();
        if (s.Msp > 0) cost.Add($"MP {s.Msp}");
        if (s.Hp > 0) cost.Add($"HP {s.Hp}");
        if (s.Sp > 0) cost.Add($"SP {s.Sp}");
        if (s.NeedArrow > 0) cost.Add($"Arrows {s.NeedArrow}");
        if (cost.Count > 0) lines.Add(string.Join("  ", cost));

        if (s.IsResurrect && s.NeedStone > 0 && s.UseItem != 0)
            lines.Add($"Costs the target {s.NeedStone} × {ItemData.DisplayName(s.UseItem)}");
        if (s.IsMasterScrollSkill)
            lines.Add($"Consumes 1 × {ItemData.DisplayName(s.ConsumedItem)}");

        var timing = new List<string>();
        if (s.Cast > 0) timing.Add($"Cast {s.CastSeconds:0.#}s");
        if (s.Recast > 0) timing.Add($"Cooldown {s.RecastSeconds:0.#}s");
        if (s.Range > 0) timing.Add($"Range {s.Range}");
        if (timing.Count > 0) lines.Add(string.Join("  ", timing));

        if (s.Hit > 0) lines.Add($"Damage {s.Hit}%");
        else if (s.Type1 == MagicType.Ranged && s.AddDamage > 0) lines.Add($"Damage {s.AddDamage}%");
        else if (s.AddDamage is > 0 and not 100) lines.Add($"Add damage {s.AddDamage}");
        if (s.FirstDamage != 0) lines.Add($"{(s.FirstDamage < 0 ? "Damage" : "Heal")} {System.Math.Abs(s.FirstDamage)}");
        if (s.TimeDamage != 0 || s.Duration > 0)
            lines.Add($"Over time {System.Math.Abs(s.TimeDamage)} / {s.Duration}s");
        if (s.Radius > 0) lines.Add($"Radius {s.Radius}");
        if (s.Angle > 0) lines.Add($"Angle {s.Angle}");
        if (s.Attribute > 0) lines.Add($"Attribute {s.Attribute}");
        return string.Join("\n", lines);
    }

    private SkillData.Skill? BasicRangedAttackSkill()
    {
        if (RangedWeaponItem() == 0) return null;
        int id = BasicRangedSkillId();
        return id != 0 ? SkillData.Get(id) : null;
    }

    private int BasicRangedSkillId()
    {
        if (CharacterClassCatalog.Family(_selfClass) != 2) return 0;
        int id = _selfClass * 1000 + 3;
        return SkillData.Get(id) != null ? id : 0;
    }

    private int RangedWeaponItem()
    {
        var gear = SelfGear();
        int right = gear.Length > 6 ? gear[6] : 0, left = gear.Length > 7 ? gear[7] : 0;
        if (ItemData.Get(left)?.Kind is WeaponAnimation.Bow or WeaponAnimation.LongBow) return left;
        if (ItemData.Get(right)?.Kind is WeaponAnimation.Crossbow) return right;
        return 0;
    }
}
