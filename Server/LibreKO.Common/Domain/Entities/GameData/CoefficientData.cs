using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class CoefficientData
{
    public short ClassId { get; set; }
    public double ShortSword { get; set; }
    public double Jamadar { get; set; }
    public double Sword { get; set; }
    public double Axe { get; set; }
    public double Club { get; set; }
    public double Spear { get; set; }
    public double Pole { get; set; }
    public double Staff { get; set; }
    public double Bow { get; set; }
    public double Hp { get; set; }
    public double Mp { get; set; }
    public double Sp { get; set; }
    public double Ac { get; set; }
    public double Hitrate { get; set; }
    public double Evasionrate { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<CoefficientData>
    {
        public void Configure(EntityTypeBuilder<CoefficientData> builder)
        {
            builder.HasKey(p => p.ClassId);
            builder.Property(p => p.ClassId).ValueGeneratedNever();
        }
    }
}
