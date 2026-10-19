using Godot;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginCharacterBridge(InventoryFeature inventory, EntityPlatesSystem entityPlates, StatsFeature stats, CosmeticsFeature cosmetics, PortraitFeature portrait, SelfSession self, ProgressSession progress, ItemsSession items, UserOptionSession userOption, MailSession mail, ClanSession clan, IUserOptionRequests userOptionRequests) : IGameCharacter
{
    event Action? IGameCharacter.Changed { add { } remove { } }

    public void Subscribe()
    {
        self.SelfHpEvent += OnHp;
        self.SelfMpEvent += OnMp;
        progress.PointChangeEvent += OnPoints;
        progress.GoldChangeEvent += OnGold;
        progress.ExpChangeEvent += OnExp;
        items.ItemStatsEvent += OnStats;
        userOption.RankAuraShownEvent += OnRankAura;
        mail.MailUnreadEvent += OnMailUnread;
    }

    private void OnMailUnread(int unread) => RaiseCharacter();

    private void OnRankAura(int charId, bool shown)
    {
        if (charId == self.MyCharId) RaiseCharacter();
    }

    public void Unsubscribe()
    {
        self.SelfHpEvent -= OnHp;
        self.SelfMpEvent -= OnMp;
        progress.PointChangeEvent -= OnPoints;
        progress.GoldChangeEvent -= OnGold;
        userOption.RankAuraShownEvent -= OnRankAura;
        progress.ExpChangeEvent -= OnExp;
        items.ItemStatsEvent -= OnStats;
        mail.MailUnreadEvent -= OnMailUnread;
    }

    private void OnHp(SelfHpChange change) => RaiseCharacter();

    private void OnMp(VitalChange change) => RaiseCharacter();

    private void OnPoints(PointChangeReply reply) => RaiseCharacter();

    private void OnGold(GoldChange change)
    {
        RaiseCharacter();
        Callable.From(PluginHost.Game.RaiseInventory).CallDeferred();
    }

    private void OnExp(ExpChange change) => RaiseCharacter();

    private void OnStats(DerivedStats s) => RaiseCharacter();

    public void RaiseCharacter() => Callable.From(PluginHost.Game.RaiseCharacter).CallDeferred();

    public string Name => self.LastEnter.Name;

    public int Class => self.Class;

    public string ClassName => CharacterClassCatalog.DisplayName(self.Class);

    public int Race => self.Race;

    public int Nation => self.LastEnter.Nation;

    public string NationName => UiKit.NationName(self.LastEnter.Nation);

    public int Level => self.Sheet.Level;

    public int Hp => self.Vitals.Hp;

    public int MaxHp => self.Vitals.MaxHp;

    public int Mp => self.Vitals.Mp;

    public int MaxMp => self.Vitals.MaxMp;

    public long Exp => self.Sheet.Exp;

    public long MaxExp => self.Sheet.MaxExp;

    public double ExpPercent => self.Sheet.ExpPercent;

    public int Gold => self.Sheet.Gold;

    public int KnightCash => self.Sheet.KnightCash;

    public int Weight => inventory.CarriedWeight();

    public int MaxWeight => self.Sheet.MaxWeight;

    public int Str => self.Sheet.Str;

    public int Sta => self.Sheet.Sta;

    public int Dex => self.Sheet.Dex;

    public int Intel => self.Sheet.Intel;

    public int Mag => self.Sheet.Mag;

    public int Points => self.Sheet.Points;

    public int Ap => self.Sheet.Ap;

    public int Ac => self.Sheet.Ac;

    public int Np => self.Sheet.Np;

    public int Manner => self.Sheet.Manner;

    public bool NoahKnight => entityPlates.SelfIsNoahKnight;

    public int MailUnread => mail.MailUnread;

    public int Resist(int index) => self.Sheet.ResistAt(index);

    public void AllocateStat(int statRow) => stats.OnAllocate(statRow);

    public int Sp => self.Vitals.Sp;

    public int MaxSp => self.Vitals.MaxSp;

    public int RebirthLevel => self.Sheet.RebirthLevel;

    public int RebirthStat(int statRow) => self.Sheet.RebirthBonusAtRow(statRow);

    public int StatBonus(int statRow) => self.Sheet.StatBonusAtRow(statRow);

    string IGameCharacter.ClanName => clan.MyClan.InClan ? clan.MyClan.Name ?? "" : "";

    public bool HasRankAura => cosmetics.SelfHasRankAura;

    public bool RankAuraShown => userOption.OwnRankAuraShown;

    public void SetRankAuraShown(bool shown) => userOptionRequests.SetOwnRankAuraShown(shown);

    public Texture2D? Portrait => portrait.Texture;

    public void SetPortraitShown(bool shown) => portrait.SetShown(shown);
}
