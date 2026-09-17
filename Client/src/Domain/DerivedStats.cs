namespace LibreKO.Domain;

public class DerivedStats
{
    public int TotalHit { get; set; }
    public int TotalAc { get; set; }
    public int MaxWeight { get; set; }
    public int MaxHp { get; set; }
    public int MaxMp { get; set; }

    public int StrBonus { get; set; }
    public int StaBonus { get; set; }
    public int DexBonus { get; set; }
    public int IntBonus { get; set; }
    public int ChaBonus { get; set; }

    public int FireR { get; set; }
    public int ColdR { get; set; }
    public int LightningR { get; set; }
    public int MagicR { get; set; }
    public int DiseaseR { get; set; }
    public int PoisonR { get; set; }

    public float CritRate { get; set; }
}
