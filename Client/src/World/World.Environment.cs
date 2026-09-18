using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private Terrain? _terrain;
    private Water? _water;
    private sealed class ObjInfo
    {
        public MeshInstance3D? Mi;
        public Mesh? InstMesh;
        public Transform3D InstXform;
        public bool Built;
        public string Name = "";
        public Vector3 KoPos;
        public Quaternion KoRot;
        public Vector3 Scale;
        public Vector3 Origin;
        public int EventId;
        public int EventType;
        public int Belong;
        public int NpcId;

        public Mesh? HitMesh => Mi?.Mesh ?? InstMesh;
        public Transform3D HitXform => Mi != null ? Mi.GlobalTransform : InstXform;
    }
    private readonly List<ObjInfo> _objects = new();
    private ObjInfo? _pickedObject;
    private Node3D? _objRoot;
    private readonly Dictionary<string, Mesh?> _objMeshCache = new();
    private readonly Dictionary<string, PackedScene?> _objSceneCache = new();

    private readonly HashSet<string> _objMissing = new();

    private readonly Dictionary<(string Key, int Cx, int Cz, bool Large), List<ObjInfo>> _objBuckets = new();
    private Node3D? _objInstRoot;

    private const int ObjBuildBatch = 150;
    private const int ObjInstanceBatch = 24;
    private const float ObjInstanceCell = 128f;
    private const int ObjInstanceMin = 2;
    private static float ObjInstanceCullPad => ObjInstanceCell * 0.7072f;

    private sealed class FxInfo
    {
        public Node3D Node = null!;
        public string Fx = "";
        public Vector3 KoPos;
        public Quaternion KoRot;
        public float Scale;
        public Vector3 Origin;
        public GeometryInstance3D[]? SunShafts;
        public float ShaftFade = -1f;
    }
    private readonly List<FxInfo> _mapFx = new();
    private readonly Dictionary<Node3D, int> _mapFxIndex = new();
    private FxRegistry.Entry? _pickedFx;
    private double _fxCullAccum;
    private double _mapHudAccum;
    private const double MapHudInterval = 1.0 / 12.0;
    private static float MapFxCullDist => Config.FxDistance;
    private const float MapFxAlwaysRadius = 6f;
    private static float MapFxSleepDist => Config.FxDistance + 30f;
    private const float MapFxFrustumMargin = 40f;
    private const float MapFxFrustumSleepMargin = 70f;
    private const double FxCullInterval = 0.2;
    private const int MapFxWakeBudget = 3;
    private readonly List<(float D2, int Index)> _fxWakeQueue = new();
    private const string SunShaftFxMarker = "sunshain";
    private const float SunShaftMinShine = 0.02f;
    private Sky _sky = null!;
    private RichTextLabel _clockLabel = null!;
    private int _lastClockMin = -1;
    private Weather _lastClockWeather = (Weather)(-1);
    private const float DayCycleSeconds = 4f * 3600f;
    private float _dayFrac = 0.30f;
    private const int WireWeatherRain = 2;
    private const int WireWeatherSnow = 3;
    private const int WireWeatherLeaves = 4;
    private const int WireWeatherLeafRain = 5;

    private Weather _weather = Weather.Sunny;

    private void BuildClockHud()
    {
        var layer = new CanvasLayer { Layer = 64 };
        AddChild(layer);
        _clockLabel = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(MiniMap.SquareSize, HudAnchor.ClockHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _clockLabel.AddThemeFontSizeOverride("normal_font_size", 14);
        _clockLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _clockLabel.AddThemeConstantOverride("outline_size", 4);
        _clockLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        layer.AddChild(_clockLabel);
        HudPlacement.Clock.ApplyTo(_clockLabel);
        _clockLabel.Visible = Platform.PointerUi;
    }

    private void UpdateClock(float frac, Weather w)
    {
        if (_clockLabel == null) return;
        int tm = ((int)(frac * 1440f) % 1440 + 1440) % 1440;
        if (tm == _lastClockMin && w == _lastClockWeather) return;
        _lastClockMin = tm; _lastClockWeather = w;
        bool day = frac >= 0.25f && frac < 0.75f;
        string tc = day ? "#ffe08a" : "#9fb8e6";
        string ww = w switch { Weather.Rainy => "Rain", Weather.Snow => "Snow", Weather.Windy => "Windy", _ => "Clear" };
        _clockLabel.Text = $"[right][color={tc}]{tm / 60:00}:{tm % 60:00}[/color]  [color=#ccd6e6]{ww}[/color][/right]";
        SetExpBarClock($"{tm / 60:00}:{tm % 60:00}   {ww}");
    }

    private void BuildEnvironment()
    {
        _sky = new Sky { Name = "Sky" };
        AddChild(_sky);
        Net.I.WarpEvent += OnWarp;
        Net.I.TimeEvent += OnServerTime;
        Net.I.WeatherEvent += OnServerWeather;

        _dayFrac = 0.5f;
        _weather = Weather.Sunny;
        if (Net.I.LastTime is { } t)
            OnServerTime(t.Hour, t.Minute);
        if (Net.I.LastWeather is { } w)
            OnServerWeather(w.Type, w.Amount);
    }

    private void OnServerWeather(int type, int amount) => _weather = type switch
    {
        WireWeatherRain or WireWeatherLeafRain => Weather.Rainy,
        WireWeatherSnow => Weather.Snow,
        WireWeatherLeaves => Weather.Windy,
        _ => Weather.Sunny,
    };

    private void OnServerTime(int hour, int minute)
    {
        float frac = (hour * 60 + minute) / 1440f;
        _dayFrac = frac;
    }

    private void UpdateSky(double delta)
    {
        _dayFrac += (float)delta / DayCycleSeconds;
        _dayFrac -= Mathf.Floor(_dayFrac);
        var weather = _weather;
        _sky.Tick(_dayFrac, weather, _self?.Position ?? Vector3.Zero);
        UpdateClock(_dayFrac, weather);
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
        _terrain?.ApplyViewDistance();
        foreach (var (group, baseDist) in
                 new[] { (ObjCullSmallGroup, SmallObjectCullDist), (ObjCullLargeGroup, LargeObjectCullDist) })
            foreach (var node in GetTree().GetNodesInGroup(group))
            {
                if (node is not GeometryInstance3D gi) continue;
                gi.VisibilityRangeEnd = baseDist * Config.ViewDistance
                                        + (node is MultiMeshInstance3D ? ObjInstanceCullPad : 0f);
            }
    }

    private void OnGraphicsChanged()
    {
        _sky?.ApplyGraphics();
        Config.ApplyGraphicsToViewport();
        ApplyViewDistance();
        if (Cape.Enabled != Config.Capes)
        {
            Cape.Enabled = Config.Capes;
            RefreshAllCapes();
        }
    }

    private string[]? _fxNames;
    private int _fxIdx = -1;
    private Node3D? _fxPreview;
    private Node3D? _fxDebug;

    private void PreviewNextFx()
    {
        if (_fxNames == null)
        {
            var names = new System.Collections.Generic.List<string>();
            using (var f = Godot.FileAccess.Open("res://assets/fx/index.json", Godot.FileAccess.ModeFlags.Read))
                if (f != null && Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } d)
                    foreach (var k in d.Keys) names.Add(k.AsString());
            names.Sort();
            _fxNames = names.ToArray();
        }
        if (_fxNames.Length == 0) { GD.Print("[fx] no baked effects (run tools/bake_fx.py)"); return; }
        if (GodotObject.IsInstanceValid(_fxPreview)) _fxPreview!.QueueFree();
        _fxIdx = (_fxIdx + 1) % _fxNames.Length;
        string name = _fxNames[_fxIdx];
        var pos = _self.Position + new Vector3(0, 1f, 0);
        _fxPreview = Fx.Spawn(name, this, pos);
    }

    private Vector3 GroundPos(float koX, float koZ, float serverY) =>
        GroundPos(koX, koZ, serverY, CapsuleHalf);

    private Vector3 GroundPos(float koX, float koZ, float serverY, float lift)
    {
        float y = serverY;
        if (_terrain != null && _terrain.SampleHeight(koX, koZ, out float gy))
            y = gy;
        return _terrain != null
            ? _terrain.KoToWorld(koX, y + lift, koZ)
            : Coord.ToGodot(koX, y + lift, koZ);
    }

    private const float StepUp = 0.7f;
    private const float EntityFloorProbeUp = 2f;
    private const float EntityFloorProbeDown = 0.5f;
    private const float GroundNormalProbe = 0.6f;

    private Vector3 EntityGroundPos(float koX, float koZ, float serverY, float lift)
    {
        var onTerrain = GroundPos(koX, koZ, serverY, lift);
        if (_terrain == null) return onTerrain;
        float terrainFeetY = onTerrain.Y - lift;
        float anchorY = Mathf.Max(terrainFeetY, _terrain.KoToWorld(koX, serverY, koZ).Y);
        var space = GetWorld3D().DirectSpaceState;
        var from = new Vector3(onTerrain.X, anchorY + EntityFloorProbeUp, onTerrain.Z);
        var to = new Vector3(onTerrain.X, terrainFeetY - EntityFloorProbeDown, onTerrain.Z);
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, WorldCollisionLayer));
        if (hit.Count == 0 || ((Vector3)hit["normal"]).Y < 0.5f) return onTerrain;
        float floorY = ((Vector3)hit["position"]).Y;
        return floorY > terrainFeetY ? new Vector3(onTerrain.X, floorY + lift, onTerrain.Z) : onTerrain;
    }

    private void RegroundEntities()
    {
        foreach (var e in _ents.Values)
        {
            if (e.HasTarget) continue;
            e.Body.Position = EntityGroundPos(e.KoX, e.KoZ, e.KoY, e.Lift);
        }
    }

    private Vector3 GroundNormalAt(Vector3 world)
    {
        if (_terrain == null) return Vector3.Up;
        float d = GroundNormalProbe;
        float hx = HeightNear(world + new Vector3(d, 0, 0)) - HeightNear(world - new Vector3(d, 0, 0));
        float hz = HeightNear(world + new Vector3(0, 0, d)) - HeightNear(world - new Vector3(0, 0, d));
        return new Vector3(-hx, 2f * d, -hz).Normalized();
    }

    private float HeightNear(Vector3 world)
    {
        var (kx, kz) = WorldToKo(world);
        return _terrain != null && _terrain.SampleHeight(kx, kz, out float y) ? _terrain.KoToWorld(kx, y, kz).Y : world.Y;
    }

    private float ObjectFloorY(Vector3 atGodot, float feetY)
    {
        if (_selfBody == null || NoClip) return float.NegativeInfinity;
        var space = GetWorld3D().DirectSpaceState;
        var from = new Vector3(atGodot.X, feetY + StepUp, atGodot.Z);
        var to = new Vector3(atGodot.X, Mathf.Min(atGodot.Y, feetY) - 2f, atGodot.Z);
        var q = PhysicsRayQueryParameters3D.Create(from, to, WorldCollisionLayer);
        q.Exclude = new Godot.Collections.Array<Rid> { _selfBody.GetRid() };
        var hit = space.IntersectRay(q);
        if (hit.Count == 0) return float.NegativeInfinity;
        if (((Vector3)hit["normal"]).Y < 0.5f) return float.NegativeInfinity;
        return ((Vector3)hit["position"]).Y;
    }

    private bool CapsuleOverlaps()
    {
        if (_selfBody == null || _selfCapsule == null || NoClip) return false;
        var space = GetWorld3D().DirectSpaceState;
        var t = _selfBody.GlobalTransform;
        t.Origin += t.Basis * new Vector3(0, _selfCapsuleOffsetY, 0);
        var q = new PhysicsShapeQueryParameters3D
        { Shape = _selfCapsule, Transform = t, CollisionMask = WorldCollisionLayer, Margin = 0f };
        q.Exclude = new Godot.Collections.Array<Rid> { _selfBody.GetRid() };
        return space.IntersectShape(q, 1).Count > 0;
    }

    public bool ObjectsPending { get; private set; } = true;

    internal string AimMapFxCamera(string fxName, Vector3 koNear, float dist, float heightAbove,
        float yawDeg = float.NaN)
    {
        if (_camera == null || _terrain == null) return "no camera";
        int best = -1;
        float bestD2 = float.MaxValue;
        var want = _terrain.KoToWorld(koNear.X, koNear.Y, koNear.Z);
        for (int i = 0; i < _mapFx.Count; i++)
        {
            if (_mapFx[i].Fx != fxName) continue;
            float d2 = _mapFx[i].Origin.DistanceSquaredTo(want);
            if (d2 < bestD2) { bestD2 = d2; best = i; }
        }
        if (best < 0) return $"no map fx named {fxName}";

        var info = _mapFx[best];
        string where = AimTestCamera(info.Origin, dist, heightAbove, yawDeg);
        return $"fx[{best}] {info.Fx} ko=({info.KoPos.X:F1},{info.KoPos.Z:F1}) " +
               $"visible={info.Node.Visible} {where} cull={MapFxCullDist:F0}{DescribeFxParts(info.Node)}";
    }

    internal string AimTestCameraKo(Vector3 koPos, float dist, float heightAbove, float yawDeg = float.NaN)
    {
        if (_camera == null || _terrain == null) return "no camera";
        return AimTestCamera(_terrain.KoToWorld(koPos.X, koPos.Y, koPos.Z), dist, heightAbove, yawDeg);
    }

    private string AimTestCamera(Vector3 target, float dist, float heightAbove, float yawDeg)
    {
        Vector3 dir;
        if (float.IsNaN(yawDeg))
        {
            dir = _camera.GlobalPosition - target;
            dir.Y = 0f;
            dir = dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector3.Back;
        }
        else
        {
            float yaw = Mathf.DegToRad(yawDeg);
            dir = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        }
        _camera.GlobalPosition = target + dir * dist + Vector3.Up * heightAbove;
        _camera.LookAt(target, Vector3.Up);
        _fxCullAccum = FxCullInterval;
        CullMapFx();
        var screen = _camera.UnprojectPosition(target);
        return $"camDist={_camera.GlobalPosition.DistanceTo(target):F1} screen=({screen.X:F0},{screen.Y:F0})";
    }

    private static string DescribeFxParts(Node node)
    {
        var sb = new System.Text.StringBuilder();
        void Visit(Node n)
        {
            if (n is MeshInstance3D mi)
            {
                sb.Append($" | {n.Name} vis={mi.Visible} aabb={mi.GetAabb().Size.X:F2}x{mi.GetAabb().Size.Y:F2}");
                if (mi.MaterialOverride is StandardMaterial3D m)
                    sb.Append($" blend={m.BlendMode} tr={m.Transparency} depth={m.DepthDrawMode} " +
                              $"noZTest={m.NoDepthTest} prio={m.RenderPriority} albedo={m.AlbedoColor} " +
                              $"tex={(m.AlbedoTexture != null ? m.AlbedoTexture.GetSize().ToString() : "none")}");
            }
            foreach (Node c in n.GetChildren()) Visit(c);
        }
        Visit(node);
        return sb.ToString();
    }

    private async System.Threading.Tasks.Task BuildObjects()
    {
        ObjectsPending = true;
        if (_terrain == null) { ObjectsPending = false; return; }
        var objs = KoObjects.LoadJson(_terrain.ZoneStem);
        if (objs == null) { ObjectsPending = false; return; }

        _objRoot = new Node3D { Name = "Objects" };
        AddChild(_objRoot);
        _objects.Clear();
        _objBuckets.Clear();
        _objInstRoot = null;
        KoTextureAnim.Reset();
        _pickedObject = null; ClearPickCandidates();
        foreach (var o in objs.Items)
            _objects.Add(new ObjInfo
            {
                Name = o.Name, KoPos = o.KoPos, KoRot = o.Rot, Scale = o.Scale,
                Origin = _terrain.KoToWorld(o.KoPos.X, o.KoPos.Y, o.KoPos.Z),
                EventId = o.EventId, EventType = o.EventType, Belong = o.Belong, NpcId = o.NpcId,
            });

        RebuildWarpGates();
        RebuildAnvils();
        for (int i = 0; i < _objects.Count; i++)
        {
            BuildObjectSlot(_objects[i]);
            if ((i + 1) % ObjBuildBatch == 0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!Alive) return;
            }
        }
        await BuildObjectInstances();
        if (!Alive) return;
        ObjectsPending = false;
    }

    private void BuildObjectSlot(ObjInfo o)
    {
        if (o.Built || _objRoot == null || _terrain == null) return;
        o.Built = true;
        string key = SafeModelName(o.Name);
        Diag.Step($"object {key}");
        var scene = ResolveObjectScene(key);
        var q = new Quaternion(o.KoRot.X, -o.KoRot.Y, -o.KoRot.Z, o.KoRot.W).Normalized();
        var localBasis = new Basis(q).Scaled(o.Scale);

        if (scene != null && ObjectNeedsCompleteScene(key, scene))
        {
            var instance = scene.Instantiate<Node3D>();
            ConfigureKoObjectMaterialsOnce(key, instance);
            ItemShineLight.MarkScenery(instance);
            instance.Transform = new Transform3D(_terrain.Transform.Basis * localBasis, o.Origin);
            ConfigureAnimatedObject(instance);
            if (FindFirst<AnimationPlayer>(instance) is { } animation)
            {
                var names = animation.GetAnimationList();
                if (names.Length > 0) animation.Play(names[0]);
            }
            _objRoot.AddChild(instance);
            o.Mi = FindFirst<MeshInstance3D>(instance);
            return;
        }

        var mesh = ResolveObjectMesh(key);
        if (mesh == null)
        {
            // These placements name meshes absent from retail's object.src — unbakeable, and retail
            // draws nothing for them either.
            if (_objMissing.Add(key))
                GD.Print($"[objects] no model for '{key}' — not in object.src, drawing nothing");
            return;
        }

        var xform = new Transform3D(_terrain.Transform.Basis * localBasis, o.Origin);
        var asz = mesh.GetAabb().Size;
        bool large = Mathf.Max(asz.X * Mathf.Abs(o.Scale.X), asz.Z * Mathf.Abs(o.Scale.Z)) >= LargeObjectFootprint;

        if (o.EventId <= 0)
        {
            o.InstMesh = mesh;
            o.InstXform = xform;
            var cell = (key, Mathf.FloorToInt(o.Origin.X / ObjInstanceCell),
                             Mathf.FloorToInt(o.Origin.Z / ObjInstanceCell), large);
            if (!_objBuckets.TryGetValue(cell, out var bucket))
                _objBuckets[cell] = bucket = new List<ObjInfo>();
            bucket.Add(o);
            return;
        }

        BuildObjectNode(o, mesh, xform, large);
    }

    private void BuildObjectNode(ObjInfo o, Mesh mesh, Transform3D xform, bool large)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            Transform = xform,
            Layers = 1u | ItemShineLight.SceneryLayer,
            VisibilityRangeEnd = (large ? LargeObjectCullDist : SmallObjectCullDist) * Config.ViewDistance,
            VisibilityRangeEndMargin = ObjectCullFade,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
        if (large) mi.LodBias = LargeObjectLodBias;
        else mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        mi.AddToGroup(large ? ObjCullLargeGroup : ObjCullSmallGroup);
        o.InstMesh = null;
        o.Mi = mi;
        _objRoot!.AddChild(mi);
    }

    private async System.Threading.Tasks.Task BuildObjectInstances()
    {
        if (_objRoot == null || _objBuckets.Count == 0) return;
        _objInstRoot = new Node3D { Name = "ObjectInstances" };
        _objRoot.AddChild(_objInstRoot);

        int made = 0, instanced = 0, loose = 0, n = 0;
        foreach (var (cell, bucket) in _objBuckets)
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
                    VisibilityRangeEnd = (large ? LargeObjectCullDist : SmallObjectCullDist)
                                         * Config.ViewDistance + ObjInstanceCullPad,
                    VisibilityRangeEndMargin = ObjectCullFade,
                    VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                };
                mmi.AddToGroup(large ? ObjCullLargeGroup : ObjCullSmallGroup);
                _objInstRoot.AddChild(mmi);
                made++;
                instanced += bucket.Count;
            }

            if (++n % ObjInstanceBatch == 0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!Alive) return;
            }
        }
        _objBuckets.Clear();
        int surf = 0;
        foreach (var c in _objInstRoot.GetChildren())
            if (c is MultiMeshInstance3D { Multimesh.Mesh: { } m }) surf += m.GetSurfaceCount();
        GD.Print($"[objects] instanced {instanced} placements into {made} multimeshes "
                 + $"({surf} surfaces), {loose} left loose");
    }

    private readonly Dictionary<string, bool> _objNeedsScene = new();

    private bool ObjectNeedsCompleteScene(string key, PackedScene scene)
    {
        if (_objNeedsScene.TryGetValue(key, out bool need)) return need;
        var probe = scene.Instantiate<Node3D>();
        need = FindFirst<AnimationPlayer>(probe) != null || RequiresCompleteObjectScene(probe);
        probe.QueueFree();
        _objNeedsScene[key] = need;
        return need;
    }

    private readonly HashSet<string> _koMaterialsApplied = new();

    private void ConfigureKoObjectMaterialsOnce(string key, Node instance)
    {
        if (_koMaterialsApplied.Add(key)) ConfigureKoObjectMaterials(instance);
    }

    private static void ConfigureAnimatedObject(Node root)
    {
        foreach (var mi in FindAll<MeshInstance3D>(root))
        {
            mi.VisibilityRangeEnd = LargeObjectCullDist;
            mi.VisibilityRangeEndMargin = ObjectCullFade;
            mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        }
    }

    private StaticBody3D? _objCollision;
    private void BuildObjectCollision()
    {
        if (_terrain == null) return;
        var coll = KoCollision.Load(_terrain.ZoneStem);
        if (coll == null)
        {
            return;
        }
        var verts = new Vector3[coll.Verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var v = coll.Verts[i];
            verts[i] = _terrain.KoToWorld(v.X, v.Y, v.Z);
        }
        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(verts);
        _objCollision = new StaticBody3D { Name = "ObjectCollision" };
        _objCollision.AddChild(new CollisionShape3D { Shape = shape });
        AddChild(_objCollision);
    }

    private void BuildFxPlacements()
    {
        if (_terrain == null) return;
        _mapFx.Clear(); _mapFxIndex.Clear();
        _pickedFx = null; ClearPickCandidates();
        var fxs = KoFxPlacements.LoadJson(_terrain.ZoneStem);
        if (fxs == null || fxs.Items.Length == 0)
        {
            return;
        }

        var root = new Node3D { Name = "MapFx" };
        AddChild(root);
        int placed = 0, missing = 0;
        foreach (var pl in fxs.Items)
        {
            var origin = _terrain.KoToWorld(pl.KoPos.X, pl.KoPos.Y, pl.KoPos.Z);
            var node = Fx.Spawn(pl.Fx, root, origin);
            if (node == null) { missing++; continue; }
            var q = new Quaternion(pl.Rot.X, -pl.Rot.Y, -pl.Rot.Z, pl.Rot.W);
            if (q.LengthSquared() > 0.0001f)
                node.Quaternion = q.Normalized();
            if (pl.Scale > 0f && !Mathf.IsEqualApprox(pl.Scale, 1f))
                node.Scale = Vector3.One * pl.Scale;
            node.Visible = false;
            node.ProcessMode = ProcessModeEnum.Disabled;
            AttachFxAmbience(node, pl.Fx);
            FxLampLight.Attach(node, pl.Fx);
            _mapFxIndex[node] = _mapFx.Count;
            _mapFx.Add(new FxInfo
            {
                Node = node, Fx = pl.Fx, KoPos = pl.KoPos, KoRot = pl.Rot,
                Scale = pl.Scale > 0f ? pl.Scale : 1f, Origin = origin,
                SunShafts = pl.Fx.Contains(SunShaftFxMarker, StringComparison.OrdinalIgnoreCase)
                    ? FindAll<GeometryInstance3D>(node).ToArray()
                    : null,
            });
            placed++;
        }
        CullMapFx(int.MaxValue);
    }

    private void CullMapFx() => CullMapFx(MapFxWakeBudget);

    private void CullMapFx(int wakeBudget)
    {
        if (_mapFx.Count == 0 || _camera == null) return;
        var cp = _camera.GlobalPosition;
        float far2 = MapFxCullDist * MapFxCullDist;
        float near2 = MapFxAlwaysRadius * MapFxAlwaysRadius;
        float sleepFar2 = MapFxSleepDist * MapFxSleepDist;
        var planes = _camera.GetFrustum();
        float shine = _sky?.SunShine ?? 1f;
        _fxWakeQueue.Clear();
        for (int i = 0; i < _mapFx.Count; i++)
        {
            var info = _mapFx[i];
            var node = info.Node;
            if (!GodotObject.IsInstanceValid(node)) continue;
            var pos = info.Origin;
            float d2 = cp.DistanceSquaredTo(pos);
            bool awake = node.Visible;
            float margin = awake ? MapFxFrustumSleepMargin : MapFxFrustumMargin;
            bool inFrustum = true;
            for (int j = 0; j < planes.Count; j++)
                if (planes[j].DistanceTo(pos) > margin) { inFrustum = false; break; }
            float far = awake ? sleepFar2 : far2;
            bool show = Config.FxAmbient
                && ((d2 <= far && (d2 <= near2 || inFrustum)) || node == _pickedFx?.Node);
            if (info.SunShafts != null)
            {
                show &= shine > SunShaftMinShine;
                if (show && !Mathf.IsEqualApprox(shine, info.ShaftFade))
                {
                    info.ShaftFade = shine;
                    foreach (var gi in info.SunShafts)
                        if (GodotObject.IsInstanceValid(gi)) gi.Transparency = 1f - shine;
                }
            }
            if (show == awake) continue;
            if (show)
            {
                _fxWakeQueue.Add((d2, i));
                continue;
            }
            node.Visible = false;
            node.ProcessMode = ProcessModeEnum.Disabled;
            SetFxAmbience(node, false);
        }
        if (_fxWakeQueue.Count == 0) return;
        _fxWakeQueue.Sort((a, b) => a.D2.CompareTo(b.D2));
        int wake = Mathf.Min(wakeBudget, _fxWakeQueue.Count);
        for (int k = 0; k < wake; k++)
        {
            var node = _mapFx[_fxWakeQueue[k].Index].Node;
            node.Visible = true;
            node.ProcessMode = ProcessModeEnum.Inherit;
            SetFxAmbience(node, true);
        }
        if (Diag.SlowLog) GD.Print($"[slow] mapfx woke {wake} of {_fxWakeQueue.Count}");
        _fxWakeQueue.Clear();
    }

    private const float LargeObjectFootprint = 15f;
    private const float LargeObjectLodBias = 16f;
    private const float SmallObjectCullDist = 200f;
    private const float LargeObjectCullDist = 700f;
    private const float ObjectCullFade = 30f;

    private void BuildWater()
    {
        if (_terrain == null) return;
        _water = new Water { Name = "Water" };
        AddChild(_water);
        if (!_water.Build(_terrain, _terrain.ZoneStem))
        {
            _water.QueueFree();
            _water = null;
        }
    }

    private Mesh? ResolveObjectMesh(string key)
    {
        if (_objMeshCache.TryGetValue(key, out var cached)) return cached;

        Mesh? result = null;
        var scene = ResolveObjectScene(key);
        if (scene != null)
        {
            var inst = scene.Instantiate();
            result = FindFirstMesh(inst);
            inst.QueueFree();
        }
        if (result != null)
            ConfigureKoObjectMaterials(result);
        _objMeshCache[key] = result;
        return result;
    }

    private static void ConfigureKoObjectMaterials(Mesh mesh)
    {
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D imported)
                continue;
            string name = imported.ResourceName;
            int marker = name.LastIndexOf("__ko_f", StringComparison.Ordinal);
            if (marker < 0)
                continue;
            int sourceMarker = name.IndexOf("_s", marker + 6, StringComparison.Ordinal);
            int destMarker = sourceMarker < 0 ? -1 : name.IndexOf("_d", sourceMarker + 2, StringComparison.Ordinal);
            int animMarker = destMarker < 0 ? -1 : name.IndexOf("_a", destMarker + 2, StringComparison.Ordinal);
            int destEnd = animMarker < 0 ? name.Length : animMarker;
            if (sourceMarker < 0 || destMarker < 0 ||
                !uint.TryParse(name.AsSpan(marker + 6, sourceMarker - marker - 6),
                    System.Globalization.NumberStyles.HexNumber, null, out uint flags) ||
                !int.TryParse(name.AsSpan(sourceMarker + 2, destMarker - sourceMarker - 2), out int srcBlend) ||
                !int.TryParse(name.AsSpan(destMarker + 2, destEnd - destMarker - 2), out int destBlend))
                continue;

            var material = (StandardMaterial3D)imported.Duplicate();
            material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
            bool alphaBlend = (flags & 0x001) != 0;
            bool noLight = (flags & 0x040) != 0;
            bool additive = srcBlend == 2 && destBlend == 2;
            bool noZWrite = (flags & 0x100) != 0;
            // An additive surface has no opaque interior for a depth pre-pass to stand for: it would
            // write the whole quad and punch the world out behind it — FX_CONTINUATION_HANDOFF §4r.
            if (alphaBlend)
                material.Transparency = noZWrite || additive
                    ? BaseMaterial3D.TransparencyEnum.Alpha
                    : BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
            if (additive)
            {
                material.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                material.DisableFog = true;
            }
            if ((flags & 0x004) != 0)
                material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            if ((flags & 0x008) != 0)
                material.BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY;
            if ((flags & 0x010) != 0)
                material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
            if (noLight)
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            if (noZWrite)
                material.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
            if ((flags & 0x400) != 0)
                material.NoDepthTest = true;

            var (fps, frameCount) = KoTextureAnim.Parse(name, destMarker + 2);
            if (fps > 0f) KoTextureAnim.Register(material, fps, frameCount);

            mesh.SurfaceSetMaterial(surface, material);
        }
    }

    internal static void ConfigureKoObjectMaterials(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
            ConfigureKoObjectMaterials(mi.Mesh);
        foreach (Node child in node.GetChildren())
            ConfigureKoObjectMaterials(child);
    }

    private static bool RequiresCompleteObjectScene(Node node)
    {
        int meshCount = 0;
        bool pivotedMesh = false;
        void Visit(Node current)
        {
            if (current is MeshInstance3D mi)
            {
                meshCount++;
                if (!mi.Position.IsEqualApprox(Vector3.Zero))
                    pivotedMesh = true;
            }
            foreach (Node child in current.GetChildren())
                Visit(child);
        }
        Visit(node);
        return meshCount > 1 || pivotedMesh;
    }

    private PackedScene? ResolveObjectScene(string key)
    {
        if (_objSceneCache.TryGetValue(key, out var cached)) return cached;
        string resPath = "res://assets/objects/" + key + ".glb";
        var result = ResourceLoader.Exists(resPath) ? ResourceLoader.Load(resPath) as PackedScene : null;
        _objSceneCache[key] = result;
        return result;
    }
}
