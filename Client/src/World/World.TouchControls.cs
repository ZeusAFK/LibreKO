using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World : Node3D
{
    private const int TouchZoneLayerIndex = 60;
    private const int TouchLayerIndex = 66;

    private CanvasLayer? _touchZoneLayer;
    private CanvasLayer? _touchLayer;
    private TouchActionBar? _touchActions;

    private void TouchControlsInit()
    {
        _touchZoneLayer = new CanvasLayer { Layer = TouchZoneLayerIndex };
        AddChild(_touchZoneLayer);
        TouchControls.BuildCameraZone(_touchZoneLayer, OrbitCamera,
            () => SelectNearest(hostile: true));

        _touchLayer = new CanvasLayer { Layer = TouchLayerIndex };
        AddChild(_touchLayer);
        TouchControls.BuildStick(_touchLayer, StartCameraHalfTurn);
        _touchActions = TouchControls.BuildActions(_touchLayer, () => ToggleAutoAttack(),
            ActivateHotSlot, ChangeHotPage, TouchSlotIcon,
            () => SelectNearest(hostile: true),
            DropOntoHotSlot,
            () => TalkToNearestNpc(""),
            () => OpenNearestLootBox(),
            () => OpenNearestAnvil(),
            () => OpenNearestWarpGate(),
            OpenNearestPlayerMenu,
            () => TryBrowseNearestMerchant());
        _hotbarBox.Visible = false;

        GD.Print($"[touch] on-screen controls built: sticks {TouchControls.StickSize:F0}px, "
                 + $"{TouchControls.ActionSlots} action buttons, "
                 + $"auto-attack at {_touchActions.AutoAttackButtonForPreview.GetGlobalRect()}");
    }

    private Texture2D? TouchSlotIcon(int slotInPage)
    {
        int id = _hotbar[_hotPage * HotSlotsPerPage + slotInPage];
        if (id == 0) return null;
        return SkillData.IsSkill(id) ? SkillData.Icon(id) : ItemData.Icon(id);
    }

    private void UpdateTouchInteractionVisibility()
    {
        if (_touchActions == null) return;
        _touchActions.SetInteractionVisibility(
            HasNearbyNpc(),
            HasNearbyLootBox(),
            HasNearbyAnvil(),
            HasNearbyWarpGate(),
            HasNearbyPlayerForUserInfo(),
            HasNearbyMerchantStall());
    }

    private void TouchControlsDispose()
    {
        _touchActions = null;
        _touchLayer?.QueueFree();
        _touchLayer = null;
        _touchZoneLayer?.QueueFree();
        _touchZoneLayer = null;
    }
}
