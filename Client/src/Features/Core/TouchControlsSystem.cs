using Godot;
using LibreKO.Scene;

namespace LibreKO.Features.Core;

internal sealed class TouchControlsSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly WorldRoot world;
    private readonly CameraSystem camera;
    private readonly CombatSystem combat;
    private readonly SelectionFeature selection;
    private readonly NpcInteractionFeature npcInteraction;
    private readonly MapInteractablesSystem mapInteractables;

    public TouchControlsSystem(IWorldContext context, WorldRoot world, CameraSystem camera, CombatSystem combat, SelectionFeature selection, NpcInteractionFeature npcInteraction, MapInteractablesSystem mapInteractables)
    {
        this.context = context;
        this.world = world;
        this.camera = camera;
        this.combat = combat;
        this.selection = selection;
        this.npcInteraction = npcInteraction;
        this.mapInteractables = mapInteractables;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private CanvasLayer? touchZoneLayer;
    private CanvasLayer? touchLayer;
    internal TouchActionBar? TouchActions { get; private set; }
    internal const double TouchInteractionInterval = 0.25;
    private double touchInteractionAccum;

    internal void TouchInteractionTick(double delta)
    {
        touchInteractionAccum += delta;
        if (touchInteractionAccum < TouchInteractionInterval) return;
        touchInteractionAccum %= TouchInteractionInterval;
        UpdateTouchInteractionVisibility();
    }

    internal void TouchControlsInit()
    {
        touchZoneLayer = new CanvasLayer { Layer = UiLayers.TouchZones };
        context.Root.AddChild(touchZoneLayer);
        TouchControls.BuildCameraZone(touchZoneLayer, camera.Orbit, TouchTapWorld);

        touchLayer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(touchLayer);
        TouchControls.BuildStick(touchLayer, camera.StartCameraHalfTurn);
        TouchActions = TouchControls.BuildActions(touchLayer, () => world.BasicAttackSystem.ToggleAutoAttack(),
            world.Hotbar.ActivateHotSlot, world.Hotbar.ChangeHotPage, TouchSlotIcon,
            () => selection.SelectNearest(hostile: true),
            world.Hotbar.DropOntoHotSlot,
            () => npcInteraction.TalkToNearest(""),
            () => world.Loot.OpenNearestBox(),
            () => mapInteractables.OpenNearestAnvil(),
            () => mapInteractables.OpenNearestWarpGate(),
            at => world.PlayerMenu.OpenNearest(at),
            () => world.Merchant.TryBrowseNearest(),
            () => world.GatherSystem.TryStartGather());
        world.Hotbar.Box.Visible = false;

        GD.Print($"[touch] on-screen controls built: sticks {TouchControls.StickSize:F0}px, "
                 + $"{TouchControls.ActionSlots} action buttons, "
                 + $"auto-attack at {TouchActions.AutoAttackButtonForPreview.GetGlobalRect()}");
    }

    private void TouchTapWorld(Vector2 at)
    {
        if (world.Loot.TryClickBox(at)) return;
        selection.SelectNearest(hostile: true);
    }

    private Texture2D? TouchSlotIcon(int slotInPage)
    {
        int id = world.Hotbar.Slots[world.Hotbar.HotPage * HotbarFeature.HotSlotsPerPage + slotInPage];
        if (id == 0) return null;
        return SkillData.IsSkill(id) ? SkillIcons.Get(id) : ItemIcons.Get(id);
    }

    internal void UpdateTouchInteractionVisibility()
    {
        if (TouchActions == null) return;
        TouchActions.SetInteractionVisibility(
            npcInteraction.HasNearby(),
            world.Loot.HasNearbyBox(),
            mapInteractables.HasNearbyAnvil(),
            mapInteractables.HasNearbyWarpGate(),
            world.PlayerMenu.HasNearbyForUserInfo(),
            world.Merchant.HasNearbyStall(),
            world.GatherSystem.GatherHere());
    }

    internal void TouchControlsDispose()
    {
        TouchActions = null;
        touchInteractionAccum = 0;
        touchLayer?.QueueFree();
        touchLayer = null;
        touchZoneLayer?.QueueFree();
        touchZoneLayer = null;
    }
}
