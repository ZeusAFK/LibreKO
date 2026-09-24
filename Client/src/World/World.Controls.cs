using Godot;

namespace LibreKO;

public partial class World
{
    private Vector3 _moveTarget;
    private bool _hasMoveTarget;
    private bool _terrainMoveHeld;
    private bool _autoMoveForward;
    private Vector2 _terrainMovePointer;
    private double _terrainMoveRetargetAccum;
    private bool _turning;
    private Node3D? _moveIndicator;
    private Vector2 _rightPressPos;
    private bool _rightClickPending;
    private const float RightClickSlop = 5f;
    private const float TargetDragSlop = 5f;
    private HeldSteer _steerFromTarget;
    private const float TurnSpeed = 2.2f;
    private const float PadTurnSpeed = 7.0f;
    private const double TerrainMoveRetargetInterval = 1.0 / 30.0;
    private const double MoveIndicatorLife = 2.0;
    private const string MoveIndicatorFx = "target_pointer";

    private static bool Held(KeyAction action) => KeyBinds.Held(action);

    private Vector3 CameraRelative(Vector2 stick)
    {
        var forward = new Vector3(-Mathf.Sin(_camYaw), 0f, -Mathf.Cos(_camYaw));
        var right = forward.Cross(Vector3.Up);
        return (forward * -stick.Y + right * stick.X).Normalized();
    }

    private void NoteMoveKey(InputEventKey key)
    {
        if (key.Keycode == KeyBinds.PolledKey(KeyAction.MoveForward)
            || key.Keycode == KeyBinds.PolledKey(KeyAction.MoveBackward))
            _autoMoveForward = false;
    }

    private void ToggleAutoRun()
    {
        _autoMoveForward = !_autoMoveForward;
        if (!_autoMoveForward) return;
        _hasMoveTarget = false;
        _terrainMoveHeld = false;
        StopAutoAttack();
    }

    private void ToggleRunMode() => _running = !_running;

    private void TargetNearestHostile()
    {
        SelectNearest(hostile: true);
        FaceSelectedWhenStill();
    }

    private bool HandleProfilePointer(InputEvent inputEvent, bool overUi)
    {
        if (AreaCastPointer(inputEvent, overUi))
            return true;
        return HandlePointer(inputEvent, overUi);
    }

