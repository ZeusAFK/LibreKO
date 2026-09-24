using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const float WalkSpeed = 1.5f;
    private const float RunSpeed = 6.0f;
    private const float GmSpeedMult = 5.0f;
    private const float SendHz = 10.0f;

    private const float CapsuleHalf = 1.0f;
    private const uint WorldCollisionLayer = 1;
    private const uint BlockerCollisionLayer = 2;
    private const float PlayerCapsuleRadius = 0.4f;
    private const float PlayerCapsuleHeight = 1.3f;
    private const float PlayerCapsuleOffsetY = 1.15f;
    private const float BlockedProgressFraction = 0.25f;
    private const double BlockedIdleAfter = 0.15;
    private const double BlockedGiveUpTargetAfter = 0.7;
    private Node3D _self = null!;
    private CharacterBody3D? _selfBody;
    private Node3D _selfVisual = null!;
    private Transform3D _selfStandingVisualTransform;
    private CapsuleShape3D? _selfCapsule;
    private float _selfCapsuleOffsetY;
    private Vector3 _lastFreePos;
    private AnimationPlayer? _selfAnim;
    private string? _selfClip;
    private double _selfActionUntil;
    private int _selfActionRank;
    private int _selfActionAnim = NoActionAnim;
    private Flinch? _selfFlinch;
    private float _selfLift = CapsuleHalf;
    private bool _selfMoving;
    private bool _selfMovingBackward;
    private bool _selfDead;
    private double _blockedFor;

    private float _myKoX, _myKoZ, _myKoY;
    private float _lastKoX, _lastKoZ;
    private double _sendAccum;
    private float _lastSentHeading = float.NaN;
    private bool _running = true;
    private bool _isGm;
    private bool _collisionsOff;
    private bool NoClip => _isGm && _collisionsOff;

    private bool GmSpeedHeld => (_isGm || Net.I.GmSpeedGranted) && Held(KeyAction.GmSpeed);
    private Vector3 _moveWish;
    private Vector3 _faceDir = Vector3.Forward;
    private Vector3 _attackLungeDir;
    private double _attackLungeUntil;
    private void HandleInput(double delta)
    {
        _movePressedEdge = false;
        _walkKeyHeld = false;

        if (_selfDead) { _selfMoving = false; return; }
        bool typing = GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
        if (typing && !WhisperInputHasFocus()) { _selfMoving = false; return; }

        if (_selfSitting)
        {
            if (!SeatedMoveIntent())
            {
                _selfMoving = false;
                _moveWish = Vector3.Zero;
                if (_selfBody != null) _selfBody.Velocity = Vector3.Zero;
                return;
            }
            StandUp();
        }

        var wish = ProfileMoveWish(delta, typing);
        _moveWish = wish.LengthSquared() > 0.01f ? wish.Normalized() : Vector3.Zero;

        _selfMoving = wish != Vector3.Zero;
        if (_selfMoving) CloseInteractionDialogs();
        if (wish == Vector3.Zero && !_turning)
            return;

        wish = _moveWish;
        float speed = _selfMovingBackward ? WalkSpeed : _running ? RunSpeed : WalkSpeed;
        speed *= MoveSpeedMultiplier();
        if (GmSpeedHeld)
            speed *= GmSpeedMult;
        if (IsRootedByCast()) speed = 0f;

        var wasAt = _self.Position;
        if (_selfBody != null)
        {
            _selfBody.Velocity = wish * speed;
            _selfBody.MoveAndSlide();
        }
        else
            _self.Position += wish * speed * (float)delta;

        var resolved = _self.Position;
        var (koX, koZ) = WorldToKo(resolved);
        var grounded = GroundPos(koX, koZ, resolved.Y - _selfLift, _selfLift);
        float objY = ObjectFloorY(grounded, resolved.Y - _selfLift);
        if (objY > grounded.Y) grounded.Y = objY + _selfLift;
        _self.Position = grounded;

        if (CapsuleOverlaps())
            _self.Position = _lastFreePos;
        else
            _lastFreePos = _self.Position;

        (koX, koZ) = WorldToKo(_self.Position);
        _myKoX = koX; _myKoZ = koZ; _myKoY = _self.Position.Y - _selfLift;

        if (!IsRootedByCast() && (_selfMoving || _turning))
            _self.RotationDegrees = new Vector3(0, 180f - Coord.KoHeading(-_faceDir.X, _faceDir.Z), 0);

        if (TrackBlockedProgress(wasAt, speed, delta))
        {
            _selfMoving = false;
            speed = 0f;
        }

        _sendAccum += delta;
        if (_sendAccum >= 1.0 / SendHz)
        {
            _sendAccum = 0;
            Net.I.SendMove(koX, koZ, _myKoY, _selfMovingBackward ? -speed : speed);
            _lastKoX = koX; _lastKoZ = koZ;
            SendHeadingIfChanged(koX, koZ);
        }
    }

    private bool TrackBlockedProgress(Vector3 wasAt, float speed, double delta)
    {
        var moved = new Vector2(_self.Position.X - wasAt.X, _self.Position.Z - wasAt.Z);
        if (!_selfMoving || speed <= 0f
            || moved.Length() >= speed * (float)delta * BlockedProgressFraction)
        {
            _blockedFor = 0;
            return false;
        }

        _blockedFor += delta;
        if (_blockedFor >= BlockedGiveUpTargetAfter)
        {
            _hasMoveTarget = false;
            _autoMoveForward = false;
            _terrainMoveHeld = false;
        }
        return _blockedFor >= BlockedIdleAfter;
    }

    private const float HeadingResendRadians = 0.2f;

    private void SendHeadingIfChanged(float koX, float koZ)
    {
        var (faceX, faceZ) = WorldToKo(_self.Position + _faceDir);
        float koHeading = Coord.KoHeading(faceX - koX, faceZ - koZ);
        if (!float.IsNaN(_lastSentHeading) && Mathf.Abs(Mathf.AngleDifference(
                Mathf.DegToRad(koHeading), Mathf.DegToRad(_lastSentHeading))) <= HeadingResendRadians)
            return;
        Net.I.SendRotate(koHeading);
        _lastSentHeading = koHeading;
    }

    private bool SeatedMoveIntent() =>
        _hasMoveTarget || _terrainMoveHeld || _autoMoveForward
        || Held(KeyAction.MoveForward) || Held(KeyAction.MoveBackward);

    private void OnWarp(float koX, float koZ)
    {
        if (_self == null) return;
        var pos = GroundPos(koX, koZ, _self.Position.Y - _selfLift, _selfLift);
        _self.Position = pos;
        _lastFreePos = pos;
        _lastKoX = koX; _lastKoZ = koZ;
        _myKoX = koX; _myKoZ = koZ; _myKoY = pos.Y - _selfLift;
        if (Now() < _blinkWarpUntil)
        {
            _blinkWarpUntil = 0;
            return;
        }
        Fx.Spawn(Net.I.LastEnter.Nation == Nations.Karus ? "warp_ka" : "warp_el", _self, Vector3.Zero,
            oneShot: true);
        Audio.Play(Sfx.WarpZone, pos);
    }

    private static CollisionShape3D BodyCapsule(float radius, float lift) => new()
    {
        Shape = new CapsuleShape3D { Radius = radius, Height = PlayerCapsuleHeight },
        Position = new Vector3(0, PlayerCapsuleOffsetY - lift, 0),
    };

    private static CharacterBody3D MakePlayerBody(Node3D visual)
    {
        var body = new CharacterBody3D { MotionMode = CharacterBody3D.MotionModeEnum.Floating };
        body.AddChild(BodyCapsule(PlayerCapsuleRadius, 0f));
        body.AddChild(visual);
        return body;
    }

    private void ApplyCollisionPolicy()
    {
        if (_selfBody == null) return;
        _selfBody.CollisionMask = NoClip ? 0u : WorldCollisionLayer | BlockerCollisionLayer;
    }

    private void SetCollisions(bool on)
    {
        if (!_isGm) return;
        _collisionsOff = !on;
        ApplyCollisionPolicy();
        RefreshAdminCollisionSwitch();
        Chat.Info(on ? "Collision enabled." : "Collision disabled — you now walk through everything.");
    }

    private bool RunLocalCommand(string command)
    {
        string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !parts[0].Equals("collision", System.StringComparison.OrdinalIgnoreCase))
            return false;
        if (!_isGm) return false;
        bool on = parts.Length < 2
            ? _collisionsOff
            : parts[1].Equals("on", System.StringComparison.OrdinalIgnoreCase);
        SetCollisions(on);
        return true;
    }
}
