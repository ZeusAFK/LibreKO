namespace LibreKO.Game.World;

public sealed class PendingMagicExecution
{
    public required long Token { get; init; }
    public required int SkillId { get; init; }
    public required int TargetId { get; init; }
    public required int[] Data { get; init; }
}
