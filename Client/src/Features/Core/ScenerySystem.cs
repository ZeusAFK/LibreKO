using Godot;

namespace LibreKO.Features.Core;

internal sealed partial class ScenerySystem : IFeature, ITerrainQueries
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly ZoneSession zone;

    public ScenerySystem(IWorldContext context, ZoneSession zone, SelfAvatar avatar)
    {
        this.context = context;
        this.zone = zone;
        this.avatar = avatar;
    }

    internal (float x, float z) WorldToMap(Vector3 position)
        => Terrain != null ? Terrain.WorldToMap(position) : Coord.ToMap(position.X, position.Z);

    internal float? GroundHeight(Vector3 worldPosition)
    {
        if (Terrain == null) return null;
        var (mapX, mapZ) = Terrain.WorldToMap(worldPosition);
        return Terrain.SampleHeight(mapX, mapZ, out float gy) ? Terrain.MapToWorld(mapX, gy, mapZ).Y : null;
    }

    internal float? FxGroundHeight(float worldX, float worldZ) => GroundHeight(new Vector3(worldX, 0f, worldZ));

    public void Build()
    {
    }

    Vector3 ITerrainQueries.RestingSpot(Vector3 feet) => RestingSpot(feet);
    Vector3 ITerrainQueries.GroundPos(float mapX, float mapZ, float serverY) => GroundPos(mapX, mapZ, serverY);
    Vector3 ITerrainQueries.GroundPos(float mapX, float mapZ, float serverY, float lift) => GroundPos(mapX, mapZ, serverY, lift);

    public void Dispose()
    {
        zone.TimeEvent -= OnServerTime;
        zone.WeatherEvent -= OnServerWeather;
        Config.GraphicsChanged -= OnGraphicsChanged;
        Config.EffectsChanged -= CullMapFx;
        fxAmbient.Clear();
        if (FxBillboard.GroundHeight == FxGroundHeight) FxBillboard.GroundHeight = null;
    }

    internal Terrain? Terrain { get; private set; }
    internal Water? Water { get; private set; }

    internal List<MapObject> Objects { get; } = new();
    internal event Action? ObjectsReset;
    internal event Action<IReadOnlyList<MapObject>>? ObjectsPlaced;
    internal event Action<MapObject, Node3D>? ObjectBuilt;
    internal event Action? MapFxReset;
    internal MapObject? PickedObject { get; private set; }
    internal Node3D? ObjRoot { get; private set; }

    private readonly Dictionary<string, Mesh?> objMeshCache = new();

    private readonly Dictionary<string, PackedScene?> objSceneCache = new();

    private readonly HashSet<string> objMissing = new();

    private readonly Dictionary<(string Key, int Cx, int Cz, bool Large), List<MapObject>> objBuckets = new();
    private Node3D? objInstRoot;

    private const ulong ObjBuildSliceLoadingUsec = 25_000;
    private const ulong ObjBuildSlicePlayingUsec = 4_000;
    private ulong objSliceStartUsec;

    private bool ObjSliceSpent() =>
        Time.GetTicksUsec() - objSliceStartUsec
        >= (context.Loading ? ObjBuildSliceLoadingUsec : ObjBuildSlicePlayingUsec);

    private async System.Threading.Tasks.Task<bool> NextObjSlice()
    {
        await context.Root.ToSignal(context.Root.GetTree(), SceneTree.SignalName.ProcessFrame);
        objSliceStartUsec = Time.GetTicksUsec();
        return context.Alive;
    }

    private const float ObjInstanceCell = 128f;
    private const int ObjInstanceMin = 2;

    private static float ObjInstanceCullPad => ObjInstanceCell * 0.7072f;

    internal sealed class FxInfo
    {
        public Node3D Node = null!;
        public string Fx = "";
        public Vector3 MapPos;
        public Quaternion MapRot;
        public float Scale;
        public Vector3 Origin;
        public GeometryInstance3D[]? SunShafts;
        public float ShaftFade = -1f;
        public bool Awake;
    }

    internal List<FxInfo> MapFx { get; } = new();

    internal Dictionary<Node3D, int> MapFxIndex { get; } = new();

    internal FxRegistry.Entry? PickedFx { get; private set; }

    internal void ClearPick()
    {
        PickedObject = null;
        PickedFx = null;
    }

    internal void PickObject(MapObject? picked)
    {
        PickedFx = null;
        PickedObject = picked;
    }

    internal void PickFx(FxRegistry.Entry? picked)
    {
        PickedObject = null;
        PickedFx = picked;
    }

    internal bool BuildTerrain(int zoneId)
    {
        Terrain = new Terrain { Name = "Terrain" };
        context.Root.AddChild(Terrain);
        if (Terrain.Build(zoneId)) return true;
        Terrain = null;
        return false;
    }
    private double fxCullAccum;

    internal void MapFxCullTick(double delta)
    {
        fxCullAccum += delta;
        if (fxCullAccum < FxCullInterval) return;
        fxCullAccum = 0;
        CullMapFx();
    }

    private static float MapFxCullDist => Config.FxDistance;

    private const float MapFxAlwaysRadius = 6f;

    private static float MapFxSleepDist => Config.FxDistance + 30f;

    private const float MapFxFrustumMargin = 40f;
    private const float MapFxFrustumSleepMargin = 70f;
    internal const double FxCullInterval = 0.2;
    private const int MapFxWakeBudget = 3;
    private readonly List<(float D2, int Index)> fxWakeQueue = new();
    private const string SunShaftFxMarker = "sunshain";
    private const float SunShaftMinShine = 0.02f;
    internal Sky Sky { get; private set; } = null!;
    internal RichTextLabel ClockLabel { get; private set; } = null!;
    private int lastClockMin = -1;
    private Weather lastClockWeather = (Weather)(-1);
    private const float DayCycleSeconds = 4f * 3600f;
    private float dayFrac = 0.30f;
    private const int WireWeatherRain = 2;
    private const int WireWeatherSnow = 3;
    private const int WireWeatherLeaves = 4;
    private const int WireWeatherLeafRain = 5;

    private Weather weather = Weather.Sunny;

    internal void BuildClockHud()
    {
        var layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);
        ClockLabel = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(MiniMap.SquareSize, HudAnchor.ClockHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ClockLabel.AddThemeFontSizeOverride("normal_font_size", 14);
        ClockLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        ClockLabel.AddThemeConstantOverride("outline_size", 4);
        ClockLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        layer.AddChild(ClockLabel);
        HudPlacement.Clock.ApplyTo(ClockLabel);
        ClockLabel.Visible = Platform.PointerUi;
    }

    internal event Action<string>? ClockChanged;

    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 24 * MinutesPerHour;
    private const float DayStartFraction = 0.25f;
    private const float DayEndFraction = 0.75f;

    internal void UpdateClock(float frac, Weather weather)
    {
        if (ClockLabel == null) return;
        int tm = ((int)(frac * MinutesPerDay) % MinutesPerDay + MinutesPerDay) % MinutesPerDay;
        if (tm == lastClockMin && weather == lastClockWeather) return;
        lastClockMin = tm; lastClockWeather = weather;
        bool day = frac >= DayStartFraction && frac < DayEndFraction;
        string tc = day ? "#ffe08a" : "#9fb8e6";
        string ww = weather switch { Weather.Rainy => "Rain", Weather.Snow => "Snow", Weather.Windy => "Windy", _ => "Clear" };
        string clock = $"{tm / MinutesPerHour:00}:{tm % MinutesPerHour:00}";
        ClockLabel.Text = $"[right][color={tc}]{clock}[/color]  [color=#ccd6e6]{ww}[/color][/right]";
        ClockChanged?.Invoke($"{clock}   {ww}");
    }

    internal void BuildEnvironment()
    {
        Sky = new Sky { Name = "Sky" };
        context.Root.AddChild(Sky);
        zone.TimeEvent += OnServerTime;
        zone.WeatherEvent += OnServerWeather;
        Config.GraphicsChanged += OnGraphicsChanged;
        Config.EffectsChanged += CullMapFx;

        dayFrac = 0.5f;
        weather = Weather.Sunny;
        if (zone.LastTime is { } t)
            OnServerTime(t.Hour, t.Minute);
        if (zone.LastWeather is { } w)
            OnServerWeather(w.Type, w.Amount);
    }

    private void OnServerWeather(int type, int amount)
    {
        if (WeatherRules.Ignored(context.Zone)) return;
        int shown = WeatherRules.Type(context.Zone, type);
        Sky.WeatherLight = WeatherRules.RelativeLight(shown, WeatherRules.Amount(shown, amount));
        weather = shown switch
        {
            WireWeatherRain or WireWeatherLeafRain => Weather.Rainy,
            WireWeatherSnow => Weather.Snow,
            WireWeatherLeaves => Weather.Windy,
            _ => Weather.Sunny,
        };
    }

    internal void OnServerTime(int hour, int minute)
    {
        float frac = (hour * MinutesPerHour + minute) / (float)MinutesPerDay;
        dayFrac = frac;
    }

    internal void UpdateSky(double delta)
    {
        dayFrac += (float)delta / DayCycleSeconds;
        dayFrac -= Mathf.Floor(dayFrac);
        var weather = this.weather;
        Sky.Tick(dayFrac, weather, avatar.Body?.Position ?? Vector3.Zero);
        UpdateClock(dayFrac, weather);
        Audio.Ambience(weather switch
        {
            Weather.Rainy => Sfx.WeatherRainy,
            Weather.Windy => Sfx.WeatherWindy,
            _ => 0,
        });
    }

    private const string ObjCullSmallGroup = "obj_cull_small";
    private const string ObjCullLargeGroup = "obj_cull_large";

    private void ApplyViewDistance()
    {
        Terrain?.ApplyViewDistance();
        foreach (var (group, baseDist) in
                 new[] { (ObjCullSmallGroup, SmallObjectCullDist), (ObjCullLargeGroup, SceneryRig.LargeObjectCullDist) })
            foreach (var node in context.Root.GetTree().GetNodesInGroup(group))
            {
                if (node is not GeometryInstance3D gi) continue;
                gi.VisibilityRangeEnd = baseDist * Config.ViewDistance
                                        + (node is MultiMeshInstance3D ? ObjInstanceCullPad : 0f);
            }
    }

    internal void OnGraphicsChanged()
    {
        Sky?.ApplyGraphics();
        Config.ApplyGraphicsToViewport();
        ApplyViewDistance();
    }

    private string[]? fxNames;
    private int fxIdx = -1;
    internal Node3D? FxPreview { get; private set; }
    internal Node3D? FxDebug { get; private set; }

    internal void PreviewNextFx()
    {
        if (fxNames == null)
        {
            var names = new System.Collections.Generic.List<string>();
            using (var f = Godot.FileAccess.Open("res://assets/fx/index.json", Godot.FileAccess.ModeFlags.Read))
                if (f != null && Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } d)
                    foreach (var k in d.Keys) names.Add(k.AsString());
            names.Sort();
            fxNames = names.ToArray();
        }
        if (fxNames.Length == 0) { GD.Print("[fx] no baked effects (run tools/bake_fx.py)"); return; }
        if (GodotObject.IsInstanceValid(FxPreview)) FxPreview!.QueueFree();
        fxIdx = (fxIdx + 1) % fxNames.Length;
        string name = fxNames[fxIdx];
        var pos = avatar.Body.Position + new Vector3(0, 1f, 0);
        FxPreview = Fx.Spawn(name, context.Root, pos);
    }

    internal Vector3 GroundPos(float mapX, float mapZ, float serverY) =>
        GroundPos(mapX, mapZ, serverY, PlayerSystem.CapsuleHalf);

    internal Vector3 GroundPos(float mapX, float mapZ, float serverY, float lift)
    {
        float y = serverY;
        if (Terrain != null && Terrain.SampleHeight(mapX, mapZ, out float gy))
            y = gy;
        return Terrain != null
            ? Terrain.MapToWorld(mapX, y + lift, mapZ)
            : Coord.ToGodot(mapX, y + lift, mapZ);
    }

    private const float RestingProbeUp = 2f;

    internal Vector3 RestingSpot(Vector3 feet)
    {
        var (mapX, mapZ) = WorldToMap(feet);
        var onTerrain = GroundPos(mapX, mapZ, feet.Y, 0f);
        var floors = ObjectFloorsAt(onTerrain.X, onTerrain.Z, Mathf.Max(onTerrain.Y, feet.Y) + RestingProbeUp,
            onTerrain.Y - EntityFloorProbeDown);
        return new Vector3(onTerrain.X, FloorChoice.Login(onTerrain.Y, floors, feet.Y), onTerrain.Z);
    }

    internal const float StepUp = 0.7f;
    internal const float NoServerHeight = float.MinValue;
    private const float EntityFloorProbeUp = 2f;
    internal const float EntityFloorProbeDown = 0.5f;
    private const float ObjectFloorProbeDown = 2f;
    private const float PitFloorProbeDown = 60f;
    private const float GroundNormalProbe = 0.6f;

    internal Vector3 EntityGroundPos(float mapX, float mapZ, float serverY, float lift)
    {
        var onTerrain = GroundPos(mapX, mapZ, serverY, lift);
        if (Terrain == null) return onTerrain;
        float terrainFeetY = onTerrain.Y - lift;
        float anchorY = Mathf.Max(terrainFeetY, Terrain.MapToWorld(mapX, serverY, mapZ).Y);
        var from = new Vector3(onTerrain.X, anchorY + EntityFloorProbeUp, onTerrain.Z);
        var to = new Vector3(onTerrain.X, terrainFeetY - EntityFloorProbeDown, onTerrain.Z);
        if (!FloorRayHit(from, to, SelfProbeExclude(), out var at, out var normal) || normal.Y < FloorNormalMin) return onTerrain;
        float floorY = at.Y;
        return floorY > terrainFeetY ? new Vector3(onTerrain.X, floorY + lift, onTerrain.Z) : onTerrain;
    }

    private static readonly Variant RayHitPosition = "position";
    private static readonly Variant RayHitNormal = "normal";
    private PhysicsRayQueryParameters3D? floorRay;
    private Godot.Collections.Array<Rid>? floorRayExclude;

    private bool FloorRayHit(Vector3 from, Vector3 to, Godot.Collections.Array<Rid>? exclude,
                             out Vector3 position, out Vector3 normal)
    {
        var query = floorRay ??= new PhysicsRayQueryParameters3D { CollisionMask = PlayerSystem.WorldCollisionLayer };
        query.From = from;
        query.To = to;
        if (!ReferenceEquals(exclude, floorRayExclude))
        {
            query.Exclude = exclude ?? new Godot.Collections.Array<Rid>();
            floorRayExclude = exclude;
        }
        using var hit = context.Root.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            position = normal = default;
            return false;
        }
        position = (Vector3)hit[RayHitPosition];
        normal = (Vector3)hit[RayHitNormal];
        return true;
    }

    private const int MaxFloorHits = 6;
    private const float FloorHitStep = 0.05f;
    internal const float FloorNormalMin = 0.5f;

    internal Vector3 GroundPosFor(bool npc, float mapX, float mapZ, float serverY, float lift) =>
        npc ? EntityGroundPos(mapX, mapZ, serverY, lift) : FloorGroundPos(mapX, mapZ, serverY, lift, login: false);

    private const float NpcFloorReferenceUp = 1f;
    private const float NpcFloorSearchUp = 20f;

    internal Vector3 InOutGroundPos(bool npc, float mapX, float mapZ, float serverY, float lift, CollisionObject3D? own = null) =>
        npc ? NpcInOutGroundPos(mapX, mapZ, serverY, lift, own) : FloorGroundPos(mapX, mapZ, serverY, lift, login: false);

    private Vector3 NpcInOutGroundPos(float mapX, float mapZ, float serverY, float lift, CollisionObject3D? own)
    {
        var onTerrain = GroundPos(mapX, mapZ, serverY, lift);
        if (Terrain == null) return onTerrain;
        float terrainFeetY = onTerrain.Y - lift;
        float anchorY = serverY == NoServerHeight ? terrainFeetY : Terrain.MapToWorld(mapX, serverY, mapZ).Y;
        float reference = anchorY + NpcFloorReferenceUp;
        var floors = ObjectFloorsAt(onTerrain.X, onTerrain.Z,
            Mathf.Max(terrainFeetY, reference) + NpcFloorSearchUp, terrainFeetY - EntityFloorProbeDown, own);
        return new Vector3(onTerrain.X, FloorChoice.Login(terrainFeetY, floors, reference) + lift, onTerrain.Z);
    }

    internal Vector3 FloorGroundPos(float mapX, float mapZ, float serverY, float lift, bool login)
    {
        var onTerrain = GroundPos(mapX, mapZ, serverY, lift);
        if (Terrain == null || serverY == NoServerHeight) return EntityGroundPos(mapX, mapZ, serverY, lift);
        float terrainFeetY = onTerrain.Y - lift;
        float serverWorldY = Terrain.MapToWorld(mapX, serverY, mapZ).Y;
        var floors = ObjectFloorsAt(onTerrain.X, onTerrain.Z,
            Mathf.Max(terrainFeetY, serverWorldY) + EntityFloorProbeUp, terrainFeetY - EntityFloorProbeDown);
        float y = login
            ? FloorChoice.Login(terrainFeetY, floors, serverWorldY)
            : FloorChoice.Nearest(terrainFeetY, floors, serverWorldY);
        return new Vector3(onTerrain.X, y + lift, onTerrain.Z);
    }

    internal List<float> ObjectFloorsAt(float x, float z, float top, float bottom, CollisionObject3D? alsoExclude = null)
    {
        var floors = new List<float>();
        var exclude = SelfProbeExclude();
        if (alsoExclude != null && GodotObject.IsInstanceValid(alsoExclude))
        {
            exclude = exclude != null ? new Godot.Collections.Array<Rid>(exclude) : new Godot.Collections.Array<Rid>();
            exclude.Add(alsoExclude.GetRid());
        }
        float from = top;
        for (int i = 0; i < MaxFloorHits && from > bottom; i++)
        {
            if (!FloorRayHit(new Vector3(x, from, z), new Vector3(x, bottom, z), exclude, out var at, out var normal)) break;
            if (normal.Y >= FloorNormalMin) floors.Add(at.Y);
            from = at.Y - FloorHitStep;
        }
        return floors;
    }

    private Godot.Collections.Array<Rid>? selfProbeExclude;

    internal Godot.Collections.Array<Rid>? SelfProbeExclude()
    {
        if (avatar.Body == null) return null;
        return selfProbeExclude ??= new Godot.Collections.Array<Rid> { avatar.Body.GetRid() };
    }

    internal void RegroundEntities()
    {
        foreach (var e in context.Entities.Views.Values)
        {
            if (e.HasTarget) continue;
            e.Body.Position = InOutGroundPos(e.IsNpc, e.MapX, e.MapZ, e.MapY, e.Lift, e.BridgeFloor);
        }
    }

    internal Vector3 GroundNormalAt(Vector3 worldPosition)
    {
        if (Terrain == null) return Vector3.Up;
        float d = GroundNormalProbe;
        float hx = HeightNear(worldPosition + new Vector3(d, 0, 0)) - HeightNear(worldPosition - new Vector3(d, 0, 0));
        float hz = HeightNear(worldPosition + new Vector3(0, 0, d)) - HeightNear(worldPosition - new Vector3(0, 0, d));
        return new Vector3(-hx, 2f * d, -hz).Normalized();
    }

    internal float HeightNear(Vector3 worldPosition) => GroundHeight(worldPosition) ?? worldPosition.Y;

    private bool PitFloors => context.Zone == TempleZones.JuraidMountain;

    internal float? StandingFloor(Vector3 grounded, float feetY)
    {
        float objY = ObjectFloorY(grounded, feetY);
        bool found = objY > float.NegativeInfinity;
        return objY > grounded.Y || (PitFloors && found) ? objY : null;
    }

    private float ObjectFloorY(Vector3 atGodot, float feetY)
    {
        if (avatar.Body == null || avatar.NoClip) return float.NegativeInfinity;
        var from = new Vector3(atGodot.X, feetY + StepUp, atGodot.Z);
        float bottomOffset = PitFloors ? PitFloorProbeDown : ObjectFloorProbeDown;
        var to = new Vector3(atGodot.X, Mathf.Min(atGodot.Y, feetY) - bottomOffset, atGodot.Z);
        if (!FloorRayHit(from, to, SelfProbeExclude(), out var at, out var normal)) return float.NegativeInfinity;
        if (normal.Y < FloorNormalMin) return float.NegativeInfinity;
        return at.Y;
    }

    private PhysicsShapeQueryParameters3D? capsuleQuery;

    internal bool CapsuleOverlaps()
    {
        if (avatar.Body == null || avatar.Capsule == null || avatar.NoClip) return false;
        var t = avatar.Body.GlobalTransform;
        t.Origin += t.Basis * new Vector3(0, avatar.CapsuleOffsetY, 0);
        var q = capsuleQuery ??= new PhysicsShapeQueryParameters3D
        {
            Shape = avatar.Capsule, CollisionMask = PlayerSystem.WorldCollisionLayer, Margin = 0f, Exclude = SelfProbeExclude()!,
        };
        q.Transform = t;
        using var hits = (Godot.Collections.Array)context.Root.GetWorld3D().DirectSpaceState.IntersectShape(q, 1);
        return hits.Count > 0;
    }

    public bool ObjectsPending { get; private set; } = true;

    internal string AimMapFxCamera(string fxName, Vector3 mapNear, float dist, float heightAbove,
        float yawDeg = float.NaN)
    {
        if (context.Camera == null || Terrain == null) return "no camera";
        int best = -1;
        float bestD2 = float.MaxValue;
        var want = Terrain.MapToWorld(mapNear.X, mapNear.Y, mapNear.Z);
        for (int i = 0; i < MapFx.Count; i++)
        {
            if (MapFx[i].Fx != fxName) continue;
            float d2 = MapFx[i].Origin.DistanceSquaredTo(want);
            if (d2 < bestD2) { bestD2 = d2; best = i; }
        }
        if (best < 0) return $"no map fx named {fxName}";

        var info = MapFx[best];
        string where = AimTestCamera(info.Origin, dist, heightAbove, yawDeg);
        return $"fx[{best}] {info.Fx} map=({info.MapPos.X:F1},{info.MapPos.Z:F1}) " +
               $"awake={info.Awake} {where} cull={MapFxCullDist:F0}{SceneryRig.DescribeFxParts(info.Node)}";
    }

    internal string AimTestCameraMap(Vector3 mapPos, float dist, float heightAbove, float yawDeg = float.NaN)
    {
        if (context.Camera == null || Terrain == null) return "no camera";
        return AimTestCamera(Terrain.MapToWorld(mapPos.X, mapPos.Y, mapPos.Z), dist, heightAbove, yawDeg);
    }

    private string AimTestCamera(Vector3 target, float dist, float heightAbove, float yawDeg)
    {
        if (context.Camera is not { } camera) return "no camera";
        Vector3 dir;
        if (float.IsNaN(yawDeg))
        {
            dir = camera.GlobalPosition - target;
            dir.Y = 0f;
            dir = dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector3.Back;
        }
        else
        {
            float yaw = Mathf.DegToRad(yawDeg);
            dir = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        }
        camera.GlobalPosition = target + dir * dist + Vector3.Up * heightAbove;
        camera.LookAt(target, Vector3.Up);
        fxCullAccum = FxCullInterval;
        CullMapFx();
        var screen = context.Camera.UnprojectPosition(target);
        return $"camDist={context.Camera.GlobalPosition.DistanceTo(target):F1} screen=({screen.X:F0},{screen.Y:F0})";
    }

    internal async System.Threading.Tasks.Task BuildObjects()
    {
        ObjectsPending = true;
        if (Terrain == null) { ObjectsPending = false; return; }
        var objs = ZoneObjects.LoadJson(Terrain.ZoneStem);
        if (objs == null) { ObjectsPending = false; return; }

        ObjRoot = new Node3D { Name = "Objects" };
        context.Root.AddChild(ObjRoot);
        Objects.Clear();
        objBuckets.Clear();
        objInstRoot = null;
        TextureAnim.Reset();
        PickedObject = null;
        ObjectsReset?.Invoke();
        foreach (var o in objs.Items)
            Objects.Add(new MapObject
            {
                Name = o.Name, MapPos = o.MapPos, MapRot = o.Rot, Scale = o.Scale,
                Origin = Terrain.MapToWorld(o.MapPos.X, o.MapPos.Y, o.MapPos.Z),
                EventId = o.EventId, EventType = (ObjectEventType)o.EventType, Belong = o.Belong, NpcId = o.NpcId,
            });

        ObjectsPlaced?.Invoke(Objects);
        objSliceStartUsec = Time.GetTicksUsec();
        for (int i = 0; i < Objects.Count; i++)
        {
            BuildObjectSlot(Objects[i]);
            if (ObjSliceSpent() && !await NextObjSlice()) return;
        }
        await BuildObjectInstances();
        if (!context.Alive) return;
        ObjectsPending = false;
    }

    private void BuildObjectSlot(MapObject o)
    {
        if (o.Built || ObjRoot == null || Terrain == null) return;
        o.Built = true;
        string key = ModelNodes.SafeModelName(o.Name);
        Diag.Step($"object {key}");
        var scene = ResolveObjectScene(key);
        var q = new Quaternion(o.MapRot.X, -o.MapRot.Y, -o.MapRot.Z, o.MapRot.W).Normalized();
        var localBasis = new Basis(q) * Basis.FromScale(o.Scale);

        if (scene != null && ObjectNeedsCompleteScene(key, scene))
        {
            var instance = scene.Instantiate<Node3D>();
            ConfigureObjectMaterialsOnce(key, instance);
            ItemShineLight.MarkScenery(instance);
            instance.Transform = new Transform3D(Terrain.Transform.Basis * localBasis, o.Origin);
            SceneryRig.ConfigureAnimatedObject(instance);
            if (NodeSearch.FindFirst<AnimationPlayer>(instance) is { } animation)
            {
                var names = animation.GetAnimationList();
                if (names.Length > 0) animation.Play(names[0]);
            }
            ObjRoot.AddChild(instance);
            o.Mi = NodeSearch.FindFirst<MeshInstance3D>(instance);
            ObjectBuilt?.Invoke(o, instance);
            return;
        }

        var mesh = ResolveObjectMesh(key);
        if (mesh == null)
        {
            // These placements name meshes absent from retail's object.src — unbakeable, and retail
            // draws nothing for them either.
            if (objMissing.Add(key))
                GD.Print($"[objects] no model for '{key}' — not in object.src, drawing nothing");
            return;
        }

        var xform = new Transform3D(Terrain.Transform.Basis * localBasis, o.Origin);
        var asz = mesh.GetAabb().Size;
        bool large = Mathf.Max(asz.X * Mathf.Abs(o.Scale.X), asz.Z * Mathf.Abs(o.Scale.Z)) >= LargeObjectFootprint;

        if (o.EventId <= 0)
        {
            o.InstMesh = mesh;
            o.InstXform = xform;
            var cell = (key, Mathf.FloorToInt(o.Origin.X / ObjInstanceCell),
                             Mathf.FloorToInt(o.Origin.Z / ObjInstanceCell), large);
            if (!objBuckets.TryGetValue(cell, out var bucket))
                objBuckets[cell] = bucket = new List<MapObject>();
            bucket.Add(o);
            return;
        }

        BuildObjectNode(o, mesh, xform, large);
    }

    private void BuildObjectNode(MapObject o, Mesh mesh, Transform3D xform, bool large)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            Transform = xform,
            Layers = 1u | ItemShineLight.SceneryLayer,
            VisibilityRangeEnd = (large ? SceneryRig.LargeObjectCullDist : SmallObjectCullDist) * Config.ViewDistance,
            VisibilityRangeEndMargin = ModelNodes.ObjectCullFade,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
        if (large) mi.LodBias = LargeObjectLodBias;
        else mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        mi.AddToGroup(large ? ObjCullLargeGroup : ObjCullSmallGroup);
        o.InstMesh = null;
        o.Mi = mi;
        ObjRoot!.AddChild(mi);
        ObjectBuilt?.Invoke(o, mi);
    }

    private async System.Threading.Tasks.Task BuildObjectInstances()
    {
        if (ObjRoot == null || objBuckets.Count == 0) return;
        objInstRoot = new Node3D { Name = "ObjectInstances" };
        ObjRoot.AddChild(objInstRoot);

        int made = 0, instanced = 0, loose = 0;
        foreach (var (cell, bucket) in objBuckets)
        {
            bool large = cell.Large;
            if (bucket.Count < ObjInstanceMin)
            {
                foreach (var o in bucket)
                {
                    loose++;
                    BuildObjectNode(o, o.InstMesh!, o.InstXform, large);
                }
            }
            else
            {
                var centre = Vector3.Zero;
                foreach (var o in bucket) centre += o.InstXform.Origin;
                centre /= bucket.Count;

                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = bucket[0].InstMesh,
                    InstanceCount = bucket.Count,
                };
                for (int i = 0; i < bucket.Count; i++)
                {
                    var x = bucket[i].InstXform;
                    mm.SetInstanceTransform(i, new Transform3D(x.Basis, x.Origin - centre));
                }
                var mmi = new MultiMeshInstance3D
                {
                    Multimesh = mm,
                    Layers = 1u | ItemShineLight.SceneryLayer,
                    Position = centre,
                    CastShadow = large
                        ? GeometryInstance3D.ShadowCastingSetting.On
                        : GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = (large ? SceneryRig.LargeObjectCullDist : SmallObjectCullDist)
                                         * Config.ViewDistance + ObjInstanceCullPad,
                    VisibilityRangeEndMargin = ModelNodes.ObjectCullFade,
                    VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                };
                mmi.AddToGroup(large ? ObjCullLargeGroup : ObjCullSmallGroup);
                objInstRoot.AddChild(mmi);
                made++;
                instanced += bucket.Count;
            }

            if (ObjSliceSpent() && !await NextObjSlice()) return;
        }
        objBuckets.Clear();
        int surf = 0;
        foreach (var c in objInstRoot.GetChildren())
            if (c is MultiMeshInstance3D { Multimesh.Mesh: { } m }) surf += m.GetSurfaceCount();
        GD.Print($"[objects] instanced {instanced} placements into {made} multimeshes "
                 + $"({surf} surfaces), {loose} left loose");
    }

    private readonly Dictionary<string, bool> objNeedsScene = new();

    private bool ObjectNeedsCompleteScene(string key, PackedScene scene)
    {
        if (objNeedsScene.TryGetValue(key, out bool need)) return need;
        var probe = scene.Instantiate<Node3D>();
        need = NodeSearch.FindFirst<AnimationPlayer>(probe) != null || SceneryRig.RequiresCompleteObjectScene(probe);
        probe.QueueFree();
        objNeedsScene[key] = need;
        return need;
    }

    private readonly HashSet<string> objectMaterialsApplied = new();

    private void ConfigureObjectMaterialsOnce(string key, Node instance)
    {
        if (objectMaterialsApplied.Add(key)) SceneryRig.ConfigureObjectMaterials(instance);
    }

    internal StaticBody3D? ObjCollision { get; private set; }

    internal void BuildObjectCollision()
    {
        if (Terrain == null) return;
        var coll = ZoneCollision.Load(Terrain.ZoneStem);
        if (coll == null)
        {
            return;
        }
        var verts = new Vector3[coll.Verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var v = coll.Verts[i];
            verts[i] = Terrain.MapToWorld(v.X, v.Y, v.Z);
        }
        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(verts);
        ObjCollision = new StaticBody3D { Name = "ObjectCollision" };
        ObjCollision.AddChild(new CollisionShape3D { Shape = shape });
        context.Root.AddChild(ObjCollision);
    }

    private readonly Dictionary<Node3D, AudioStreamPlayer3D> fxAmbient = new();

    private void AttachFxAmbience(Node3D node, string fxName)
    {
        int sid = SoundCatalog.FxSound(fxName);
        if (sid == 0 || !SoundCatalog.TryGet(sid, out var e)) return;
        if (e.Type != SoundCatalog.Kind.ThreeD) return;
        if (Audio.Loop(sid, node) is { } player)
        {
            player.StreamPaused = true;
            fxAmbient[node] = player;
        }
    }

    private void SetFxAmbience(Node3D node, bool audible)
    {
        if (fxAmbient.TryGetValue(node, out var p) && GodotObject.IsInstanceValid(p))
            p.StreamPaused = !audible;
    }

    internal void BuildFxPlacements()
    {
        if (Terrain == null) return;
        MapFx.Clear(); MapFxIndex.Clear();
        PickedFx = null;
        MapFxReset?.Invoke();
        var fxs = FxPlacements.LoadJson(Terrain.ZoneStem);
        if (fxs == null || fxs.Items.Length == 0)
        {
            return;
        }

        var root = new Node3D { Name = "MapFx" };
        context.Root.AddChild(root);
        int placed = 0, missing = 0;
        foreach (var pl in fxs.Items)
        {
            var origin = Terrain.MapToWorld(pl.MapPos.X, pl.MapPos.Y, pl.MapPos.Z);
            var node = Fx.Spawn(pl.Fx, root, origin);
            if (node == null) { missing++; continue; }
            var q = new Quaternion(pl.Rot.X, -pl.Rot.Y, -pl.Rot.Z, pl.Rot.W);
            if (q.LengthSquared() > 0.0001f)
                node.Quaternion = q.Normalized();
            if (pl.Scale > 0f && !Mathf.IsEqualApprox(pl.Scale, 1f))
                node.Scale = Vector3.One * pl.Scale;
            Fx.SetShown(node, false);
            node.ProcessMode = Node.ProcessModeEnum.Disabled;
            if (node is FxInstance mapRoot) mapRoot.Asleep = true;
            AttachFxAmbience(node, pl.Fx);
            FxLampLight.Attach(node, pl.Fx);
            MapFxIndex[node] = MapFx.Count;
            MapFx.Add(new FxInfo
            {
                Node = node, Fx = pl.Fx, MapPos = pl.MapPos, MapRot = pl.Rot,
                Scale = pl.Scale > 0f ? pl.Scale : 1f, Origin = origin,
                SunShafts = pl.Fx.Contains(SunShaftFxMarker, StringComparison.OrdinalIgnoreCase)
                    ? ModelNodes.FindAll<GeometryInstance3D>(node).ToArray()
                    : null,
            });
            placed++;
        }
        CullMapFx(int.MaxValue);
    }

    private readonly ViewFrustum mapFxFrustum = new();

    internal void CullMapFx() => CullMapFx(MapFxWakeBudget);

    internal void CullMapFx(int wakeBudget)
    {
        if (MapFx.Count == 0 || context.Camera == null) return;
        var cp = context.Camera.GlobalPosition;
        float far2 = MapFxCullDist * MapFxCullDist;
        float near2 = MapFxAlwaysRadius * MapFxAlwaysRadius;
        float sleepFar2 = MapFxSleepDist * MapFxSleepDist;
        mapFxFrustum.Refresh(context.Camera);
        float shine = Sky?.SunShine ?? 1f;
        fxWakeQueue.Clear();
        for (int i = 0; i < MapFx.Count; i++)
        {
            var info = MapFx[i];
            var node = info.Node;
            if (!GodotObject.IsInstanceValid(node)) continue;
            var pos = info.Origin;
            float d2 = cp.DistanceSquaredTo(pos);
            bool awake = info.Awake;
            float margin = awake ? MapFxFrustumSleepMargin : MapFxFrustumMargin;
            bool inFrustum = mapFxFrustum.Contains(pos, margin);
            float far = awake ? sleepFar2 : far2;
            bool show = Config.FxAmbient
                && ((d2 <= far && (d2 <= near2 || inFrustum)) || node == PickedFx?.Node);
            if (info.SunShafts != null)
            {
                show &= shine > SunShaftMinShine;
                if (show && !Mathf.IsEqualApprox(shine, info.ShaftFade))
                {
                    info.ShaftFade = shine;
                    foreach (var gi in info.SunShafts)
                        if (GodotObject.IsInstanceValid(gi)) Fx.SetTransparency(gi, 1f - shine);
                }
            }
            if (show == awake) continue;
            if (show)
            {
                fxWakeQueue.Add((d2, i));
                continue;
            }
            info.Awake = false;
            Fx.SetShown(node, false);
            node.ProcessMode = Node.ProcessModeEnum.Disabled;
            if (node is FxInstance sleeping) sleeping.Asleep = true;
            SetFxAmbience(node, false);
        }
        FxLampLight.Rank(cp);
        if (fxWakeQueue.Count == 0) return;
        fxWakeQueue.Sort((a, b) => a.D2.CompareTo(b.D2));
        int wake = Mathf.Min(wakeBudget, fxWakeQueue.Count);
        for (int k = 0; k < wake; k++)
        {
            var woken = MapFx[fxWakeQueue[k].Index];
            var node = woken.Node;
            woken.Awake = true;
            Fx.SetShown(node, true);
            node.ProcessMode = Node.ProcessModeEnum.Inherit;
            if (node is FxInstance awake) awake.Asleep = false;
            SetFxAmbience(node, true);
        }
        if (Diag.SlowLog) GD.Print($"[slow] mapfx woke {wake} of {fxWakeQueue.Count}");
        fxWakeQueue.Clear();
    }

    private const float LargeObjectFootprint = 15f;
    private const float LargeObjectLodBias = 16f;
    private const float SmallObjectCullDist = 200f;

    internal void BuildWater()
    {
        if (Terrain == null) return;
        Water = new Water { Name = "Water" };
        context.Root.AddChild(Water);
        if (!Water.Build(Terrain, Terrain.ZoneStem))
        {
            Water.QueueFree();
            Water = null;
        }
    }

    private Mesh? ResolveObjectMesh(string key)
    {
        if (objMeshCache.TryGetValue(key, out var cached)) return cached;

        Mesh? result = null;
        var scene = ResolveObjectScene(key);
        if (scene != null)
        {
            var inst = scene.Instantiate();
            result = ModelNodes.FindFirstMesh(inst);
            inst.QueueFree();
        }
        if (result != null)
            SceneryRig.ConfigureObjectMaterials(result);
        objMeshCache[key] = result;
        return result;
    }

    private PackedScene? ResolveObjectScene(string key)
    {
        if (objSceneCache.TryGetValue(key, out var cached)) return cached;
        string resPath = "res://assets/objects/" + key + ".glb";
        var result = ResourceLoader.Exists(resPath) ? ResourceLoader.Load(resPath) as PackedScene : null;
        objSceneCache[key] = result;
        return result;
    }
}
