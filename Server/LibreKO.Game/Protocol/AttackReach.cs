using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public static class AttackReach
{
    public const float Melee = 6f;
    public const float PlayerBodyRadius = 1f;
    public const float RangeUnitsPerMetre = 10f;
    private const float SpeedUnitsPerMetre = 10f;

    public static float MovementLag(UserSession session, long now)
    {
        var sinceReport = (float)(now - session.LastMoveTicks) / TimeSpan.TicksPerSecond;
        if (session.MoveOldEcho != WorldMovementService.MoveEchoMove || sinceReport > MovePace.ReportSeconds + MovePace.LateReportSeconds)
            return 0f;

        var speed = Math.Min(Math.Abs((int)session.MoveOldSpeed) / SpeedUnitsPerMetre, MovePace.TopSpeed(session, now));
        var ran = Math.Min(speed, session.LastStepSpeed) * Math.Clamp(sinceReport, 0f, MovePace.ReportSeconds);
        return Math.Min(ran, session.LastStepDistance);
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
