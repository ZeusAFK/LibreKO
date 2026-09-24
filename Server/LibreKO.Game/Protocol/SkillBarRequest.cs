namespace LibreKO.Game.Protocol;

internal static class SkillBarRequest
{
    public const int Pages = 8;
    public const int SlotsPerPage = 10;
    public const int MaxSlots = Pages * SlotsPerPage;
    public const int BytesPerSlot = 4;
}
