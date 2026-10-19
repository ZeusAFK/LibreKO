using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ProgressionFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly StatsFeature stats;
    private readonly EntityPlatesSystem entityPlates;
    private readonly SelfSession self;
    private readonly SkillWindowFeature skillWindow;
    private readonly ProgressSession progress;
    private readonly LootSession loot;

    public ProgressionFeature(IWorldContext context, StatsFeature stats, EntityPlatesSystem entityPlates, SelfSession self, SkillWindowFeature skillWindow, ProgressSession progress, LootSession loot)
    {
        this.context = context;
        this.stats = stats;
        this.entityPlates = entityPlates;
        this.self = self;
        this.skillWindow = skillWindow;
        this.progress = progress;
        this.loot = loot;
    }

    public void Build()
    {
        progress.GoldChangeEvent += OnGoldChange;
        loot.LootGoldEvent += OnLootGold;
        progress.GoldChangeLogEvent += OnGoldChangeLog;
        progress.ExpJackpotEvent += OnExpJackpot;
        progress.ExpChangeEvent += OnExpChange;
        progress.LevelChangeEvent += OnLevelChange;
        progress.PeerLevelChangeEvent += OnPeerLevelChange;
        progress.LoyaltyChangeEvent += OnLoyaltyChange;
        progress.MannerChangeEvent += OnMannerChange;
    }

    public void Dispose()
    {
        progress.GoldChangeEvent -= OnGoldChange;
        loot.LootGoldEvent -= OnLootGold;
        progress.GoldChangeLogEvent -= OnGoldChangeLog;
        progress.ExpJackpotEvent -= OnExpJackpot;
        progress.ExpChangeEvent -= OnExpChange;
        progress.LevelChangeEvent -= OnLevelChange;
        progress.PeerLevelChangeEvent -= OnPeerLevelChange;
        progress.LoyaltyChangeEvent -= OnLoyaltyChange;
        progress.MannerChangeEvent -= OnMannerChange;
    }

    private const string LevelUpFxKarus = "karu_levelup";
    private const string LevelUpFxElMorad = "elmo_levelup";

    private void OnGoldChange(GoldChange change)
    {
        if (change.Delta > 0 && !change.Transfer) context.Floaters?.Gold(change.Delta);
        stats.RefreshUI();
    }

    private const int TextPickedUpCoins = 6067;
    private const int TextEarnedExp = 3007;
    private const int TextLostExp = 3008;
    private const int TextEarnedNp = 6100;
    private const int TextEarnedManner = 6101;
    private const int TextLostNp = 6104;
    private const int TextEarnedLadder = 6103;
    private const int TextLostLadder = 6105;

    private static string WithAmount(int textId, string fallback, long amount) =>
        ItemData.Text(textId, fallback).Replace("%I64d", amount.ToString("n0")).Replace("%d", amount.ToString("n0"));

    private const int TextEarnedCoins = 6088;
    private const int TextLostCoins = 6089;
    private const int TextUsedCoins = 6099;
    private const int TextReceivedCoins = 6091;
    private const int TextMultipliedCoins = 6093;

    private void OnGoldChangeLog(GoldChangeReply reply)
    {
        switch (reply.Sub)
        {
            case GoldChangeKind.Earned:
                context.Log.Add(WithAmount(TextEarnedCoins, "Earned %d Coins.", reply.Amount), CombatLogKind.Resource, tab: LogTab.Item);
                break;
            case GoldChangeKind.Lost:
                context.Log.Add(WithAmount(TextLostCoins, "Lost %d Coins.", reply.Amount), CombatLogKind.Incoming, tab: LogTab.Item);
                break;
            case GoldChangeKind.Used:
                context.Log.Add(WithAmount(TextUsedCoins, "Used %d Coins.", reply.Amount), CombatLogKind.Incoming, tab: LogTab.Item);
                break;
            case GoldChangeKind.Received:
                context.Log.Add(WithAmount(TextReceivedCoins, "Received %d coins.", reply.Amount), CombatLogKind.Resource, tab: LogTab.Item);
                break;
            case GoldChangeKind.Multiplied:
                if (reply.UnitId == context.Self.CharId)
                    context.Log.Add(WithAmount(TextMultipliedCoins, "Received %d times of the coins.", reply.Times), CombatLogKind.Resource, tab: LogTab.Item);
                PlayJackpotEffect(reply.UnitId, Jackpot.CoinEffect(reply.Times));
                break;
        }
    }

    private void OnExpJackpot(int unitId, int times)
    {
        if (unitId == context.Self.CharId)
            context.Log.Add(WithAmount(Jackpot.ExpText, "You have been awarded %d times your EXP.", times), CombatLogKind.Resource, tab: LogTab.System);
        PlayJackpotEffect(unitId, Jackpot.ExpEffect(times));
    }

    private void PlayJackpotEffect(int unitId, int fxId)
    {
        if (fxId > 0 && Fx.NameForId(fxId) is { } name) context.Entities.SpawnFxOn(unitId, name, 0f);
    }

    private void OnLootGold(int total)
    {
        long gained = total - self.Sheet.Gold;
        if (gained > 0)
            context.Log.Add(WithAmount(TextPickedUpCoins, "Picked up %d Coins.", gained), CombatLogKind.Resource, tab: LogTab.Item);
    }

    private void LogExpDelta(long delta)
    {
        if (delta > 0) context.Log.Add(WithAmount(TextEarnedExp, "Earned %I64d Experience Points", delta), CombatLogKind.Experience);
        else if (delta < 0) context.Log.Add(WithAmount(TextLostExp, "Lost %I64d Experience Points", -delta), CombatLogKind.Experience);
    }

    private void OnExpChange(ExpChange change)
    {
        if (change.Delta > 0) context.Floaters?.Exp(change.Delta);
        LogExpDelta(change.Delta);
        stats.RefreshUI();
    }

    private void OnLevelChange(LevelChange change)
    {
        if (change.PreviousLevel > 0 && change.Level != change.PreviousLevel)
            LogExpDelta(change.LeveledUp ? change.Exp + (change.PreviousMaxExp - change.PreviousExp)
                : change.Exp - change.PreviousExp - change.MaxExp);
        context.Hud.RefreshVitalBars();
        skillWindow.RefreshSkillsEnabled();
        stats.RefreshUI();
        context.Inventory.RefreshFooter();
        if (change.LeveledUp)
        {
            PlayLevelUp(context.Self.CharId, self.LastEnter.Nation);
        }
        context.Entities.RefreshSelfClanLine();
        entityPlates.NoticeNoahGraduation(change.PreviousLevel, change.Level);
    }

    private void OnPeerLevelChange(int charId, int level)
    {
        if (!context.Entities.Views.TryGetValue(charId, out var e)) return;
        bool dingedUp = level > e.Level;
        e.Level = level;
        context.Entities.RefreshClanLine(charId);
        if (dingedUp) PlayLevelUp(charId, e.Nation);
    }

    private void PlayLevelUp(int charId, int nation)
    {
        context.Entities.SpawnFxOn(charId, nation == Nations.Karus ? LevelUpFxKarus : LevelUpFxElMorad, 0f);
        Audio.Play(Sfx.LevelUp(nation), context.Entities.WorldPosOf(charId) ?? context.Self.Body.GlobalPosition);
    }

    private void OnLoyaltyChange(LoyaltyChange change)
    {
        if (self.Sheet.Level <= 0) { stats.RefreshUI(); return; }
        if (change.Delta > 0) context.Log.Add(WithAmount(TextEarnedNp, "Earned %d national points.", change.Delta), CombatLogKind.National);
        else if (change.Delta < 0) context.Log.Add(WithAmount(TextLostNp, "You have lost %d country CONT.", -change.Delta), CombatLogKind.National);
        int ladder = change.MonthlyDelta;
        if (ladder > 0) context.Log.Add(WithAmount(TextEarnedLadder, "Received %d ladder points.", ladder), CombatLogKind.National);
        else if (ladder < 0) context.Log.Add(WithAmount(TextLostLadder, "You have lost %d ladder points.", -ladder), CombatLogKind.National);
        stats.RefreshUI();
    }

    private void OnMannerChange(MannerChange change)
    {
        if (change.Delta > 0) context.Log.Add(WithAmount(TextEarnedManner, "Earned %d manner points.", change.Delta), CombatLogKind.National);
        stats.RefreshUI();
    }
}
