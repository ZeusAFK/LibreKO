namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemExchangeExpData
{
    public int Index { get; set; }
    public byte RandomFlag { get; set; }

    public int ExchangeItem1 { get; set; }
    public int ExchangeCount1 { get; set; }
    public int ExchangeItem2 { get; set; }
    public int ExchangeCount2 { get; set; }
    public int ExchangeItem3 { get; set; }
    public int ExchangeCount3 { get; set; }
    public int ExchangeItem4 { get; set; }
    public int ExchangeCount4 { get; set; }
    public int ExchangeItem5 { get; set; }
    public int ExchangeCount5 { get; set; }

    public (int itemId, int count)[] GetExchangeItems()
    {
        return
        [
            (ExchangeItem1, ExchangeCount1),
            (ExchangeItem2, ExchangeCount2),
            (ExchangeItem3, ExchangeCount3),
            (ExchangeItem4, ExchangeCount4),
            (ExchangeItem5, ExchangeCount5)
        ];
    }
}
