using Godot;
using LibreKO.Domain;

namespace LibreKO;

internal static class WeaponGlow
{
    private const float BladeAlong = 0.62f;
    private const int AabbCorners = 8;

    internal static uint? Attach(Node3D weaponMesh, int itemId)
    {
        if (!WeaponCatalog.TryResolveGlow(itemId, out int baseItemId, out var fxName, out var tailFx)) return null;
        var def = ItemData.Get(baseItemId);
        var ext = def != null ? ItemData.ExtRow(def.Cat, itemId - baseItemId) : null;
        if (def != null && ext != null)
        {
            int variant = WeaponGlowRule.VariantFx(ext.GlowFx, def.Kind, def.Effect2, ext.Linked,
                ext.FireDamage, ext.IceDamage, ext.LightningDamage);
            if (variant != WeaponGlowRule.NoFx)
            {
                fxName = LoadableFx(variant);
                tailFx = LoadableFx(variant + 1);
            }
        }
        if (fxName.Length == 0 && tailFx.Length == 0) return null;

        var mi = FirstMesh(weaponMesh);
        var index = WeaponCatalog.Index;
        Vector3 bladeAt;
        if (index.TryGetValue(baseItemId, out var wi) && wi.FxPos is { } fxp)
            bladeAt = mi != null ? mi.Transform * fxp : fxp;
        else if (mi?.Mesh != null)
        {
            var ab = mi.Mesh.GetAabb();
            Vector3 tip = mi.Transform * ab.GetCenter();
            float best = -1f;
            for (int c = 0; c < AabbCorners; c++)
            {
                Vector3 corner = mi.Transform * (ab.Position + ab.Size * new Vector3(c & 1, (c >> 1) & 1, (c >> 2) & 1));
                float d = corner.LengthSquared();
                if (d > best) { best = d; tip = corner; }
            }
            bladeAt = (mi.Transform * ab.GetCenter()).Lerp(tip, BladeAlong);
        }
        else bladeAt = Vector3.Zero;

        if (fxName.Length > 0
            && index.TryGetValue(baseItemId, out var info)
            && info.FxGuide.Length > 0
            && FxWeaponGlow.Create(fxName, tailFx, info.FxGuide) is { } guideGlow)
        {
            weaponMesh.AddChild(guideGlow);
            if (mi != null) guideGlow.Transform = mi.Transform;
        }
        else
        {
            if (fxName.Length > 0) Fx.Spawn(fxName, weaponMesh, bladeAt);
            Aabb bounds = mi?.Mesh != null ? mi.Mesh.GetAabb() : new Aabb(bladeAt, Vector3.Zero);
            if (FxWeaponGlow.CreateTailOnly(tailFx, bounds) is { } scatter)
            {
                weaponMesh.AddChild(scatter);
                if (mi != null) scatter.Transform = mi.Transform;
            }
            else if (tailFx.Length > 0) Fx.Spawn(tailFx, weaponMesh, bladeAt);
        }
        if (fxName.Length == 0) return null;
        return ext == null ? WeaponGlowRule.White
            : WeaponGlowRule.Tint(ext.MagicOrRare, ext.FireDamage, ext.IceDamage, ext.LightningDamage, ext.PoisonDamage);
    }

    private static string LoadableFx(int fxId) =>
        Fx.NameForId(fxId) is { } name && Fx.Has(name) ? name : "";

    private static MeshInstance3D? FirstMesh(Node node)
    {
        if (node is MeshInstance3D mesh) return mesh;
        foreach (var child in node.GetChildren())
            if (FirstMesh(child) is { } found) return found;
        return null;
    }
}
