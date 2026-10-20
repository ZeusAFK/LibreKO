using System;
using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class SlaveMerchantData
{
    public int Id { get; set; }
    public int OwnerCharacterId { get; set; }
    public int OwnerAccountId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string BotName { get; set; } = string.Empty;
    public AccountNation Nation { get; set; } = AccountNation.Karus;
    public byte Race { get; set; }
    public short Class { get; set; }
    public byte Face { get; set; }
    public byte Hair { get; set; }
    public byte Level { get; set; }
    public byte ZoneId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public short Direction { get; set; }
    public MerchantType MerchantType { get; set; } = MerchantType.Selling;
    public string StallTitle { get; set; } = string.Empty;
    public string ItemsJson { get; set; } = "[]";
    public string EquipmentJson { get; set; } = "[]";
    public long EarnedNoah { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<SlaveMerchantData>
    {
        public void Configure(EntityTypeBuilder<SlaveMerchantData> builder)
        {
            builder.ToTable("SlaveMerchants");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedOnAdd();
            builder.Property(p => p.OwnerName).HasMaxLength(32);
            builder.Property(p => p.BotName).HasMaxLength(32);
            builder.Property(p => p.StallTitle).HasMaxLength(128);
            builder.Property(p => p.ItemsJson).HasMaxLength(4000);
            builder.Property(p => p.EquipmentJson).HasMaxLength(2000);
            builder.HasIndex(p => p.OwnerCharacterId);
            builder.HasIndex(p => p.IsActive);
        }
    }
}
