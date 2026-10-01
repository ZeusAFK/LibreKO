namespace LibreKO;

public partial class World
{
    public const byte FreezeBuffType = 22;
    public const byte StunBuffType = 47;

    private double _stunnedUntil;

    public static bool IsStunBuffType(int buffType) => buffType is FreezeBuffType or StunBuffType;

    private bool IsStunned() => Now() < _stunnedUntil;

    private void Stun(double seconds)
    {
        if (seconds <= 0) return;
        double until = Now() + seconds;
        if (until > _stunnedUntil) _stunnedUntil = until;
        _hasMoveTarget = false;
        _terrainMoveHeld = false;
        CancelSwing();
    }

    private void ClearStun() => _stunnedUntil = 0;

    private void ApplySelfStatus(int buffType, int speedPercent, double seconds)
    {
        if (IsStunBuffType(buffType)) Stun(seconds);
        else if (IsSpeedBuffType(buffType)) ApplyMoveSpeedBuff(speedPercent, (int)seconds);
        StartBlind(buffType, seconds);
    }
}
