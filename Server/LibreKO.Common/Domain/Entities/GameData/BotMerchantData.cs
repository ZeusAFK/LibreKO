using System;
using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class BotMerchantData
{
    public int Id { get; set; }
    public string BotName { get; set; } = string.Empty;
    public AccountNation Nation { get; set; } = AccountNation.Karus;
    public byte Race { get; set; } = 1;
    public short Class { get; set; } = 101;
    public byte Face { get; set; } = 0;
    public byte Hair { get; set; } = 0;
    public byte Level { get; set; } = 83;
    public byte ZoneId { get; set; } = 21; // Moradon
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public short Direction { get; set; }
    public MerchantType MerchantType { get; set; } = MerchantType.Selling;
    public string StallTitle { get; set; } = string.Empty;
    public string ItemsJson { get; set; } = "[]";
    public string EquipmentJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    internal class EntityConfiguration : IEntityTypeConfiguration<BotMerchantData>
    {
        public void Configure(EntityTypeBuilder<BotMerchantData> builder)
        {
            builder.ToTable("BotMerchants");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedOnAdd();
            builder.Property(p => p.BotName).HasMaxLength(32);
            builder.Property(p => p.StallTitle).HasMaxLength(128);
            builder.Property(p => p.ItemsJson).HasMaxLength(4000);
            builder.Property(p => p.EquipmentJson).HasMaxLength(2000);
        }
    }
}
