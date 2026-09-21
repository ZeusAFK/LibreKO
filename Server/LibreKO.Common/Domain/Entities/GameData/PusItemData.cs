using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class PusItemData
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string? ItemName { get; set; }
    public string? ItemTitle { get; set; }
    public int Price { get; set; }
    public int SendType { get; set; }
    public int BuyCount { get; set; }
    public string ItemDesc { get; set; } = "";
    public byte Category { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<PusItemData>
    {
        public void Configure(EntityTypeBuilder<PusItemData> builder)
        {
            builder.ToTable("PUS_ITEMS");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).HasColumnName("ID").ValueGeneratedNever();
            builder.Property(p => p.ItemId).HasColumnName("ItemID");
            builder.Property(p => p.ItemName).HasColumnName("strItemName").HasMaxLength(150);
            builder.Property(p => p.ItemTitle).HasColumnName("strItemTitle").HasMaxLength(1000);
            builder.Property(p => p.Price).IsRequired();
            builder.Property(p => p.SendType).HasColumnName("SendType");
            builder.Property(p => p.BuyCount).HasColumnName("BuyCount");
            builder.Property(p => p.ItemDesc).HasColumnName("strItemDesc").HasMaxLength(1000).IsRequired();
            builder.Property(p => p.Category).HasColumnType("tinyint");
        }
    }
}

public class PusCategoryData
{
    public byte Id { get; set; }
    public string CategoryName { get; set; } = "";
    public string Description { get; set; } = "";
    public byte CategoryId { get; set; }
    public byte Status { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<PusCategoryData>
    {
        public void Configure(EntityTypeBuilder<PusCategoryData> builder)
        {
            builder.ToTable("PUS_CATEGORY");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).HasColumnName("ID").ValueGeneratedNever();
            builder.Property(p => p.CategoryName).HasColumnName("Categoryname").HasMaxLength(30).IsRequired();
            builder.Property(p => p.Description).HasMaxLength(50).IsRequired();
            builder.Property(p => p.CategoryId).IsRequired();
            builder.Property(p => p.Status).IsRequired();
        }
    }
}