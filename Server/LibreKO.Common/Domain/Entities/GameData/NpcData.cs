using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class NpcData
{
    public const byte TypeMonster = 0;
    public const byte TypeBoss = 3;
    public const byte TypeGuard = 11;
    public const byte TypePatrolGuard = 12;
    public const byte TypeStoreGuard = 13;
    public const byte TypeWarGuard = 14;
    public const byte TypeTradeMerchant = 21;
    public const byte TypeRepairMerchant = 22;
    public const byte TypeAnvil = 24;
    public const byte TypeClanCape = 25;
    public const byte TypeWarehouse = 31;
    public const byte TypeClassChange = 35;
    public const byte TypeHealer = 40;
    public const byte TypeGate = 50;
    public const byte TypeTalk = 64;
    public const short MakeupArtist = 31525;
    public const byte TypeObjectWood = 54;
    public const byte TypeChaoticGenerator = 162;
    public const byte TypeScarecrow = 171;
    public const byte TypeGuardSummon = 255;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte NpcType { get; set; }
    public bool IsMonster { get; set; }
    public bool IsBoss { get; set; }
    public byte Group { get; set; }
    public byte Rank { get; set; }
    public byte Title { get; set; }
    public short Level { get; set; }
    public int Hp { get; set; }
    public short Mp { get; set; }
    public short Ac { get; set; }
    public short Attack1 { get; set; }
    public short Attack2 { get; set; }
    public int Money { get; set; }
    public int Experience { get; set; }
    public int Loyalty { get; set; }
    public short ModelId { get; set; }
    public short Size { get; set; }
    public int WeaponType1 { get; set; }
    public int WeaponType2 { get; set; }
    public short HitRate { get; set; }
    public short EvadeRate { get; set; }
    public short FireR { get; set; }
    public short ColdR { get; set; }
    public short LightningR { get; set; }
    public short MagicR { get; set; }
    public short PoisonR { get; set; }
    public short CurseR { get; set; }
    public int SellingGroup { get; set; }
    public short ItemGroup { get; set; }

    // AI fields
    public byte ActType { get; set; }
    public short AttackDelay { get; set; }
    public short Speed { get; set; }        // m_sSpeed: movement delay ms
    public byte Speed1 { get; set; }
    public byte Speed2 { get; set; }
    public short Standtime { get; set; }
    public byte AttackRange { get; set; }
    public byte SearchRange { get; set; }
    public byte TracingRange { get; set; }
    public short Bulk { get; set; }
    public byte DirectAttack { get; set; }
    public byte MagicAttack { get; set; }
    public int Magic1 { get; set; }
    public int Magic2 { get; set; }
    public int Magic3 { get; set; }
    public byte Family { get; set; }
    public double AreaRange { get; set; }

    public bool IsNpc => !IsMonster;
    public bool IsMerchant => SellingGroup > 0;

    internal class EntityConfiguration : IEntityTypeConfiguration<NpcData>
    {
        public void Configure(EntityTypeBuilder<NpcData> builder)
        {
            builder.HasKey(p => new { p.Id, p.IsMonster });
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
