using Godot;

namespace LibreKO.Presentation;

internal static class SkillFxRig
{
    private static readonly int[] StrikeHands = { InventoryConstants.VisRightHand, InventoryConstants.VisLeftHand };

    internal static void SpawnHitImpact(Node root, EntityView victim, Node3D? attackerBody, int[]? attackerGear, bool npcAttacker, bool transformed, int strikeAnim)
    {
        var local = StrikeLocalPoint(victim, attackerBody);
        int weaponFx = transformed ? StrikeEffect.None : WeaponStrikeEffect(attackerGear);
        if (weaponFx != StrikeEffect.None)
        {
            if (Fx.NameForId(weaponFx) is { } weaponHit)
                Fx.Spawn(weaponHit, victim.Body, local, oneShot: true, deferParts: true);
            return;
        }
        var (right, left, weight) = WeaponMounts.HandLoadout(attackerGear);
        int blood = StrikeEffect.Blood(npcAttacker, right, strikeAnim, WeaponAnimation.BreathBase(right, left, weight),
            n => (int)(GD.Randi() % (uint)n));
        if (Fx.NameForId(blood) is { } bloodHit)
            Fx.Spawn(bloodHit, root, victim.Body.GlobalTransform * local, oneShot: true, deferParts: true);
    }

    internal static Vector3 StrikeLocalPoint(EntityView victim, Node3D? attackerBody)
    {
        var box = EntityLocalBox(victim);
        if (attackerBody == null || !GodotObject.IsInstanceValid(attackerBody)) return box.GetCenter();
        var toLocal = victim.Body.GlobalTransform.AffineInverse();
        foreach (var child in attackerBody.GetChildren())
        {
            if (child is not WeaponTrail blade || !blade.BladeSegment(out var from, out var to)) continue;
            if (StrikePoint.Entry(box, toLocal * from, toLocal * StrikePoint.Extended(from, to)) is { } entry) return entry;
            break;
        }
        var attacker = toLocal * (attackerBody.GlobalPosition + Vector3.Up * box.GetCenter().Y);
        return StrikePoint.Entry(box, attacker, box.GetCenter()) ?? StrikePoint.TowardAttacker(box, attacker);
    }

    internal static int WeaponStrikeEffect(int[]? gear)
    {
        if (gear == null) return StrikeEffect.None;
        foreach (int slot in StrikeHands)
        {
            int fx = StrikeEffect.FromGlow(ItemData.ExtFor(GearAt(gear, slot))?.GlowFx ?? 0);
            if (fx != StrikeEffect.None) return fx;
        }
        return StrikeEffect.None;
    }

    private static int GearAt(int[] gear, int slot) => slot < gear.Length ? gear[slot] : 0;

    internal static Aabb EntityLocalBox(EntityView e)
    {
        if (e.HitFxBox is { } cached) return cached;
        bool first = true;
        var box = new Aabb();
        var inv = e.Body.GlobalTransform.AffineInverse();
        foreach (var mi in ModelMeshes(e.Body))
        {
            if (mi.Mesh == null || !mi.Visible) continue;
            var b = (inv * mi.GlobalTransform) * mi.GetAabb();
            box = first ? b : box.Merge(b);
            first = false;
        }
        if (first || box.Size.Y < 0.2f)
            box = new Aabb(new Vector3(-e.Radius * 0.7f, 0.2f, -e.Radius * 0.7f),
                           new Vector3(e.Radius * 1.4f, 1.6f, e.Radius * 1.4f));
        e.HitFxBox = box;
        return box;
    }

    private static List<MeshInstance3D> ModelMeshes(Node body)
    {
        var list = new List<MeshInstance3D>();
        Walk(body);
        return list;

        void Walk(Node n)
        {
            if (n is FxInstance or FxWeaponGlow or Node3D { TopLevel: true }) return;
            if (n is MeshInstance3D mi) list.Add(mi);
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }

    internal static float ProjectileSpeedFor(string? fxName) =>
        fxName != null && Fx.AuthoredVelocity(fxName) is > 0f and var authored ? authored : ActionAnimation.ProjectileFxSpeed;
}
