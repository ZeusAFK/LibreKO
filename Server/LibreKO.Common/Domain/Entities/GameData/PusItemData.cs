namespace LibreKO.Common.Domain.Entities.GameData;

public class PusItemData
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Price { get; set; }
    public byte Category { get; set; }
}

public class PusCategoryData
{
    public byte Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public byte Status { get; set; }
}