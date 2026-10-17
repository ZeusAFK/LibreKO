using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string WeaponNodePrefix = "weapon_";
    private const int ExtRowSpan = 1000;
    private const float SizePercent = 100f;

    private void AttachWeapons(Node3D body, int[]? gear, HandWearer wearer, int npcType = 0)
    {
        if (gear == null) return;
        if (npcType == NpcTypes.FixedPose) return;
        var skel = FindFirst<Skeleton3D>(body);
        if (skel == null) return;
        var weapons = WeaponCatalog.Index;
        if (weapons.Count == 0) return;
        foreach (var old in skel.GetChildren())
            if (old is BoneAttachment3D ba
                && ba.Name.ToString().StartsWith(WeaponNodePrefix, System.StringComparison.Ordinal))
            { skel.RemoveChild(ba); ba.QueueFree(); }
            else if (old is BoneAttachment3D builtIn
                     && builtIn.Name.ToString().StartsWith(BuiltInPlugPrefix, System.StringComparison.Ordinal))
                builtIn.Visible = true;
        foreach (var old in body.GetChildren())
            if (old is WeaponTrail wt) { body.RemoveChild(wt); wt.QueueFree(); }

        float bindHeight = WeaponMount.BindHeight(skel);
        foreach (int slot in new[] { 6, 7 })
        {
            if (slot >= gear.Length || gear[slot] <= 0) continue;
            bool right = slot == 6;
            if (!NpcPlugSlots.Holds(wearer.PlugSlots, right)) continue;
            int baseItemId = WeaponCatalog.ResolveBaseId(gear[slot]);
            if (!weapons.TryGetValue(baseItemId, out var w)) continue;
            if (KeptScene($"res://assets/items/weapon/{w.Stem}.glb") is not { } scene) continue;
            int bone = WeaponMount.Bone(skel, right, gear[slot]);
            if (bone < 0) continue;
            var attach = new BoneAttachment3D { Name = $"{WeaponNodePrefix}{slot}" };
            skel.AddChild(attach);
            attach.BoneIdx = bone;
            var mesh = scene.Instantiate<Node3D>();
            mesh.Transform = WeaponMount.HandPlugLocal(w.Pose, w.Version, w.RaceBlocks, right, wearer, bindHeight);
            ForceDoubleSided(mesh);
            attach.AddChild(mesh);
            WeaponIdle.Start(mesh, w.Anim);
            if (skel.GetNodeOrNull<Node3D>($"{BuiltInPlugPrefix}{NpcPlugSlots.PlugIndex(right)}") is { } replaced)
                replaced.Visible = false;
            uint? glowTrace = WeaponGlow.Attach(mesh, gear[slot]);
            ItemShine.Apply(mesh, gear[slot], slot);
            if (slot == 6 && w.TraceSteps > 0)
                WeaponTrail.Create(body, BodyAnim(body), skel, bone, mesh.Transform, w.Trace0, w.Trace1,
                                   glowTrace ?? w.TraceColor, TrailElement(gear[slot]));
        }
    }

    private static float SizeScale(int size) => size > 0 ? size / SizePercent : 1f;

    private static HandWearer WeaponWearer(bool npc, bool statue, int race, int npcId, int size, int modelId) =>
        npc && !statue
            ? new HandWearer(race, Npc: true, NpcId: npcId, NpcScale: SizeScale(size), PlugSlots: NpcPlugSlots.For(modelId))
            : new HandWearer(race, NpcId: npcId);

    internal static int TrailElement(int itemId)
    {
        if (itemId <= 0 || ItemData.Get(itemId) is not { } def) return WeaponTrailRule.Normal;
        int extId = ItemData.ExtIdFor(itemId);
        return extId % ExtRowSpan != 0 && ItemData.ExtRow(def.Cat, extId) is { } ext
            ? WeaponTrailRule.Element(ext.FireDamage, ext.IceDamage, ext.LightningDamage, ext.PoisonDamage)
            : WeaponTrailRule.Normal;
    }

}
