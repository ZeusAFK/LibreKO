using Godot;

namespace LibreKO.Features.Core;

internal sealed class EntityLooksSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly CosmeticsSession cosmetics;
    private readonly SelfSession self;
    private readonly GmFxSession gmFx;

    public EntityLooksSystem(IWorldContext context, CosmeticsSession cosmetics, SelfSession self, SelfAvatar avatar, GmFxSession gmFx)
    {
        this.context = context;
        this.cosmetics = cosmetics;
        this.self = self;
        this.avatar = avatar;
        this.gmFx = gmFx;
    }

    private readonly System.Collections.Generic.Dictionary<string, (Mesh? Mesh, Skin? Skin)> partCache = new();

    private readonly Dictionary<int, (Mesh? Mesh, Skin? Skin)> selfDefaultParts = new();

    internal Dictionary<int, (Mesh? Mesh, Skin? Skin)> CapturePartDefaults(Node3D visual)
    {
        var defaults = new Dictionary<int, (Mesh? Mesh, Skin? Skin)>();
        foreach (var (idx, mi) in ModelNodes.BodyParts(visual))
            defaults[idx] = (mi.Mesh, mi.Skin);
        return defaults;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    internal const string FixedWeaponMeta = "fixed_weapon";

    internal int[] WornGear(Node3D worn, int[]? gear)
    {
        int fixedWeapon = worn.GetMeta(FixedWeaponMeta, 0).AsInt32();
        if (fixedWeapon <= 0)
            return worn.GetMeta(WornLook.WornAccessoriesMeta, false).AsBool() && gear != null ? gear : System.Array.Empty<int>();
        var armed = gear != null && gear.Length > InventoryConstants.VisLeftHand
            ? (int[])gear.Clone()
            : new int[InventoryConstants.VisualSlots.Length];
        armed[InventoryConstants.VisRightHand] = fixedWeapon;
        armed[InventoryConstants.VisLeftHand] = 0;
        return armed;
    }

    private const int WeaponRightSlot = 6;
    private const int WeaponLeftSlot = 7;

    private readonly Dictionary<string, PackedScene?> gearScenes = new();

    private readonly Dictionary<string, List<string>> gearPathsByLook = new();

    private readonly HashSet<int> deferredRedress = new();

    internal bool SceneReadyFor(EntitySnapshot info) =>
        BodySceneReady(info) && (info.IsNpc || GearLoaded(PlayerRig.LookRace(info.Race, info.Class), info.Gear));

    private bool GearLoaded(int lookRace, int[]? gear)
    {
        foreach (var path in GearPaths(lookRace, gear))
            if (!StreamGear(path)) return false;
        return true;
    }

    private bool StreamGear(string path)
    {
        if (gearScenes.ContainsKey(path) || partCache.ContainsKey(path)) return true;
        if (ResourceLoader.HasCached(path) && SceneLoadInFlight != path)
        {
            gearScenes[path] = ResourceLoader.Load(path) as PackedScene;
            return true;
        }
        if (!SceneLoadSettled()) return false;
        if (gearScenes.ContainsKey(path)) return true;
        if (ResourceLoader.LoadThreadedRequest(path) != Error.Ok)
        {
            gearScenes[path] = null;
            return true;
        }
        BeginSceneLoad(path, scene => gearScenes[path] = scene);
        return SceneLoadSettled() && gearScenes.ContainsKey(path);
    }

    private Action<PackedScene?>? storeLoadedScene;

    private void BeginSceneLoad(string path, Action<PackedScene?> store)
    {
        SceneLoadInFlight = path;
        storeLoadedScene = store;
    }

    private bool SceneLoadSettled()
    {
        if (SceneLoadInFlight == null) return true;
        var status = ResourceLoader.LoadThreadedGetStatus(SceneLoadInFlight);
        if (status == ResourceLoader.ThreadLoadStatus.InProgress) return false;
        var scene = status == ResourceLoader.ThreadLoadStatus.Loaded
            ? ResourceLoader.LoadThreadedGet(SceneLoadInFlight) as PackedScene
            : null;
        var store = storeLoadedScene;
        SceneLoadInFlight = null;
        storeLoadedScene = null;
        store?.Invoke(scene);
        return true;
    }

    internal List<string> GearPaths(int lookRace, int[]? gear)
    {
        string key = $"{lookRace}:{(gear == null ? "" : string.Join(',', gear))}";
        if (gearPathsByLook.TryGetValue(key, out var cached)) return cached;
        var paths = new List<string>();
        if (gear != null)
        {
            if (!partsLoaded) { partsIndex = ModelParts.LoadPartsIndex(); partsLoaded = true; }
            foreach (var (gi, _) in ModelNodes.ArmorSlotToPart)
            {
                if (gi >= gear.Length || gear[gi] <= 0) continue;
                int resource = ArmorResource(gear[gi]);
                if (resource == 0) continue;
                int prefix = CostumeLook.PartPrefix(gear[gi], ItemData.Get(gear[gi])?.SaleType ?? 0);
                foreach (string stem in CostumeLook.PartStems(prefix, resource, lookRace))
                {
                    string path = $"res://assets/items/armor/{stem}.glb";
                    if (!ResourceLoader.Exists(path)) continue;
                    paths.Add(path);
                    break;
                }
            }

            var weapons = WeaponCatalog.Index;
            foreach (int slot in new[] { WeaponRightSlot, WeaponLeftSlot })
            {
                if (slot >= gear.Length || gear[slot] <= 0) continue;
                if (!weapons.TryGetValue(WeaponCatalog.ResolveBaseId(gear[slot]), out var weapon)) continue;
                string path = $"res://assets/items/weapon/{weapon.Stem}.glb";
                if (ResourceLoader.Exists(path)) paths.Add(path);
            }

            foreach (int slot in WingRig.WingVisualSlots)
            {
                if (slot >= gear.Length || gear[slot] <= 0 || !WingRig.TryWingPart(gear[slot], out var wing)) continue;
                string path = $"res://assets/wings/{wing.Stem}.glb";
                if (ResourceLoader.Exists(path)) paths.Add(path);
            }
        }
        gearPathsByLook[key] = paths;
        return paths;
    }

    internal void RedressWhenGearLoaded(EntityView e)
    {
        if (GearLoaded(e.LookRace, e.Gear)) RedressEntity(e);
        else deferredRedress.Add(e.Id);
    }

    internal void GearStreamTick()
    {
        if (deferredRedress.Count == 0) return;
        foreach (int id in new List<int>(deferredRedress))
        {
            if (!context.Entities.Views.TryGetValue(id, out var e))
            {
                deferredRedress.Remove(id);
                continue;
            }
            if (!GearLoaded(e.LookRace, e.Gear)) return;
            deferredRedress.Remove(id);
            RedressEntity(e);
        }
    }

    private readonly System.Collections.Generic.Dictionary<int, PackedScene?> mobSceneCache = new();

    internal System.Collections.Generic.Dictionary<int, string>? MobIndex { get; private set; }

    private readonly System.Collections.Generic.Dictionary<int, PackedScene?> playerSceneCache = new();

    private System.Collections.Generic.Dictionary<int, string>? playerIndex;

    internal string? SceneLoadInFlight { get; private set; }

    private bool BodySceneReady(EntitySnapshot info)
    {
        if (info.ObjectType == NpcTypes.ObjectType.MapObject) return true;
        var cache = info.IsNpc ? mobSceneCache : playerSceneCache;
        int key = info.IsNpc ? info.ModelId : info.Race;
        if (cache.ContainsKey(key)) return true;
        MobIndex ??= ModelParts.LoadIdIndex("res://assets/npcs/index.json");
        playerIndex ??= ModelParts.LoadIdIndex("res://assets/characters/index.json");
        var index = info.IsNpc ? MobIndex : playerIndex;
        if (!index.TryGetValue(key, out var stem)) { cache[key] = null; return true; }
        string path = info.IsNpc ? $"res://assets/npcs/{stem}.glb" : $"res://assets/characters/{stem}.glb";
        if (!ResourceLoader.Exists(path)) { cache[key] = null; return true; }
        if (!SceneLoadSettled()) return false;
        if (cache.ContainsKey(key)) return true;
        if (ResourceLoader.LoadThreadedRequest(path) != Error.Ok) return true;
        BeginSceneLoad(path, scene => { if (scene != null) cache[key] = scene; });
        return SceneLoadSettled();
    }

    internal PackedScene? ResolveMobScene(int modelId)
    {
        if (mobSceneCache.TryGetValue(modelId, out var cached)) return cached;
        MobIndex ??= ModelParts.LoadIdIndex("res://assets/npcs/index.json");

        PackedScene? result = null;
        if (MobIndex.TryGetValue(modelId, out var stem))
        {
            string resPath = $"res://assets/npcs/{stem}.glb";
            if (ResourceLoader.Exists(resPath)
                && ResourceLoader.Load(resPath) is PackedScene scene)
                result = scene;
        }
        mobSceneCache[modelId] = result;
        return result;
    }

    internal PackedScene? ResolvePlayerScene(int race)
    {
        if (playerSceneCache.TryGetValue(race, out var cached)) return cached;
        playerIndex ??= ModelParts.LoadIdIndex("res://assets/characters/index.json");

        PackedScene? result = null;
        if (playerIndex.TryGetValue(race, out var stem))
        {
            string resPath = $"res://assets/characters/{stem}.glb";
            if (ResourceLoader.Exists(resPath)
                && ResourceLoader.Load(resPath) is PackedScene scene)
                result = scene;
        }
        playerSceneCache[race] = result;
        return result;
    }

    private ModelNodes.PartsIndex? partsIndex;
    private bool partsLoaded;

    internal (Node3D body, AnimationPlayer? anim) MakeAnimatedEntity(PackedScene scene, string name, float scale)
    {
        if (scale <= 0f) scale = 1f;
        var body = new Node3D();

        var visual = new Node3D { Name = ModelAnimation.ModelNodeName, Scale = new Vector3(scale, scale, scale) };
        body.AddChild(visual);
        Node3D inst;
        using (Perf.Measure(Perf.Section.BuildInstance)) inst = scene.Instantiate<Node3D>();
        inst.RotationDegrees = new Vector3(0, 180, 0);
        visual.AddChild(inst);
        AnimationPlayer? anim;
        using (Perf.Measure(Perf.Section.BuildShare))
        {
            ModelAnimation.ForceDoubleSidedOnce(inst, scene.ResourcePath);
            SkinShare.ShareUnder(inst);
            anim = NodeSearch.FindFirst<AnimationPlayer>(inst);
        }
        if (anim != null)
            using (Perf.Measure(Perf.Section.BuildMeta))
                ModelAnimation.RegisterAnimationMetadata(anim, scene.ResourcePath.GetBaseName() + ".anim.json");

        ModelNodes.AttachNameLabel(body, name, ModelMetrics.TopY(inst) * scale + 0.35f);
        return (body, anim);
    }

    internal void GraftEquipment(Node3D body, int race, int face, int[]? gear, int hair,
                               bool hideHelmet = false, bool hideCostume = false)
    {
        if (!partsLoaded) { partsIndex = ModelParts.LoadPartsIndex(); partsLoaded = true; }
        if (partsIndex == null) return;

        var parts = ModelNodes.BodyParts(body);
        foreach (var part in parts.Values)
        {
            ItemShine.Clear(part);
            part.MaterialOverlay = null;
            part.MaterialOverride = null;
        }

        int grafted = 0, missing = 0;
        string? faceStem = FacePartStem(race, face);
        if (faceStem != null) { if (GraftPart(parts, CostumeLook.FacePart, faceStem, "characters")) grafted++; else missing++; }

        var costume = gear != null && !hideCostume
            ? CostumeLook.Dress(gear, hideHelmet, ArmorResource,
                (part, resource, itemId) => GraftArmor(parts, part, itemId, resource, race))
            : null;
        bool helmet = costume?.Contains(CostumeLook.HeadPart) == true
                      || !hideHelmet && gear != null
                      && gear.Length > ModelParts.HelmetGearIndex && gear[ModelParts.HelmetGearIndex] > 0;
        if (!helmet && HairPartStem(race, HairCode.StyleOf(hair)) is { } hairStem
            && GraftPart(parts, CostumeLook.HeadPart, hairStem, "characters"))
        {
            grafted++;
            if (HairCode.TintOf(hair) is { } tint) CharacterPreview.TintHair(parts[CostumeLook.HeadPart], tint);
        }

        if (gear != null)
            foreach (var (gi, pn) in ModelNodes.ArmorSlotToPart)
            {
                if (gi >= gear.Length || gear[gi] <= 0) continue;
                if (hideHelmet && gi == ModelParts.HelmetGearIndex) continue;
                if (costume?.Contains(pn) == true) continue;
                int resource = ArmorResource(gear[gi]);
                if (resource == 0)
                {
                    missing++; continue;
                }
                if (GraftArmor(parts, pn, gear[gi], resource, race))
                {
                    grafted++;
                    ItemShine.Apply(parts[pn], gear[gi], pn);
                }
                else missing++;
            }
        Cape.RefreshBody(body);
    }

    private bool GraftPart(System.Collections.Generic.Dictionary<int, MeshInstance3D> parts, int partNode, string stem, string dir)
    {
        if (!parts.TryGetValue(partNode, out var target)) return false;
        string resPath = $"res://assets/{dir}/{stem}.glb";

        if (!partCache.TryGetValue(resPath, out var c))
        {
            Mesh? m = null; Skin? sk = null;
            if (ResourceLoader.Exists(resPath) && ResourceLoader.Load(resPath) is PackedScene scene)
            {
                var inst = scene.Instantiate<Node3D>();
                var src = NodeSearch.FindFirst<MeshInstance3D>(inst);
                if (src?.Mesh != null)
                {
                    m = src.Mesh; sk = SkinShare.Canonical(src.Skin);
                    target.Mesh = m; target.Skin = sk;
                    ModelNodes.ForceDoubleSided(target);
                }
                inst.QueueFree();
            }
            c = (m, sk);
            partCache[resPath] = c;
        }

        if (c.Mesh == null) return false;
        target.Mesh = c.Mesh;
        target.Skin = c.Skin;
        SkinShare.FixBounds(target);
        target.Visible = true;
        target.MaterialOverlay = null;
        return true;
    }

    private int ArmorResource(int itemId) =>
        partsIndex != null
        && (partsIndex.Items.TryGetValue(itemId, out int r) || partsIndex.Items.TryGetValue(ItemData.BaseId(itemId), out r))
        ? r : 0;

    private bool GraftArmor(Dictionary<int, MeshInstance3D> parts, int partNode, int itemId, int resource, int race)
    {
        int prefix = CostumeLook.PartPrefix(itemId, ItemData.Get(itemId)?.SaleType ?? 0);
        foreach (string stem in CostumeLook.PartStems(prefix, resource, race))
            if (GraftPart(parts, partNode, stem, "items/armor")) return true;
        return false;
    }

    private string? FacePartStem(int race, int face)
    {
        if (partsIndex == null || !partsIndex.Races.TryGetValue(race, out var rr) || rr.Face.Length == 0)
            return null;
        return CharacterPreview.FaceStem(rr.Face, face);
    }

    internal string? HairPartStem(int race, int hair)
    {
        if (partsIndex == null || !partsIndex.Races.TryGetValue(race, out var rr) || rr.Hair.Length == 0)
            return null;
        return CharacterPreview.HairStem(rr.Hair, hair);
    }

    internal void AttachWeapons(Node3D body, int[]? gear, HandWearer wearer, int npcType = 0)
    {
        if (gear == null) return;
        if (npcType == NpcTypes.FixedPose) return;
        var skel = NodeSearch.FindFirst<Skeleton3D>(body);
        if (skel == null) return;
        var weapons = WeaponCatalog.Index;
        if (weapons.Count == 0) return;
        foreach (var old in skel.GetChildren())
            if (old is BoneAttachment3D ba
                && ba.Name.ToString().StartsWith(WeaponMounts.WeaponNodePrefix, System.StringComparison.Ordinal))
            { skel.RemoveChild(ba); ba.QueueFree(); }
            else if (old is BoneAttachment3D builtIn
                     && builtIn.Name.ToString().StartsWith(ModelNodes.BuiltInPlugPrefix, System.StringComparison.Ordinal))
                builtIn.Visible = true;
        foreach (var old in body.GetChildren())
            if (old is WeaponTrail wt) { body.RemoveChild(wt); wt.QueueFree(); }

        float bindHeight = WeaponMount.BindHeight(skel);
        foreach (int slot in new[] { InventoryConstants.VisRightHand, InventoryConstants.VisLeftHand })
        {
            if (slot >= gear.Length || gear[slot] <= 0) continue;
            bool right = slot == InventoryConstants.VisRightHand;
            if (!NpcPlugSlots.Holds(wearer.PlugSlots, right)) continue;
            int baseItemId = WeaponCatalog.ResolveBaseId(gear[slot]);
            if (!weapons.TryGetValue(baseItemId, out var w)) continue;
            if (ModelNodes.KeptScene($"res://assets/items/weapon/{w.Stem}.glb") is not { } scene) continue;
            int bone = WeaponMount.Bone(skel, right, gear[slot]);
            if (bone < 0) continue;
            var attach = new BoneAttachment3D { Name = $"{WeaponMounts.WeaponNodePrefix}{slot}" };
            skel.AddChild(attach);
            attach.BoneIdx = bone;
            var mesh = scene.Instantiate<Node3D>();
            mesh.Transform = WeaponMount.HandPlugLocal(w.Pose, w.Version, w.RaceBlocks, right, wearer, bindHeight);
            ModelNodes.ForceDoubleSided(mesh);
            attach.AddChild(mesh);
            WeaponIdle.Start(mesh, w.Anim);
            if (skel.GetNodeOrNull<Node3D>($"{ModelNodes.BuiltInPlugPrefix}{NpcPlugSlots.PlugIndex(right)}") is { } replaced)
                replaced.Visible = false;
            uint? glowTrace = WeaponGlow.Attach(mesh, gear[slot]);
            ItemShine.Apply(mesh, gear[slot], slot);
            if (slot == InventoryConstants.VisRightHand && w.TraceSteps > 0)
                WeaponTrail.Create(body, ModelNodes.BodyAnim(body), skel, bone, mesh.Transform, w.Trace0, w.Trace1,
                                   glowTrace ?? w.TraceColor, WeaponMounts.TrailElement(gear[slot]));
        }
    }

    private AnimationPlayer?[] selfWingAnims = new AnimationPlayer?[WingRig.WingSlotCount];
    private readonly string?[] selfWingClips = new string?[WingRig.WingSlotCount];

    internal void AttachSelfWings(Node3D body, int[] gear, int race, int zone)
    {
        selfWingAnims = WingRig.AttachWings(body, gear, race, zone, shineShadow: true);
        System.Array.Clear(selfWingClips);
    }

    private readonly WingPick selfWingPick = new();
    private double selfWingMovedAt = double.MinValue;

    internal void WingTick()
    {
        double now = context.Now;
        foreach (var e in context.Entities.Views.Values)
        {
            if (e.WingAnims == null) continue;
            if (e.AnimPaused) continue;
            string state = e.WingPick.Settle(WingRig.EntityWingState(e, now), now);
            bool active = !e.AnimThrottled;
            for (int s = 0; s < e.WingAnims.Length; s++)
            {
                if (e.WingAnims[s] is not { } anim) continue;
                if (anim.Active != active) anim.Active = active;
                WingRig.PlayWingClip(anim, ref e.WingClips[s], state);
            }
        }

        string selfState = selfWingPick.Settle(SelfWingState(now), now);
        for (int s = 0; s < selfWingAnims.Length; s++)
            if (selfWingAnims[s] is { } anim)
                WingRig.PlayWingClip(anim, ref selfWingClips[s], selfState);
    }

    private string SelfWingState(double now)
    {
        if (context.Self.Dead) return WingState.Die;
        if (WingRig.HoverPosture(avatar.Hover, avatar.Sitting, avatar.ActionRank, avatar.ActionUntil)) return WingState.Sit;
        if (WingRig.LocomotionClip(avatar.Clip)) selfWingMovedAt = now;
        if (now - selfWingMovedAt <= WingRig.WingRunLinger) return WingState.Run;
        bool acting = now < avatar.ActionUntil;
        if (avatar.Flinch is { Weight: > 0f } || (acting && avatar.ActionRank == ModelAnimation.ActionRankStruck)) return WingState.Hit;
        return acting ? WingState.Attack : WingState.Breath;
    }

    internal void CaptureSelfDefaults(Node3D selfVisual)
    {
        selfDefaultParts.Clear();
        foreach (var (idx, part) in CapturePartDefaults(selfVisual))
            selfDefaultParts[idx] = part;
    }

    internal void RestorePartDefaults(Node3D visual, Dictionary<int, (Mesh? Mesh, Skin? Skin)> defaults)
    {
        if (defaults.Count == 0) return;
        var parts = ModelNodes.BodyParts(visual);
        foreach (var (idx, def) in defaults)
            if (parts.TryGetValue(idx, out var mi))
            {
                mi.Mesh = def.Mesh;
                mi.Skin = def.Skin;
                mi.MaterialOverlay = null;
            }
    }

    internal float HandWeaponReach(bool rightFirst)
    {
        int hand = self.Inventory.HandWeapon(rightFirst);
        return hand == 0 ? 0f : (ItemData.Get(hand)?.Range ?? 0) / Coord.TenthsPerUnit;
    }

    private int[]? selfGearBuffer;

    internal int[] SelfGear()
    {
        var gear = selfGearBuffer ??= new int[InventoryConstants.VisualSlots.Length];
        for (int i = 0; i < gear.Length; i++)
        {
            int s = InventoryConstants.VisualSlots[i];
            gear[i] = s < self.Inventory.Length ? self.Inventory[s].ItemId : 0;
        }
        return gear;
    }

    private string? selfDressKey;

    internal void RerenderSelfEquipment(bool force = false)
    {
        if (context.Self.Body == null) return;
        var gear = SelfGear();
        string key = EntityRig.DressKey(PlayerRig.LookRace(self.Race, self.Class), self.Face, gear, self.Hair,
            cosmetics.HelmetHidden, cosmetics.CostumeHidden);
        if (key == selfDressKey && !force) return;
        selfDressKey = key;
        RestorePartDefaults(context.Self.Body, selfDefaultParts);
        GraftEquipment(context.Self.Body, PlayerRig.LookRace(self.Race, self.Class), self.Face, gear, self.Hair, cosmetics.HelmetHidden,
            cosmetics.CostumeHidden);
        AttachWeapons(context.Self.Body, gear, new HandWearer(self.Race));
        selfWingAnims = DressAccessories(context.Self.Body, gear, self.Race, shineShadow: true);
        System.Array.Clear(selfWingClips);
        if (avatar.Sitting)
        {
            avatar.Hover = WingRig.ResolveHoverClips(context.Self.Body, avatar.Anim);
            avatar.Clip = null;
        }
        RearmWornLook(context.Self.Body, gear);
    }

    internal void RearmWornLook(Node3D host, int[]? gear)
    {
        if (host.GetNodeOrNull<Node3D>(StateLooks.TransformNodeName) is { } worn)
            AttachWeapons(worn, WornGear(worn, gear), HandWearer.TransformedLook);
    }

    internal bool WearsAccessories(SkillData.Skill skill, Node3D ownBody, int race, Node3D worn) =>
        TransformationUse.WearsAccessories(skill.TransformUse)
    && NodeSearch.FindFirst<Skeleton3D>(worn) is { } wornRig
    && PlayerRig.SameBones(StandardRigBones(ownBody, race), StateLooks.BoneNames(wornRig));

    private string[]? standardRigBones;

    private string[] StandardRigBones(Node3D ownBody, int race)
    {
        if (!PlayerRig.IsKurian(race))
            return NodeSearch.FindFirst<Skeleton3D>(ownBody) is { } own ? StateLooks.BoneNames(own) : System.Array.Empty<string>();
        if (standardRigBones != null) return standardRigBones;
        var names = System.Array.Empty<string>();
        if (ResolvePlayerScene(PlayerRig.StandardRace(race)) is { } scene)
        {
            var rig = scene.Instantiate<Node3D>();
            if (NodeSearch.FindFirst<Skeleton3D>(rig) is { } skel) names = StateLooks.BoneNames(skel);
            rig.Free();
        }
        return standardRigBones = names;
    }

    internal AnimationPlayer?[] DressAccessories(Node3D body, int[]? gear, int race, bool shineShadow = false)
    {
        var (look, lookRace) = WornLook.AccessoryHost(body, race);
        var anims = WingRig.AttachWings(body, look == body ? gear : null, race, context.Zone, shineShadow: shineShadow);
        HandFxRig.AttachHandFx(body, look == body ? gear : null, race, context.Zone);
        if (look == null || look == body) return anims;
        HandFxRig.AttachHandFx(look, gear, lookRace, context.Zone);
        return WingRig.AttachWings(look, gear, lookRace, context.Zone, shineShadow: shineShadow);
    }

    internal void RedressEntity(EntityView e, bool force = false)
    {
        if (e.Anim == null || e.DefaultParts.Count == 0)
            return;
        string key = EntityRig.DressKey(e.LookRace, e.Face, e.Gear, e.Hair, e.HelmetHidden, e.CostumeHidden);
        if (key == e.DressKey && !force) return;
        e.DressKey = key;
        CharacterMerge.Remove(e.Body);
        RestorePartDefaults(e.Body, e.DefaultParts);
        GraftEquipment(e.Body, e.LookRace, e.Face, e.Gear, e.Hair, e.HelmetHidden, e.CostumeHidden);
        AttachWeapons(e.Body, e.Gear, WeaponMounts.WeaponWearer(e.IsNpc, e.Statue, e.Race, e.NpcId, e.Size, e.ModelId));
        e.WingAnims = DressAccessories(e.Body, CosmeticsFeature.ShownCospre(e.Gear), e.Race);
        System.Array.Clear(e.WingClips);
        if (e.Sitting)
        {
            e.Hover = WingRig.ResolveHoverClips(e.Body, e.Anim);
            e.Clip = null;
        }
        RearmWornLook(e.Body, e.Gear);
        EntityRig.ApplyEntityRenderCost(e.Body);
        if (!e.IsNpc && Config.MergeCharacters) CharacterMerge.Apply(e.Body);
        e.HitFxBox = null;
        e.PickBox = null;
    }

    private static (int Id, Color Dye) ResolveCape(int capeId, int r, int g, int b, bool isGm)
    {
        int worn = GmCape.Worn(capeId, isGm);
        if (worn == GmCape.Id) return (worn, Colors.White);
        return (worn, new Color(r / 255f, g / 255f, b / 255f));
    }

    internal void Dress(Node3D? body, int capeId, int r, int g, int b, bool isGm, int race,
                           bool highDetail = false)
    {
        if (body == null || !GodotObject.IsInstanceValid(body)) return;
        var (look, lookRace) = WornLook.AccessoryHost(body, race);
        if (look != body && body.GetNodeOrNull<Cape>("Cape") is { } shed)
        {
            body.RemoveChild(shed);
            shed.QueueFree();
        }
        if (look == null) return;
        body = look;
        race = lookRace;
        var (id, dye) = ResolveCape(capeId, r, g, b, isGm);
        var existing = body.GetNodeOrNull<Cape>("Cape");
        if (!Cape.Enabled || !Cape.IsRenderable(id) || !ChaosBattle.ShowsCapes(context.Zone))
        {
            existing?.QueueFree();
            return;
        }
        // A cape freed earlier this frame is still findable; detach it so the name is free to re-attach.
        if (existing != null && existing.IsQueuedForDeletion())
        {
            body.RemoveChild(existing);
            existing = null;
        }
        if (existing != null && GodotObject.IsInstanceValid(existing))
        {
            existing.SetCape(id, dye);
            return;
        }
        Cape.Attach(body, id, dye, race, highDetail);
    }

    internal void DressSelf()
    {
        var me = self.LastEnter;
        bool gm = gmFx.Visible(context.Self.CharId, context.Self.IsGm);
        Dress(context.Self.Visual, me.CapeId, me.CapeR, me.CapeG, me.CapeB, gm, me.Race, highDetail: true);
        var (id, _) = ResolveCape(me.CapeId, me.CapeR, me.CapeG, me.CapeB, gm);
        GD.Print($"[cape] self: sent={me.CapeId} worn={(Cape.IsRenderable(id) ? id : 0)} "
               + $"dye=({me.CapeR},{me.CapeG},{me.CapeB}) gm={gm} "
               + $"cloth={context.Self.Visual?.GetNodeOrNull<Cape>("Cape")?.Resolution ?? "-"}");
    }

    internal void DressEntity(Node3D body, EntitySnapshot info)
    {
        if (info.IsNpc) return;
        Dress(body, info.CapeId, info.CapeR, info.CapeG, info.CapeB, info.IsGm, info.Race);
    }
}
