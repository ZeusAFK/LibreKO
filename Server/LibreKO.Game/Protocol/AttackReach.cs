using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public static class AttackReach
{
    public const float Melee = 6f;
    public const float PlayerBodyRadius = 1f;
    public const float RangeUnitsPerMetre = 10f;
    // A moving client reports where it is only every 1.5 s.
    private const float MoveReportSeconds = 1.5f;
    private const float LateReportSeconds = 0.5f;
    private const float SpeedUnitsPerMetre = 10f;

    public static float MovementLag(UserSession session, long now)
    {
        var sinceReport = (float)(now - session.LastMoveTicks) / TimeSpan.TicksPerSecond;
        if (session.MoveOldEcho == WorldMovementService.MoveEchoFinish || sinceReport > MoveReportSeconds + LateReportSeconds)
            return 0f;

        var speed = Math.Min(Math.Abs((int)session.MoveOldSpeed) / SpeedUnitsPerMetre, MovePace.TopSpeed(session, now));
        if (session.MoveOldEcho == WorldMovementService.MoveEchoMove)
            speed = Math.Min(speed, session.LastStepSpeed);
        return speed * Math.Clamp(sinceReport, 0f, MoveReportSeconds);
    }

    public static ItemData? HeldBow(UserSession session, IGameDataService gameData) =>
        new[] { InventoryConstants.LeftHand, InventoryConstants.RightHand }
            .Select(session.GetEquippedItem)
            .Where(slot => !slot.IsEmpty)
            .Select(slot => gameData.GetItem(slot.ItemId))
            .FirstOrDefault(item => item?.IsBow() == true);

    public static bool Within(UserSession attacker, float x, float z, float reach)
    {
        var dx = attacker.X - x;
        var dz = attacker.Z - z;
        return dx * dx + dz * dz <= reach * reach;
    }
}
