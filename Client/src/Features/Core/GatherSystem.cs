using Godot;

namespace LibreKO.Features.Core;

internal sealed class GatherSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly MiningSession mining;
    private readonly SelfSession self;
    private readonly IMiningRequests miningRequests;
    private readonly EntityActionSystem entityAction;
    private readonly AudioSystem audio;
    private readonly CombatFxSystem combatFx;
    private readonly EntitySystem entities;

    public GatherSystem(IWorldContext context, MiningSession mining, SelfSession self, IMiningRequests miningRequests, SelfAvatar avatar, EntityActionSystem entityAction, AudioSystem audio, CombatFxSystem combatFx, EntitySystem entities)
    {
        this.context = context;
        this.mining = mining;
        this.self = self;
        this.miningRequests = miningRequests;
        this.avatar = avatar;
        this.entityAction = entityAction;
        this.audio = audio;
        this.combatFx = combatFx;
        this.entities = entities;
    }

    public void Build()
    {
        entities.EntitySpawned += OnEntitySpawned;
        entities.EntityRefreshed += OnEntityRefreshed;
    }

    public void Dispose()
    {
        entities.EntitySpawned -= OnEntitySpawned;
        entities.EntityRefreshed -= OnEntityRefreshed;
    }

    private void OnEntitySpawned(EntityView e, EntitySnapshot info)
    {
        if (e.Gathering && !info.Dead) BeginRemoteGather(info.Id, e.GatherFishing);
    }

    private void OnEntityRefreshed(EntityView e, EntitySnapshot info)
    {
        if (info.Gathering) BeginRemoteGather(info.Id, info.GatherFishing);
        else if (e.Gathering) EndRemoteGather(info.Id);
    }

    private const int KindPickaxe = 61;
    private const int BoxCorners = 8;
    private const int KindFishingRod = 63;
    private const int MiningClip = 63;
    private const int FishingCastClip = 151;
    private const int FishingWaitClip = 152;
    private const int FishingWaitFx = 30732;
    internal const int MiningStrikeFx = 13080;
    internal const float MiningStrikeReach = 1.1f;
    private const double GatherActionRearm = 0.5;
    private const int RightHandGearSlot = 6;
    private const int LeftHandGearSlot = 7;
    private const double GatherInterval = 5.0;
    private const float GatherFxHeight = 1.2f;

    private static readonly string[] MiningClips =
    { "attack_Twoblunt0_A", "attack_Twoblunt1_A", "attack0", "attack" };

    private static readonly string[] FishingCastClips = { "fishing1", "fishing2", "breath" };

    private static readonly string[] FishingWaitClips = { "fishing2", "fishing1", "breath" };

    private readonly Dictionary<int, bool> gatherers = new();

    private readonly HashSet<int> fishingCast = new();

    internal bool Gathering { get; private set; }
    internal bool GatherFishing { get; private set; }
    private int gatherToolSlot = -1;
    private double gatherNextAttempt;
    private Node3D? gatherFx;

    internal void GatherInit()
    {
        mining.GatherStartEvent += OnGatherStart;
        mining.GatherResultEvent += OnGatherResult;
        mining.GatherStopEvent += OnGatherStop;
        mining.ItemDurationEvent += OnItemDuration;
    }

    internal void GatherDispose()
    {
        mining.GatherStartEvent -= OnGatherStart;
        mining.GatherResultEvent -= OnGatherResult;
        mining.GatherStopEvent -= OnGatherStop;
        mining.ItemDurationEvent -= OnItemDuration;
        gatherers.Clear();
        fishingCast.Clear();
    }

    private const int TextNoPickaxe = 18103;
    private const int TextNoFishingRod = 18112;
    private const int TextGatherBagFull = 18105;
    private const int TextPickaxeBroken = 18106;
    private const int TextFishingRodBroken = 18113;
    private const int TextGatherTransformed = 10353;

    private int FindGatherTool(int kind)
    {
        if (self.Inventory == null) return -1;

        foreach (int slot in new[] { InventoryConstants.RightHand, InventoryConstants.LeftHand })
        {
            if (self.Inventory.Length <= slot) continue;
            if (ItemData.Get(self.Inventory[slot].ItemId)?.Kind == kind) return slot;
        }

        return -1;
    }

    private bool BackpackFull()
    {
        for (int i = 0; i < InventoryConstants.HaveMax; i++)
        {
            int slot = InventoryConstants.InventoryStart + i;
            if (slot >= self.Inventory.Length || self.Inventory[slot].IsEmpty) return false;
        }
        return true;
    }

    private string ToolBrokenText(bool fishing) => fishing
    ? GameText.System(TextFishingRodBroken, "Durability of fishing rod becomes 0.")
    : GameText.System(TextPickaxeBroken, "The durability of pickaxe is 0");

    private bool SelfInGatherArea(bool fishing)
    {
        var (mapX, mapZ) = Coord.ToMap(context.Self.Body.GlobalPosition.X, context.Self.Body.GlobalPosition.Z);
        return fishing
            ? GatherZones.IsFishingArea(context.Zone, mapX, mapZ)
            : GatherZones.IsMiningArea(context.Zone, mapX, mapZ);
    }

    internal bool TryStartGather()
    {
        if (Gathering) { StopGather(sendStop: true); return true; }
        if (context.Self.Dead) return false;

        bool fishing = SelfInGatherArea(fishing: true);
        if (!fishing && !SelfInGatherArea(fishing: false)) return false;

        int slot = FindGatherTool(fishing ? KindFishingRod : KindPickaxe);
        if (slot < 0)
        {
            context.Log.Notice(fishing
                ? GameText.System(TextNoFishingRod, "Fishing rod is not equipped.")
                : GameText.System(TextNoPickaxe, "A pickaxe is not equipped"));
            return true;
        }

        if (self.Inventory[slot].Durability <= 0)
        {
            context.Log.Notice(ToolBrokenText(fishing));
            return true;
        }

        if (BackpackFull())
        {
            context.Log.Notice(GameText.System(TextGatherBagFull, "Not enough inventory slot"));
            return true;
        }

        if (self.TransformSkill != 0)
        {
            context.Log.Notice(GameText.System(TextGatherTransformed, "Transforming."));
            return true;
        }

        GatherFishing = fishing;
        gatherToolSlot = slot;
        if (fishing) miningRequests.FishingStart(); else miningRequests.Start();
        return true;
    }

    private void StopGather(bool sendStop)
    {
        if (!Gathering) return;
        Gathering = false;
        SetGatherClipLoop(context.Self.CharId, loop: false);
        avatar.ActionUntil = 0;
        gatherers.Remove(context.Self.CharId);
        fishingCast.Remove(context.Self.CharId);
        if (context.Entities.Views.TryGetValue(context.Self.CharId, out var selfView)) selfView.Gathering = false;
        ClearGatherFx();
        RefreshGatherChip();
        if (sendStop) { if (GatherFishing) miningRequests.FishingStop(); else miningRequests.Stop(); }
    }

    private void ClearGatherFx()
    {
        if (gatherFx == null) return;
        if (GodotObject.IsInstanceValid(gatherFx)) gatherFx.QueueFree();
        gatherFx = null;
    }

    private AnimationPlayer? GatherAnimOf(int entityId) =>
        entityId == context.Self.CharId ? avatar.Anim
        : context.Entities.Views.TryGetValue(entityId, out var e) ? e.Anim
        : null;

    private void SetGatherClipLoop(int entityId, bool loop)
    {
        var anim = GatherAnimOf(entityId);
        string name = anim?.CurrentAnimation.ToString() ?? "";
        if (anim == null || name.Length == 0) return;
        var res = anim.GetAnimation(name);
        if (res != null)
            res.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
    }

    private void PlayGatherCast(int entityId)
    {
        entityAction.PlaySkillAction(entityId, FishingCastClip, FishingCastClips, ModelAnimation.ActionRankPosture);
        SetGatherClipLoop(entityId, loop: false);
        fishingCast.Add(entityId);
        if (entityId == context.Self.CharId) ClearGatherFx();
    }

    private void PlayGatherHold(int entityId, bool fishing)
    {
        fishingCast.Remove(entityId);
        entityAction.PlaySkillAction(entityId, fishing ? FishingWaitClip : MiningClip,
                        fishing ? FishingWaitClips : MiningClips, ModelAnimation.ActionRankPosture);
        SetGatherClipLoop(entityId, loop: true);
        HoldGatherAction(entityId);
        if (fishing && entityId == context.Self.CharId) SpawnFishingLine();
    }

    private Node3D? GatherToolAttachment()
    {
        int gearSlot = gatherToolSlot == InventoryConstants.LeftHand ? LeftHandGearSlot : RightHandGearSlot;
        var skel = NodeSearch.FindFirst<Skeleton3D>(context.Self.Body);
        if (skel == null) return null;
        string want = $"{WeaponMounts.WeaponNodePrefix}{gearSlot}";
        foreach (var child in skel.GetChildren())
            if (child is BoneAttachment3D ba && ba.Name.ToString() == want)
                return ba;
        return null;
    }

    private bool TryGatherToolTip(out Vector3 tip)
    {
        tip = Vector3.Zero;
        var attach = GatherToolAttachment();
        if (attach == null || !GodotObject.IsInstanceValid(attach)) return false;
        var mesh = NodeSearch.FindFirst<MeshInstance3D>(attach);
        if (mesh == null || mesh.Mesh == null) return false;

        var aabb = mesh.GetAabb();
        var toWorld = mesh.GlobalTransform;
        Vector3 hand = attach.GlobalPosition;
        float best = -1f;
        for (int i = 0; i < BoxCorners; i++)
        {
            Vector3 corner = toWorld * aabb.GetEndpoint(i);
            float d = corner.DistanceSquaredTo(hand);
            if (d > best) { best = d; tip = corner; }
        }
        return best >= 0f;
    }

    private void AnchorFishingLine()
    {
        if (gatherFx == null || !GodotObject.IsInstanceValid(gatherFx)) return;
        if (TryGatherToolTip(out var tip)) gatherFx.GlobalPosition = tip;
    }

    private void SpawnFishingLine()
    {
        if (Fx.NameForId(FishingWaitFx) is not { } waitFx) return;
        ClearGatherFx();
        gatherFx = Fx.Spawn(waitFx, context.Self.Body, new Vector3(0, GatherFxHeight, 0));
        if (gatherFx == null) return;
        gatherFx.RotationDegrees = new Vector3(0, 180f, 0);
        AnchorFishingLine();
        audio.FxAt(FishingWaitFx, context.Self.CharId);
    }

    private void HoldGatherAction(int entityId)
    {
        double now = FrameClock.Now();
        if (entityId == context.Self.CharId) entityAction.BeginSelfAction(ActionAnimation.ActionClipCap, ModelAnimation.ActionRankPosture, now);
        else if (context.Entities.Views.TryGetValue(entityId, out var e)) ActionAnimation.BeginEntityAction(e, ActionAnimation.ActionClipCap, ModelAnimation.ActionRankPosture, now);
    }

    internal void GatherTick(double nowSec)
    {
        if (gatherers.Count == 0) return;

        foreach (var (entityId, fishing) in gatherers)
        {
            double until = entityId == context.Self.CharId ? avatar.ActionUntil
                : context.Entities.Views.TryGetValue(entityId, out var e) ? e.ActionUntil
                : 0;

            if (fishingCast.Contains(entityId))
            {
                if (nowSec >= until) PlayGatherHold(entityId, fishing);
            }
            else if (nowSec >= until - GatherActionRearm)
            {
                HoldGatherAction(entityId);
            }
        }

        if (Gathering && GatherFishing) AnchorFishingLine();

        if (!Gathering || nowSec < gatherNextAttempt) return;
        gatherNextAttempt = nowSec + GatherInterval;
        if (context.Self.Dead) { StopGather(sendStop: true); return; }
        if (GatherFishing) miningRequests.FishingAttempt(); else miningRequests.Attempt();
    }

    private void OnGatherStart(MiningSubOpcode sub, MiningResult code, int charId)
    {
        bool fishing = sub == MiningSubOpcode.FishingStart;

        if (code == MiningResult.Already && !Gathering)
        {
            if (fishing) miningRequests.FishingStop(); else miningRequests.Stop();
            context.Log.Notice(fishing ? "Fishing stopped." : "Mining stopped.");
            return;
        }

        if (code != MiningResult.Success)
        {
            ReportGatherFailure(sub, code);
            return;
        }

        gatherers[charId] = fishing;
        if (context.Entities.Views.TryGetValue(charId, out var gatherer))
        {
            gatherer.Gathering = true;
            gatherer.GatherFishing = fishing;
        }
        if (charId == context.Self.CharId)
        {
            Gathering = true;
            GatherFishing = fishing;
            gatherNextAttempt = FrameClock.Now() + GatherInterval;
            RefreshGatherChip();
        }

        if (fishing) PlayGatherCast(charId);
        else PlayGatherHold(charId, fishing: false);
    }

    private void OnGatherResult(MiningSubOpcode sub, MiningResult code, int charId, int effect)
    {
        if (code == MiningResult.Success)
        {
            if (effect > 0 && Fx.NameForId(effect) is { } fxName
                && (charId == context.Self.CharId || !context.Entities.Views.TryGetValue(charId, out var gatherer) || audio.RemoteGatherFxAllowed(gatherer)))
            {
                context.Entities.SpawnFxOn(charId, fxName, GatherFxHeight);
                audio.FxAt(effect, charId);
            }
            if (gatherers.TryGetValue(charId, out bool wasFishing) && wasFishing)
                PlayGatherCast(charId);
            if (charId == context.Self.CharId) gatherNextAttempt = FrameClock.Now() + GatherInterval;
            return;
        }

        if (!Gathering) return;

        if (code is MiningResult.NothingFound or MiningResult.Preparing)
        {
            gatherNextAttempt = FrameClock.Now() + GatherInterval;
            return;
        }

        ReportGatherFailure(sub, code);
    }

    private void ReportGatherFailure(MiningSubOpcode sub, MiningResult code)
    {
        bool fishing = sub is MiningSubOpcode.FishingStart or MiningSubOpcode.FishingAttempt or MiningSubOpcode.FishingStop;
        context.Log.Notice(code switch
        {
            MiningResult.Already => fishing ? "On fishing." : "You are mining already",
            MiningResult.NotArea => fishing ? "This is not a fishing area." : "Not mining area",
            MiningResult.NoTool => fishing ? "Fishing rod is not equipped." : "A pickaxe is not equipped",
            MiningResult.NoEarthworm => "No Earthworm.",
            _ => fishing ? "Fishing has been failed." : "Mining failed",
        });
        StopGather(sendStop: false);
    }

    internal void BeginRemoteGather(int entityId, bool fishing)
    {
        if (entityId == context.Self.CharId) return;
        gatherers[entityId] = fishing;
        if (context.Entities.Views.TryGetValue(entityId, out var e))
        {
            e.Gathering = true;
            e.GatherFishing = fishing;
        }
        if (fishing) PlayGatherCast(entityId);
        else PlayGatherHold(entityId, fishing: false);
    }

    internal void EndRemoteGather(int entityId)
    {
        SetGatherClipLoop(entityId, loop: false);
        gatherers.Remove(entityId);
        fishingCast.Remove(entityId);
        if (context.Entities.Views.TryGetValue(entityId, out var e)) { e.Gathering = false; e.ActionUntil = 0; }
    }

    private void OnGatherStop(MiningSubOpcode sub, int charId, bool personalAck)
    {
        if (personalAck) { StopGather(sendStop: false); return; }
        EndRemoteGather(charId);
    }

    private void OnItemDuration(int position, short dura)
    {
        if (position >= 0 && position < self.Inventory.Length)
        {
            self.SetInventorySlot(position, new ItemSlot { ItemId = self.Inventory[position].ItemId, Count = self.Inventory[position].Count, Durability = dura });
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
        }

        if (Gathering && position == gatherToolSlot && dura <= 0)
        {
            context.Log.Notice(ToolBrokenText(GatherFishing));
            StopGather(sendStop: true);
        }
    }

    private const string GatherChipLayoutId = "hud_gather";
    private const float GatherChipLift = 230f;
    private const int GatherChipFont = 13;

    private PanelContainer? gatherChip;
    private Label gatherChipText = null!;

    private void BuildGatherChip()
    {
        var layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);
        var chip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        chip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        chip.AddChild(row);
        gatherChipText = UiTheme.Text("", GatherChipFont, UiTheme.Gold);
        gatherChipText.MouseFilter = Control.MouseFilterEnum.Ignore;
        gatherChipText.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(gatherChipText);
        var stop = UiTheme.SmallButton("Stop", "");
        stop.Pressed += () => StopGather(sendStop: true);
        row.AddChild(stop);
        layer.AddChild(chip);
        gatherChip = chip;
        HudLayout.Attach(chip, GatherChipLayoutId, chip, GatherChipHome);
    }

    private Vector2 GatherChipHome()
    {
        var screen = context.Root.GetViewport().GetVisibleRect().Size;
        var size = gatherChip?.GetCombinedMinimumSize() ?? Vector2.Zero;
        return new Vector2((screen.X - size.X) / 2f, screen.Y - GatherChipLift - size.Y);
    }

    private void RefreshGatherChip()
    {
        if (!Gathering)
        {
            if (gatherChip != null) gatherChip.Visible = false;
            return;
        }
        if (gatherChip == null || !GodotObject.IsInstanceValid(gatherChip)) BuildGatherChip();
        gatherChipText.Text = GatherFishing ? "Fishing in progress" : "Mining in progress";
        gatherChip!.TooltipText = $"{KeyBinds.Get(KeyAction.Interact).Text} also stops it.";
        gatherChip.Visible = true;
    }
}
