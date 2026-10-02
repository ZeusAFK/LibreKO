using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World : Node3D, IWorldContext
{
    private int _myId;
    private int _zone;
    private bool _worldReady;

    internal ChatSystem Chat { get; private set; } = null!;
    internal FloaterSystem Floaters { get; private set; } = null!;

    Node3D IWorldContext.Root => this;
    int IWorldContext.SelfCharId => _myId;
    Node3D? IWorldContext.BodyOf(int charId) =>
        charId == _myId ? _self : _ents.TryGetValue(charId, out var e) ? e.Body : null;
    float IWorldContext.HeadHeight(int charId) => HeadHeightOf(charId);
    double IWorldContext.Now => Now();

    private bool Alive => IsInstanceValid(this) && !IsQueuedForDeletion();

    public override void _Ready()
    {
        GD.Print("[enter] world scene instantiated");
        _ = Diag.Guard("world-load", LoadWorld);
    }

    private double _loadStartedAt;

    private async System.Threading.Tasks.Task LoadWorld()
    {
        Ui.MenuScale(false);
        _entities = GetNode<Node3D>("Entities");
        _camera = GetNode<Camera3D>("Camera");

        var info = Net.I.LastEnter;
        _myId = info.CharId;
        _zone = info.Zone;
        _myKoX = info.X; _myKoZ = info.Z; _myKoY = info.Y;
        _isGm = info.Authority == Net.GameMasterAuthority;
        _collisionsOff = _isGm;

        _loadStartedAt = Now();
        BuildLoading();
        GlyphWarmer.Warm(this);
        if (!await LoadStep("Preparing…", 0.04f, "Lighting and sky")) return;

        BuildEnvironment();
        Config.ApplyGraphicsToViewport();
        Cape.Enabled = Config.Capes;
        Config.GraphicsChanged += OnGraphicsChanged;
        Config.EffectsChanged += CullMapFx;
        KeyBinds.Changed += BuildHotkeys;

        string zoneLabel = $"Zone {info.Zone}";
        foreach (var z in ZoneCatalog.All) if (z.Id == info.Zone) { zoneLabel = $"{z.Name} (zone {z.Id})"; break; }
        if (!await LoadStep("Loading terrain…", 0.15f, zoneLabel)) return;

        _terrain = new Terrain { Name = "Terrain" };
        AddChild(_terrain);
        var ground = GetNodeOrNull<MeshInstance3D>("Ground");
        bool hasTerrain = _terrain.Build(info.Zone);
        if (hasTerrain) ground?.QueueFree();
        else { _terrain = null; if (ground != null) ground.Position = Coord.ToGodot(info.X, 0, info.Z); }
        FxBillboard.GroundHeight = hasTerrain ? FxGroundHeight : null;
        if (!await LoadStep("Loading world objects…", 0.45f,
            hasTerrain ? "Objects · collision · water · map FX" : "Flat placeholder (zone not baked)")) return;

        if (hasTerrain)
        {
            GD.Print("[load] world objects: placements");
            await BuildObjects();

            if (!Alive) return;
            GD.Print("[load] world objects: collision");
            BuildObjectCollision();
            RegroundEntities();
            GD.Print("[load] world objects: water");
            BuildWater();
            GD.Print("[load] world objects: ambient FX");
            BuildFxPlacements();
            WarmRoleFx();
            GD.Print("[load] world objects: complete");
        }
        if (!await LoadStep("Loading character…", 0.70f,
            $"{(info.Name.Length > 0 ? info.Name : "You")} · Lv {Sheet.Level} · {ClassName(info.Class)}")) return;

        string selfName = info.Name.Length > 0 ? info.Name : "You";
        GD.Print("[load] character: body scene");
        PackedScene? selfScene = ResolvePlayerScene(info.Race);
        Node3D selfVisual;
        if (selfScene != null)
        {
            GD.Print("[load] character: rig");
            (selfVisual, _selfAnim) = MakeAnimatedEntity(selfScene, selfName, 1f);
            _selfFlinch = Flinch.Attach(selfVisual);
            CaptureSelfDefaults(selfVisual);
            GD.Print("[load] character: equipment");
            GraftEquipment(selfVisual, info.Race, info.Face, info.Gear, info.Hair, Net.I.HelmetHidden);
            GD.Print("[load] character: complete");
        }
        else
        {
            selfVisual = MakeEntity(new Color(1f, 0.82f, 0.3f), selfName);
            selfVisual.Position = new Vector3(0, CapsuleHalf, 0);
        }
        _selfLift = 0f;
        _selfBody = MakePlayerBody(selfVisual);
        _self = _selfBody;
        ApplyCollisionPolicy();
        var selfCs = FindFirst<CollisionShape3D>(_selfBody);
        _selfCapsule = selfCs?.Shape as CapsuleShape3D;
        _selfCapsuleOffsetY = selfCs?.Position.Y ?? 0.85f;
        _self.Position = GroundPos(info.X, info.Z, info.Y, _selfLift);
        _lastFreePos = _self.Position;
        _entities.AddChild(_self);
        ApplySelfClan(MyClan.InClan ? MyClan.Name : "");
        if (selfScene != null)
        {
            AttachWeapons(_self, info.Gear);
            AttachClanGauntlet(_self, info.Race, MyClan.InClan ? MyClan.Grade : 0, MyClan.InClan ? MyClan.Ranking : 0);
            _selfWingAnims = AttachWings(_self, info.Gear, info.Race, _zone, shineShadow: true);
            System.Array.Clear(_selfWingClips);
            AttachHandFx(_self, info.Gear, info.Race, _zone);
        }
        _selfVisual = selfVisual;
        DressSelfCape();
        _selfStandingVisualTransform = selfVisual.Transform;
        _lastKoX = info.X; _lastKoZ = info.Z;

        if (!await LoadStep("Entering world…", 0.92f, "Spawning nearby players & monsters")) return;

        Net.I.EntitySpawnEvent += OnSpawn;
        Net.I.EntityMoveEvent += OnMove;
        Net.I.EntityRotateEvent += OnRotate;
        Net.I.EntityOutEvent += OnOut;
        Net.I.EntityHpEvent += OnEntityHp;
        Net.I.EntityHpSyncEvent += OnEntityHpSync;
        Net.I.AttackEvent += OnAttack;
        Net.I.MagicEvent += OnMagic;
        Net.I.BuffExpiredEvent += OnBuffExpired;
        Net.I.DeadEvent += OnDead;
        Net.I.SelfHpEvent += OnSelfHp;
        Net.I.SelfMpEvent += OnSelfMp;
        Net.I.RegeneEvent += OnRegene;
        Net.I.LookChangeEvent += OnLookChange;

        Net.I.ReplayKnownEntities(OnSpawn);

        BuildFacets();
        BuildEscapeStack();
        BuildHotkeys();
        ApplyHudTheme();
        GlyphWarmer.Warm(this);
        Floaters?.WarmText();

        if (_selfAnim != null) { PlayClipOn(_selfAnim, ref _selfClip, "idle"); _selfAnim.Advance(0.0); }
        _worldReady = true;
        if (Net.I.ConsumeZoneChange()) Net.I.SendZoneEnterAck();
        else Net.I.SendReady();

        if (!await LoadStep("Syncing server time…", 0.96f, $"{_ents.Count} entities nearby")) return;
        await WaitForServerEnvironment(2.5);

        if (!Alive) return;

        if (!await LoadStep("Ready", 1.0f, "Welcome to LibreKO")) return;
        HideLoading();

    }

    private async System.Threading.Tasks.Task WaitForServerEnvironment(double timeoutSec)
    {
        double waited = 0.0;
        while (Net.I.LastTime is null && waited < timeoutSec)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!Alive) return;
            waited += GetProcessDeltaTime();
        }
        if (Net.I.LastTime is { } t) OnServerTime(t.Hour, t.Minute);
    }

    public override void _Input(InputEvent ev)
    {
        if (!_worldReady || Net.I.ReconnectBlocking) return;
        if (ev is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
            } leftPress)
        {
            _autoMoveForward = false;
            FocusWhisperAt(leftPress.Position);
        }

        if (ev is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (_hudEditMode)
            {
                if (k.Keycode == Key.Escape)
                {
                    SetHudEditMode(false);
                    GetViewport().SetInputAsHandled();
                }
                return;
            }
            if (Chat.IsActive)
            {
                if (k.Keycode == Key.Escape) { Chat.Close(); GetViewport().SetInputAsHandled(); }
                return;
            }
            if (_buyAmountShown && k.Keycode is Key.Escape or Key.Enter or Key.KpEnter)
            {
                if (k.Keycode == Key.Escape) CloseBuyAmount(); else ConfirmBuyAmount();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (k.Keycode == Key.Escape && TryMinimizeFocusedWhisper())
            { GetViewport().SetInputAsHandled(); return; }
            if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return;
            if (k.Keycode is Key.Enter or Key.KpEnter) { Chat.Open(); GetViewport().SetInputAsHandled(); return; }
            NoteMoveKey(k);
            if (_hotkeys.TryGetValue(KeyChord.From(k), out var press))
            { press(); GetViewport().SetInputAsHandled(); return; }
            if (k.Keycode == Key.Escape)
            { HandleEscape(); GetViewport().SetInputAsHandled(); }
        }
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventJoypadButton { Pressed: true } jb)
        {
            if (_padHotkeys.TryGetValue(new PadChord(jb.ButtonIndex, KeyBinds.ActiveMod()), out var padPress))
            { padPress(); GetViewport().SetInputAsHandled(); }
            return;
        }
        if (ev is InputEventMouseButton mb)
        {
            bool overUi = GetViewport().GuiGetHoveredControl() != null;
            float maxDist = _infoShown ? InfoMaxDist : MaxDist;
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp && !overUi)
                _camDist = Mathf.Clamp(_camDist * 0.9f, MinDist, maxDist);
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown && !overUi)
                _camDist = Mathf.Clamp(_camDist * 1.12f, MinDist, maxDist);
            else if (HandleProfilePointer(mb, overUi)) return;
        }
        else if (ev is InputEventMouseMotion mm)
            HandleProfilePointer(mm, GetViewport().GuiGetHoveredControl() != null);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_worldReady || Net.I.ReconnectBlocking) return;
        UpdateHeldMoveTarget(delta);
        HandleInput(delta);
    }

    public override void _Process(double delta)
    {
        if (!_worldReady) return;
        using var worldScope = Perf.Measure(Perf.Section.World);

        using (Perf.Measure(Perf.Section.Spawn))
        {
            ProcessSpawnQueue();
            TickNestTimer();
            TickQuestToast(delta);
        }

        float dt = (float)delta;
        double nowSec = Time.GetTicksMsec() / 1000.0;
        Vector3 selfPos = _self.Position;
        Vector3 camPos = _camera != null ? _camera.GlobalPosition : selfPos;
        float nameDist2 = NameTagDist * NameTagDist;
        var entityScope = Perf.Measure(Perf.Section.Entities);
        StealthTick(selfPos);
        RefreshFrustum();
        RankEntityAnimation();
        _animFull = _animMid = _animFar = _animOff = _closeEntities = 0;
        foreach (var e in _ents.Values)
        {
            float step = dt;
            if (e.Far)
            {
                e.FarAccum += delta;
                if (e.FarAccum < FarEntityInterval) continue;
                step = (float)e.FarAccum;
                e.FarAccum = 0;
            }
            Vector3 pos = e.Body.Position;
            var moveScope = Perf.Measure(Perf.Section.EntMove);
            float moveStep = step;
            if (e.JustEntered)
            {
                moveStep += e.MoveAccum;
                e.MoveAccum = 0f;
            }
            else if (!e.Far && (!e.OnScreen || e.AnimEvery >= Config.AnimFarEvery))
            {
                int moveEvery = e.OnScreen ? Config.MoveFarEvery : OffScreenMoveEvery;
                e.MoveAccum += step;
                if (++e.MoveFrames % moveEvery != 0) moveStep = 0f;
                else { moveStep = e.MoveAccum; e.MoveAccum = 0f; }
            }
            if (moveStep > 0f && !Perf.SkipMove && e.HasTarget && e.Speed > 0f && !e.Dead)
            {
                var moved = pos.MoveToward(e.Target, e.Speed * moveStep);
                if (moved != pos) { pos = moved; e.Body.Position = pos; }
            }

            moveScope.Dispose();
            using (Perf.Measure(Perf.Section.EntFacing)) TickEntityFacing(e, step);
            using (Perf.Measure(Perf.Section.EntCollider)) SyncEntityCollider(e, pos, selfPos);

            using (Perf.Measure(Perf.Section.EntLod)) UpdateEntityAnimLod(e, camPos, pos);
            using (Perf.Measure(Perf.Section.EntAudio)) AudioEntityStepTick(e, nowSec, selfPos, pos);

            var plateScope = Perf.Measure(Perf.Section.EntPlates);
            if (e.NameTag != null)
            {
                bool near = !Perf.SkipPlates && pos.DistanceSquaredTo(selfPos) <= nameDist2;
                SyncEntityPlates(e, near);
                if (e.Plate != null) e.Plate.SetVisible(near);
                else if (near != e.NameTag.Visible) e.NameTag.Visible = near;
            }

            plateScope.Dispose();
            if (e.Anim != null && !e.AnimPaused)
            {
                if (e.Dead) {  }
                else if (nowSec < e.ActionUntil) {  }
                else
                {
                    if (e.ActionClip != null) { e.ActionClip = null; e.Clip = null; }
                    bool moving = EntityMoving(e, pos);
                    PlayClip(e, moving
                        ? e.Backwards ? "walk_reverse" : e.Speed >= RunThreshold ? "run" : "walk"
                        : e.Sitting ? "sit" : "idle");
                }
                if (e.AnimThrottled)
                {
                    e.AnimAccum += dt;
                    e.AnimFrames++;
                    if (e.JustEntered || (e.AnimFrames >= e.AnimEvery && e.AnimAccum >= e.AnimStep))
                    {
                        using (Perf.Measure(Perf.Section.EntStep))
                        {
                            if (e.Crowd != null) e.Crowd.Step(e.AnimAccum, e.OnScreen);
                            else e.Anim.Advance(e.AnimAccum);
                        }
                        if (e.WingAnims != null && e.OnScreen)
                            using (Perf.Measure(Perf.Section.EntWings))
                                foreach (var wing in e.WingAnims)
                                    wing?.Advance(e.AnimAccum);
                        e.AnimAccum = 0;
                        e.AnimFrames = 0;
                    }
                }
            }
        }
        Perf.EntityTiers = (_ents.Count, _animFull, _animMid, _animFar, _animOff);
        Perf.CloseEntities = _closeEntities;
        entityScope.Dispose();

        var selfScope = Perf.Measure(Perf.Section.SelfAnim);
        if (_selfAnim != null && !_selfDead)
        {
            if (nowSec < _selfActionUntil
                && _walkKeyHeld
                && !IsRootedByCast()
                && (_selfActionRank != ActionRankSkill || _walkPressedEdge))
            {
                _selfActionUntil = 0;
                _selfClip = null;
            }

            if (nowSec < _selfActionUntil) {  }
            else
            {
                if (_selfActionUntil != 0) { _selfActionUntil = 0; _selfClip = null; }
                string locomotion = !_selfMoving
                    ? _selfSitting ? "sit" : "idle"
                    : _selfMovingBackward
                        ? "walk_reverse"
                        : _running ? "run" : "walk";
                bool combatStance = locomotion == "idle" && SelfCombatStanceReady();
                var stanceGear = combatStance ? SelfGear() : null;
                PlayClipOn(
                    _selfAnim,
                    ref _selfClip,
                    locomotion,
                    combatStance ? StanceIdleClips(stanceGear) : null,
                    combatStance ? StanceIdleSlot(stanceGear) : -1);
            }
        }

        WingTick();
        CombatStanceTick();
        ApplySelfAnimSpeed();
        AudioFootstepTick();
        selfScope.Dispose();

        using (Perf.Measure(Perf.Section.Combat))
        {
            CombatTick(nowSec);
            TickPadTriggers();
            AreaCastTick(delta, nowSec);
            LootTick(nowSec);
            StallSignTick();
            GatherTick(nowSec);
            NpcTick(delta);
            _touchInteractionAccum += delta;
            if (_touchInteractionAccum >= TouchInteractionInterval)
            {
                _touchInteractionAccum %= TouchInteractionInterval;
                UpdateTouchInteractionVisibility();
            }
            PvpTick(nowSec);
            Chat.TickBubbles(nowSec);
            CastMoveCancelTick();
            WarpGateTick();
            AnvilTick();
            PieceChangeTick(delta);
            KoTextureAnim.Tick(delta);
        }

        using (Perf.Measure(Perf.Section.Selection))
        {
            UpdateSelectionRing();
            TargetHpPollTick(nowSec);
            UpdateTargetHud();
            UpdateInfoPanel();
        }

        using (Perf.Measure(Perf.Section.Camera)) UpdateCamera(delta);

        using (Perf.Measure(Perf.Section.Hud))
        {
            Floaters?.Tick(nowSec);
            CursorTick(delta);
            _mapHudAccum += delta;
            if (_mapHudAccum >= MapHudInterval) { _mapHudAccum = 0; UpdateMiniMap(); UpdateFullMap(); }
            UpdateInventoryTooltip();
            UpdateDeletePrompt();
        }

        using (Perf.Measure(Perf.Section.Sky)) UpdateSky(delta);

        using (Perf.Measure(Perf.Section.MapFx))
        {
            _fxCullAccum += delta;
            if (_fxCullAccum >= FxCullInterval) { _fxCullAccum = 0; CullMapFx(); }
            _pickMarkerAccum += delta;
            if (_pickMarkerAccum >= PickMarkerInterval) { _pickMarkerAccum = 0; RefreshPickMarkers(); }
        }
    }

    private (float x, float z) WorldToKo(Vector3 world)
        => _terrain != null ? _terrain.WorldToKo(world) : Coord.ToKo(world.X, world.Z);

    private float? FxGroundHeight(float worldX, float worldZ)
    {
        if (_terrain == null) return null;
        var (koX, koZ) = _terrain.WorldToKo(new Vector3(worldX, 0f, worldZ));
        if (!_terrain.SampleHeight(koX, koZ, out float gy)) return null;
        return GroundPos(koX, koZ, gy, 0f).Y;
    }
    public override void _ExitTree()
    {
        HudLayout.EditMode = false;
        GameCursor.Disable();
        Net.I.EntitySpawnEvent -= OnSpawn;
        Net.I.EntityMoveEvent -= OnMove;
        Net.I.EntityRotateEvent -= OnRotate;
        Net.I.EntityOutEvent -= OnOut;
        Net.I.EntityHpEvent -= OnEntityHp;
        Net.I.EntityHpSyncEvent -= OnEntityHpSync;
        Net.I.AttackEvent -= OnAttack;
        Net.I.MagicEvent -= OnMagic;
        Net.I.BuffExpiredEvent -= OnBuffExpired;
        Net.I.DeadEvent -= OnDead;
        Net.I.SelfHpEvent -= OnSelfHp;
        Net.I.SelfMpEvent -= OnSelfMp;
        Net.I.RegeneEvent -= OnRegene;
        GetViewport().SizeChanged -= RefreshInventoryUI;
        Net.I.ItemMoveResultEvent -= OnItemMoveResult;
        Net.I.ItemRemoveResultEvent -= OnItemRemoveResult;
        Net.I.ItemStatsEvent -= OnItemStats;
        Net.I.InventorySlotEvent -= OnInventorySlotUpdate;
        Net.I.ItemGainedEvent -= OnItemGained;
        Net.I.InventoryGridRefreshEvent -= OnInventoryGridRefresh;
        Net.I.GoldChangeEvent -= OnInventoryGoldChange;
        Net.I.LookChangeEvent -= OnLookChange;
        TeardownFacets();
        Net.I.TimeEvent -= OnServerTime;
        Net.I.WarpEvent -= OnWarp;
        Net.I.SkillDataEvent -= OnSkillData;
        Config.GraphicsChanged -= OnGraphicsChanged;
        Config.EffectsChanged -= CullMapFx;
        KeyBinds.Changed -= BuildHotkeys;
    }
}