    private bool HandlePointer(InputEvent inputEvent, bool overUi)
    {
        if (inputEvent is InputEventMouseButton
            {
                Pressed: false,
                ButtonIndex: MouseButton.Left,
            })
        {
            bool wasTerrainMove = _terrainMoveHeld;
            _terrainMoveHeld = false;
            _steerFromTarget.Disarm();
            return wasTerrainMove;
        }

        if (inputEvent is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Middle,
            } && !overUi)
        {
            StartCameraHalfTurn();
            return true;
        }

        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true, DoubleClick: true } && !overUi)
        {
            _rightClickPending = false;
            return CastSelectedHotSlot();
        }

        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Right } right)
        {
            if (right.Pressed)
            {
                _rightClickPending = !overUi;
                _rightPressPos = right.Position;
                return false;
            }

            bool wasClick = _rightClickPending;
            _rightClickPending = false;
            return wasClick && !overUi && TryInteractAt(right.Position);
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
            _terrainMoveHeld = false;
            _steerFromTarget.Disarm();
            if (TryClickLootBox(mouseButton.Position))
                return true;
            if (TryPickAt(mouseButton.Position))
            {
                if (!TouchControls.Available)
                    _steerFromTarget.ArmAt(mouseButton.Position.X, mouseButton.Position.Y);
            }
            else if (!TouchControls.Available && TrySetTerrainMoveTarget(mouseButton.Position))
            {
                _terrainMoveHeld = true;
                _terrainMovePointer = mouseButton.Position;
                _terrainMoveRetargetAccum = 0.0;
            }
            return true;
        }

        if (inputEvent is InputEventMouseMotion motion)
        {
            bool handled = false;

            if (Input.IsMouseButtonPressed(MouseButton.Right) && !overUi)
            {
                if (_rightClickPending
                    && motion.Position.DistanceTo(_rightPressPos) > RightClickSlop)
                    _rightClickPending = false;
                if (!_rightClickPending)
                {
                    OrbitCamera(motion.Relative);
                    handled = true;
                }
            }

            if (!_terrainMoveHeld && !overUi
                && _steerFromTarget.TryStart(Input.IsMouseButtonPressed(MouseButton.Left),
                    motion.Position.X, motion.Position.Y, TargetDragSlop)
                && TrySetTerrainMoveTarget(motion.Position))
            {
                _terrainMoveHeld = true;
                _terrainMovePointer = motion.Position;
                _terrainMoveRetargetAccum = 0.0;
                handled = true;
            }

            if (_terrainMoveHeld)
            {
                if (!Input.IsMouseButtonPressed(MouseButton.Left))
                {
                    _terrainMoveHeld = false;
                }
                else
                {
                    if (!overUi)
                    {
                        _terrainMovePointer = motion.Position;
                        TrySetTerrainMoveTarget(motion.Position);
                        _terrainMoveRetargetAccum = 0.0;
                    }
                    handled = true;
                }
            }

            return handled;
        }

        return false;
    }

    private bool TryAutoAttackAt(Vector2 screenPosition)
    {
        if (_selfDead) return false;
        var target = PickEntityAt(screenPosition, ClickPickRadius, out int id, out _);
        if (target == null || !target.Attackable || target.Dead) return false;
        Select(id, target);
        StartAutoAttack(id);
        return true;
    }

    private void UpdateHeldMoveTarget(double delta)
    {
        if (!_terrainMoveHeld)
            return;
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _terrainMoveHeld = false;
            return;
        }

        _terrainMoveRetargetAccum += delta;
        if (_terrainMoveRetargetAccum < TerrainMoveRetargetInterval)
            return;
        _terrainMoveRetargetAccum %= TerrainMoveRetargetInterval;

        if (GetViewport().GuiGetHoveredControl() != null)
            return;

        _terrainMovePointer = GetViewport().GetMousePosition();
        TrySetTerrainMoveTarget(_terrainMovePointer);
    }

    private void OrbitCamera(Vector2 relative)
    {
        _halfTurnActive = false;
        _camYaw -= relative.X * OrbitSens;
        _camPitch = Mathf.Clamp(_camPitch + relative.Y * OrbitSens, MinPitch, MaxPitch);
    }

    private Vector3 ProfileMoveWish(double delta, bool keyboardBlocked)
    {
        if (_terrainMoveHeld
            && !Input.IsMouseButtonPressed(MouseButton.Left))
            _terrainMoveHeld = false;

        _turning = false;
        _selfMovingBackward = false;

        var stick = keyboardBlocked ? Vector2.Zero : KeyBinds.MoveStick();
        if (stick != Vector2.Zero && MerchantBlocksMove()) stick = Vector2.Zero;
        if (stick != Vector2.Zero)
        {
            _autoMoveForward = false;
            _hasMoveTarget = false;
            _terrainMoveHeld = false;
            _movePressedEdge = _movePrevMask == MoveKeys.None;
            _movePrevMask = MoveKeys.Forward;
            _walkKeyHeld = false;
            _moveInputHeld = true;
            if (_movePressedEdge && RangedAutoAttack()) StopAutoAttack();
            var wish = CameraRelative(stick);
            float toTurn = _faceDir.SignedAngleTo(wish, Vector3.Up);
            float limit = PadTurnSpeed * (float)delta;
            _faceDir = _faceDir.Rotated(Vector3.Up, Mathf.Clamp(toTurn, -limit, limit)).Normalized();
            _turning = !Mathf.IsZeroApprox(toTurn);
            return _faceDir;
        }

        bool moveForward = !keyboardBlocked && Held(KeyAction.MoveForward);
        bool moveBackward = !keyboardBlocked && Held(KeyAction.MoveBackward);
        bool turnLeft = !keyboardBlocked && Held(KeyAction.TurnLeft);
        bool turnRight = !keyboardBlocked && Held(KeyAction.TurnRight);
        bool keyboardControl = moveForward || moveBackward || turnLeft || turnRight;

        var moveMask = (moveForward ? MoveKeys.Forward : MoveKeys.None)
                       | (moveBackward ? MoveKeys.Backward : MoveKeys.None)
                       | (turnLeft ? MoveKeys.TurnLeft : MoveKeys.None)
                       | (turnRight ? MoveKeys.TurnRight : MoveKeys.None);
        _movePressedEdge = (moveMask & ~_movePrevMask) != MoveKeys.None;
        _walkKeyHeld = (moveMask & MoveKeys.Walk) != MoveKeys.None;
        _moveInputHeld = keyboardControl;
        _movePrevMask = moveMask;
        if (keyboardControl)
        {
            if (moveForward || moveBackward)
                _autoMoveForward = false;
            _hasMoveTarget = false;
            if (_movePressedEdge && RangedAutoAttack()) StopAutoAttack();
        }

        float turn = (turnLeft ? 1f : 0f) - (turnRight ? 1f : 0f);
        if (!Mathf.IsZeroApprox(turn))
        {
            _faceDir = _faceDir.Rotated(Vector3.Up, turn * TurnSpeed * (float)delta).Normalized();
            _turning = true;
        }

        moveForward |= _autoMoveForward;
        if ((moveForward || moveBackward) && MerchantBlocksMove())
        {
            _autoMoveForward = false;
            return Vector3.Zero;
        }

        if (moveForward != moveBackward)
        {
            _selfMovingBackward = moveBackward;
            return moveBackward ? -_faceDir : _faceDir;
        }

        if (AutoAttackTarget() is { } autoTarget) return AutoAttackWish(autoTarget);

        if (!_hasMoveTarget)
            return Vector3.Zero;

        Vector3 toward = _moveTarget - _self.Position;
        toward.Y = 0;
        if (toward.LengthSquared() <= 0.12f * 0.12f)
        {
            _hasMoveTarget = false;
            return Vector3.Zero;
        }
        _faceDir = toward.Normalized();
        return _faceDir;
    }

    private Ent? AutoAttackTarget() =>
        _autoAttack
        && _autoTargetId >= 0
        && _ents.TryGetValue(_autoTargetId, out var target)
        && target.Attackable
        && !target.Dead
            ? target
            : null;

    private bool RangedAutoAttack() => BasicRangedAttackSkill() != null;

    private Vector3 AutoAttackWish(Ent autoTarget)
    {
        if (RangedAutoAttack()) return Vector3.Zero;
        Vector3 towardTarget = autoTarget.Body.GlobalPosition - _self.Position;
        towardTarget.Y = 0;
        float closeTo = BasicAttackRange(autoTarget) * ApproachFraction;
        if (towardTarget.LengthSquared() <= closeTo * closeTo) return Vector3.Zero;
        _faceDir = towardTarget.Normalized();
        return _faceDir;
    }

    private bool TrySetTerrainMoveTarget(Vector2 screenPosition)
    {
        if (MerchantBlocksMove()) return false;
        if (!TryPickTerrainPoint(screenPosition, _selfLift, out Vector3 destination))
            return false;

        _moveTarget = destination;
        _hasMoveTarget = true;
        InterruptSelfCast();
        StopAutoAttack();
        ShowMoveIndicator(_moveTarget);
        return true;
    }

    private bool TryPickTerrainPoint(Vector2 screenPosition, float lift, out Vector3 world)
    {
        world = Vector3.Zero;
        if (_camera == null || _self == null)
            return false;

        Vector3 from = _camera.ProjectRayOrigin(screenPosition);
        Vector3 direction = _camera.ProjectRayNormal(screenPosition).Normalized();

        if (_terrain == null)
        {
            float groundY = _self.Position.Y;
            if (Mathf.Abs(direction.Y) < 0.0001f)
                return false;
            float distance = (groundY - from.Y) / direction.Y;
            if (distance <= 0)
                return false;
            world = from + direction * distance;
            return true;
        }

        const float maxRayDistance = 1200f;
        const float rayStep = 4f;
        float previousDistance = 0f;
        float previousHeightDelta = HeightAboveTerrain(from);
        for (float distance = rayStep; distance <= maxRayDistance; distance += rayStep)
        {
            Vector3 point = from + direction * distance;
            float heightDelta = HeightAboveTerrain(point);
            if (!float.IsFinite(heightDelta))
                continue;
            if (heightDelta <= 0f && previousHeightDelta > 0f)
            {
                float low = previousDistance;
                float high = distance;
                for (int i = 0; i < 12; i++)
                {
                    float middle = (low + high) * 0.5f;
                    if (HeightAboveTerrain(from + direction * middle) > 0f)
                        low = middle;
                    else
                        high = middle;
                }

                Vector3 hit = from + direction * high;
                var (koX, koZ) = WorldToKo(hit);
                world = GroundPos(koX, koZ, hit.Y, lift);
                return true;
            }

            previousDistance = distance;
            previousHeightDelta = heightDelta;
        }

        return false;
    }

    private void ShowMoveIndicator(Vector3 destination)
    {
        if (_moveIndicator != null
            && GodotObject.IsInstanceValid(_moveIndicator))
        {
            _moveIndicator.GlobalPosition = destination + Vector3.Up * 0.04f;
            return;
        }

        var marker = Fx.Spawn(
            MoveIndicatorFx,
            this,
            destination + Vector3.Up * 0.04f);
        if (marker == null)
        {
            GD.PushWarning($"[controls] missing movement marker FX: {MoveIndicatorFx}");
            _moveIndicator = null;
            return;
        }

        marker.Name = "MoveIndicator";
        _moveIndicator = marker;
        ScheduleMoveIndicatorExpiry(marker);
    }

    private void ScheduleMoveIndicatorExpiry(Node3D marker)
    {
        GetTree().CreateTimer(MoveIndicatorLife).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(marker))
                return;
            if (_terrainMoveHeld)
            {
                ScheduleMoveIndicatorExpiry(marker);
                return;
            }

            marker.QueueFree();
            if (ReferenceEquals(_moveIndicator, marker))
                _moveIndicator = null;
        };
    }

    private float HeightAboveTerrain(Vector3 worldPoint)
    {
        if (_terrain == null)
            return float.NaN;
        var (koX, koZ) = WorldToKo(worldPoint);
        if (!_terrain.SampleHeight(koX, koZ, out float terrainY))
            return float.NaN;
        return worldPoint.Y - GroundPos(koX, koZ, terrainY, 0f).Y;
    }
}
