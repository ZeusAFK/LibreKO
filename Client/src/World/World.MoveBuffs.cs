using System;
using Godot;

namespace LibreKO;

public partial class World
{
    private double _moveBuffUntil;
    private float _moveBuffPercent = 100f;

    private int _castingSkillId;
    private double _castingUntil;
    private float _castAnimScale = 1f;

    [Flags]
    private enum MoveKeys
    {
        None = 0,
        Forward = 1,
        Backward = 2,
        TurnLeft = 4,
        TurnRight = 8,
        Walk = Forward | Backward,
    }

    private MoveKeys _movePrevMask;
    private bool _movePressedEdge;
    private bool _walkKeyHeld;
    private bool _moveInputHeld;
    private const double CastHoldCancelGraceSeconds = 0.15;

    private const float AnimSpeedDeltaMin = 0.01f;
    private const float AnimSpeedDeltaMax = 10.0f;

    private void ApplySelfAnimSpeed()
    {
        if (_selfAnim == null) return;
        float delta = IsRootedByCast() ? _castAnimScale
            : SelfSwinging() ? AttackSpeedMultiplier()
            : _selfMoving ? MoveSpeedMultiplier() : 1f;
        if (delta < AnimSpeedDeltaMin || delta >= AnimSpeedDeltaMax) return;
        if (!Mathf.IsEqualApprox(_selfAnim.SpeedScale, delta)) _selfAnim.SpeedScale = delta;
    }

    private float MoveSpeedMultiplier()
    {
        if (Now() >= _moveBuffUntil)
        {
            _moveBuffPercent = 100f;
            return 1f;
        }
        return _moveBuffPercent / 100f;
    }

    private void ApplyMoveSpeedBuff(SkillData.Skill s, int duration)
    {
        int pct = s.MoveSpeedPercent;
        if (pct == 100 || duration <= 0) return;

        _moveBuffPercent = pct;
        _moveBuffUntil = Now() + duration;
    }

    private void ClearMoveSpeedBuff()
    {
        _moveBuffPercent = 100f;
        _moveBuffUntil = 0;
    }

    private bool IsRootedByCast() => _castingSkillId != 0 && Now() < _castingUntil;

    private void BeginCast(SkillData.Skill s)
    {
        if (!s.RootsCaster) return;
        if (_castingSkillId == s.Id && Now() < _castingUntil) return;
        _castingSkillId = s.Id;
        _castingUntil = Now() + s.CastSeconds;
    }

    private void EndCast(int skillId)
    {
        if (_castingSkillId == skillId || skillId == 0)
        {
            _castingSkillId = 0;
            _castingUntil = 0;
            _castAnimScale = 1f;
        }
    }

    private const float CastStretchMinSeconds = 0.1f;

    private void StretchCastAnimation(SkillData.Skill s)
    {
        _castAnimScale = 1f;
        if (s.CastSeconds <= CastStretchMinSeconds || _selfAnim == null) return;
        var clip = _selfAnim.GetAnimation(_selfAnim.CurrentAnimation);
        if (clip == null || clip.Length <= 0f) return;
        _castAnimScale = (float)(clip.Length / s.CastSeconds);
        _selfActionUntil = Now() + s.CastSeconds;
    }

    private const double RangedCommitSeconds = 0.4;

    private void CastMoveCancelTick()
    {
        if (_castingSkillId == 0) return;
        var s = SkillData.Get(_castingSkillId);
        if (s is not { HasCastPhase: true }) return;
        if (_movePressedEdge) { InterruptSelfCast(); return; }
        if (!_moveInputHeld) return;
        if (!s.NeedsFlying && Now() - (_castingUntil - s.CastSeconds) >= CastHoldCancelGraceSeconds)
            InterruptSelfCast();
    }

    private void InterruptSelfCast()
    {
        int skillId = _castingSkillId;
        if (skillId == 0) return;
        var s = SkillData.Get(skillId);
        if (s is not { HasCastPhase: true }) return;
        if (s.NeedsFlying && Now() >= _castingUntil - s.CastSeconds + RangedCommitSeconds)
            ReleaseSelfCastEarly(skillId);
        else
            CancelSelfCast(skillId);
    }

    private void ReleaseSelfCastEarly(int skillId)
    {
        double now = Now();
        for (int i = 0; i < _pendingCasts.Count; i++)
        {
            var pc = _pendingCasts[i];
            if (pc.SkillId != skillId || pc.Stage != PendingFlying) continue;
            pc.EffectTime = now;
            _pendingCasts[i] = pc;
        }
        StopSkillFx(_myId, skillId, 1);
        EndCast(skillId);
        _selfActionUntil = 0;
        _selfClip = null;
    }

    private void CancelSelfCast(int skillId)
    {
        StopSkillFx(_myId, skillId);
        EndCast(skillId);
        ClearPendingCast(skillId);
        var s = SkillData.Get(skillId);
        if (s != null)
            CancelSkillCooldown(s);
        Net.I.SendMagic(4, skillId, _myId);
        _selfActionUntil = 0;
        _selfClip = null;
    }
}
