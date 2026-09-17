namespace LibreKO.Common.Domain.Entities.GameData;

public class MiningExchangeData
{
    public short Index { get; set; }

    public short NpcId { get; set; }

    public byte GiveEffect { get; set; }

    public byte OreType { get; set; }

    public int OriginItemNum { get; set; }

    public int GiveItemNum { get; set; }

    public short GiveItemCount { get; set; }

    public int SuccessRate { get; set; }
}
