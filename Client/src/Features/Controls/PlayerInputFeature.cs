using Godot;
using LibreKO.Scene;

namespace LibreKO.Features.Controls;

internal sealed class PlayerInputFeature : IFeature, IMoveGates
{
    private readonly WorldRoot world;
    private readonly SelfAvatar avatar;

    public PlayerInputFeature(WorldRoot world, SelfAvatar avatar)
    {
        this.world = world;
        this.avatar = avatar;
    }

    public void Build()
    {
        world.PlayerSystem.AttachMoveGates(this);
        world.SkillCastSystem.AttachMoveGates(this);
    }

    public void Dispose()
    {
        world.PlayerSystem.DetachMoveGates();
        world.SkillCastSystem.DetachMoveGates();
    }

    bool IMoveGates.InputHeld => world.Exchange.TradeHoldsInput;

    bool IMoveGates.WhisperHasFocus => world.Whispers.WhisperInputHasFocus();

    bool IMoveGates.StallBlocksMove() => world.MerchantStalls.BlocksMove();

    private Vector2 rightPressPos;
    private bool rightClickPending;
    private const float RightClickSlop = 5f;
    private const float TargetDragSlop = 5f;
    private HeldSteer steerFromTarget;

    internal void TargetNearestHostile()
    {
        world.Selection.SelectNearest(hostile: true);
        world.Selection.FaceSelectedWhenStill();
    }

    internal bool HandleProfilePointer(InputEvent inputEvent, bool overUi)
    {
        if (world.SkillAreaSystem.AreaCastPointer(inputEvent, overUi))
            return true;
        return HandlePointer(inputEvent, overUi);
    }

    private bool HandlePointer(InputEvent inputEvent, bool overUi)
    {
        var player = world.PlayerSystem;
        if (inputEvent is InputEventMouseButton
            {
                Pressed: false,
                ButtonIndex: MouseButton.Left,
            })
        {
            bool wasTerrainMove = player.HeldMoveActive;
            player.EndHeldMove();
            steerFromTarget.Disarm();
            return wasTerrainMove;
        }

        if (inputEvent is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Middle,
            } && !overUi)
        {
            world.CameraSystem.StartCameraHalfTurn();
            return true;
        }

        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true, DoubleClick: true } && !overUi)
        {
            rightClickPending = false;
            return world.Hotbar.CastSelectedHotSlot();
        }

        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Right } right)
        {
            if (right.Pressed)
            {
                rightClickPending = !overUi;
                rightPressPos = right.Position;
                return false;
            }

            bool wasClick = rightClickPending;
            rightClickPending = false;
            return wasClick && !overUi && world.NpcInteraction.TryInteractAt(right.Position);
        }

        if (inputEvent is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
                DoubleClick: true,
            } doubleClick && !overUi && TryAutoAttackAt(doubleClick.Position))
        {
            return true;
        }

        if (inputEvent is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
            } mouseButton && !overUi)
        {
            player.ResetHeldMove();
            steerFromTarget.Disarm();
            if (world.Loot.TryClickBox(mouseButton.Position))
                return true;
            if (world.Selection.TryPickAt(mouseButton.Position))
            {
                if (!TouchControls.Available)
                    steerFromTarget.ArmAt(mouseButton.Position.X, mouseButton.Position.Y);
            }
            else if (!TouchControls.Available)
                player.BeginClickMove(mouseButton.Position);
            return true;
        }

        if (inputEvent is InputEventMouseMotion motion)
        {
            bool handled = false;

            if (Input.IsMouseButtonPressed(MouseButton.Right) && !overUi)
            {
                if (rightClickPending
                    && motion.Position.DistanceTo(rightPressPos) > RightClickSlop)
                    rightClickPending = false;
                if (!rightClickPending)
                {
                    world.CameraSystem.Orbit(motion.Relative);
                    handled = true;
                }
            }

            if (!player.HeldMoveActive && !overUi
                && steerFromTarget.TryStart(Input.IsMouseButtonPressed(MouseButton.Left),
                    motion.Position.X, motion.Position.Y, TargetDragSlop)
                && player.TryStartHeldMove(motion.Position))
                handled = true;

            if (player.DragHeldMove(motion.Position, overUi))
                handled = true;

            return handled;
        }

        return false;
    }

    private bool TryAutoAttackAt(Vector2 screenPosition)
    {
        if (avatar.Dead) return false;
        var target = world.Selection.PickEntityAt(screenPosition, SelectionFeature.ClickPickRadius, out int id, out _);
        if (target == null || !target.Attackable || target.Dead) return false;
        world.Selection.Select(id, target);
        world.BasicAttackSystem.StartAutoAttack(id);
        return true;
    }
}
