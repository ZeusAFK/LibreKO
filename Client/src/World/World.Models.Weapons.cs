using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string WeaponNodePrefix = "weapon_";
    private const int ExtRowSpan = 1000;

    private void AttachWeapons(Node3D body, int[]? gear, int npcType = 0, int npcId = 0)
    {
        if (gear == null) return;
        if (npcType == NpcTypes.FixedPose || NoWeaponNpcIds.Contains(npcId)) return;
        var skel = FindFirst<Skeleton3D>(body);
        if (skel == null) return;
        var weapons = WeaponCatalog.Index;
        if (weapons.Count == 0) return;
        foreach (var old in skel.GetChildren())
            if (old is BoneAttachment3D ba
                && ba.Name.ToString().StartsWith(WeaponNodePrefix, System.StringComparison.Ordinal))
            { skel.RemoveChild(ba); ba.QueueFree(); }
        foreach (var old in body.GetChildren())
            if (old is WeaponTrail wt) { body.RemoveChild(wt); wt.QueueFree(); }

        foreach (int slot in new[] { 6, 7 })
        {
            if (slot >= gear.Length || gear[slot] <= 0) continue;
            int baseItemId = WeaponCatalog.ResolveBaseId(gear[slot]);
            if (!weapons.TryGetValue(baseItemId, out var w)) continue;
            string resPath = $"res://assets/items/weapon/{w.Stem}.glb";
            if (!ResourceLoader.Exists(resPath) || ResourceLoader.Load(resPath) is not PackedScene scene)
                continue;
            int bone = WeaponMount.Bone(skel, right: slot == 6, gear[slot]);
            if (bone < 0) continue;
            var attach = new BoneAttachment3D { Name = $"{WeaponNodePrefix}{slot}" };
            skel.AddChild(attach);
            attach.BoneIdx = bone;
            var mesh = scene.Instantiate<Node3D>();
            mesh.Transform = new Transform3D(new Basis(w.Quat).Scaled(w.Scale), w.Pos);
            ForceDoubleSided(mesh);
            attach.AddChild(mesh);
            uint? glowTrace = WeaponGlow.Attach(mesh, gear[slot]);
            ItemShine.Apply(mesh, gear[slot], slot);
            if (slot == 6 && w.TraceSteps > 0)
                WeaponTrail.Create(body, BodyAnim(body), skel, bone, mesh.Transform, w.Trace0, w.Trace1,
                                   glowTrace ?? w.TraceColor, TrailElement(gear[slot]));
        }
    }

    internal static int TrailElement(int itemId)
    {
        if (itemId <= 0 || ItemData.Get(itemId) is not { } def) return WeaponTrailRule.Normal;
        int extId = ItemData.ExtIdFor(itemId);
        return extId % ExtRowSpan != 0 && ItemData.ExtRow(def.Cat, extId) is { } ext
            ? WeaponTrailRule.Element(ext.FireDamage, ext.IceDamage, ext.LightningDamage, ext.PoisonDamage)
            : WeaponTrailRule.Normal;
    }

    private static string? WeaponElement(int itemId)
    {
        if (itemId <= 0 || ItemData.ExtFor(itemId) is not { } ext) return null;
        bool Eligible(int damage) => damage >= 64 || (ext.MagicOrRare == 4 && damage > 0);
        if (Eligible(ext.FireDamage)) return "fire";
        if (Eligible(ext.IceDamage)) return "ice";
        if (Eligible(ext.PoisonDamage)) return "poison";
        if (Eligible(ext.LightningDamage)) return "lighting";
        return null;
    }
}
