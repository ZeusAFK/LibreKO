using LibreKO.Common.Enums;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MiningFishingItemData
{
    public const int ExpRewardItemNum = 900001000;

    public int Index { get; set; }
    public GatherType Type { get; set; }
    public GatherWarStatus WarStatus { get; set; }
    public GatherTool UseItemType { get; set; }
    public int GiveItemNum { get; set; }
    public short GiveItemCount { get; set; }
    public int SuccessRate { get; set; }
}
