using Godot;

namespace LibreKO.Features.Entities;

internal sealed class CosmeticsFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly CosmeticsSession cosmetics;
    private readonly SelfSession self;
    private readonly UserOptionSession userOption;
    private readonly ICosmeticsRequests requests;
    private readonly IUserOptionRequests userOptionRequests;
    private readonly StealthSystem stealth;
    private readonly EntitySystem entities;
    private readonly EntityPlatesSystem entityPlates;

    public CosmeticsFeature(IWorldContext context, CosmeticsSession cosmetics, SelfSession self, UserOptionSession userOption, ICosmeticsRequests requests, IUserOptionRequests userOptionRequests, StealthSystem stealth, EntitySystem entities, EntityPlatesSystem entityPlates)
    {
        this.context = context;
        this.cosmetics = cosmetics;
        this.self = self;
        this.userOption = userOption;
        this.requests = requests;
        this.userOptionRequests = userOptionRequests;
        this.stealth = stealth;
        this.entities = entities;
        this.entityPlates = entityPlates;
    }

    private Button? helmetToggle;
    private Button? costumeToggle;
    private Button? rankAuraToggle;

    internal const string RankAuraToggleIcon = "system/star";

    internal void AddLookToggles(Control board, float span)
    {
        costumeToggle = LookToggle("game/shoulder", ToggleCostume);
        costumeToggle.Position = new Vector2(LookToggleInset, LookToggleInset);
        board.AddChild(costumeToggle);
        helmetToggle = LookToggle("game/helmet", ToggleHelmet);
        helmetToggle.Position = new Vector2(span - LookToggleSize - LookToggleInset, LookToggleInset);
        board.AddChild(helmetToggle);
        rankAuraToggle = LookToggle(RankAuraToggleIcon, ToggleRankAura);
        rankAuraToggle.Position = new Vector2(LookToggleInset, span - LookToggleSize - LookToggleInset);
        board.AddChild(rankAuraToggle);
        RefreshLookToggles();
    }

    public void Build()
    {
        entityPlates.ClanFameApplied += OnClanFame;
        entities.EntitySpawned += OnEntitySeen;
        self.PersonalRankEvent += OnPersonalRank;
        entities.EntityRefreshed += OnEntitySeen;
        stealth.StealthLookChanged += RefreshRankAuraFor;
        cosmetics.HelmetEvent += OnHelmet;
        cosmetics.CostumeEvent += OnCostume;
        userOption.RankAuraShownEvent += OnRankAuraShown;
        Config.PlatesChanged += RefreshPlateTitles;
        Config.CospreChanged += RedressOthersForCospre;
    }

    public void Dispose()
    {
        entityPlates.ClanFameApplied -= OnClanFame;
        entities.EntitySpawned -= OnEntitySeen;
        self.PersonalRankEvent -= OnPersonalRank;
        entities.EntityRefreshed -= OnEntitySeen;
        stealth.StealthLookChanged -= RefreshRankAuraFor;
        cosmetics.HelmetEvent -= OnHelmet;
        cosmetics.CostumeEvent -= OnCostume;
        userOption.RankAuraShownEvent -= OnRankAuraShown;
        Config.PlatesChanged -= RefreshPlateTitles;
        Config.CospreChanged -= RedressOthersForCospre;
    }

    private void OnClanFame(int charId) => RefreshRankAuraFor(charId, context.Entities.StealthIds.Contains(charId));

    private void OnEntitySeen(EntityView e, EntitySnapshot info)
    {
        if (!info.IsNpc) RefreshRankAuraFor(info.Id, context.Entities.StealthIds.Contains(info.Id));
    }

    private void OnPersonalRank() =>
        RefreshRankAuraFor(context.Self.CharId, context.Entities.StealthIds.Contains(context.Self.CharId));

    internal void ToggleHelmet()
    {
        requests.LookToggles(!cosmetics.HelmetHidden, cosmetics.CostumeHidden);
    }

    internal void ToggleRankAura() => userOptionRequests.SetOwnRankAuraShown(!userOption.OwnRankAuraShown);

    internal void ToggleCostume()
    {
        requests.LookToggles(cosmetics.HelmetHidden, !cosmetics.CostumeHidden);
    }

    private void OnHelmet(int charId, bool hidden)
    {
        if (charId == context.Self.CharId)
        {
            context.Self.Redress();
            RefreshLookToggles();
            return;
        }
        if (!context.Entities.Views.TryGetValue(charId, out var e) || e.IsNpc || e.HelmetHidden == hidden) return;
        e.HelmetHidden = hidden;
        context.Entities.Redress(e);
    }

    private void OnCostume(int charId, bool hidden)
    {
        if (charId == context.Self.CharId)
        {
            context.Self.Redress();
            RefreshLookToggles();
            return;
        }
        if (!context.Entities.Views.TryGetValue(charId, out var e) || e.IsNpc || e.CostumeHidden == hidden) return;
        e.CostumeHidden = hidden;
        context.Entities.Redress(e);
    }

    internal Button LookToggle(string icon, System.Action pressed)
    {
        var button = UiTheme.IconButton(UiIcons.Get(icon), "");
        button.CustomMinimumSize = new Vector2(LookToggleSize, LookToggleSize);
        button.AddThemeConstantOverride("icon_max_width", LookToggleIcon);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.Pressed += pressed;
        return button;
    }

    internal void RefreshLookToggles()
    {
        StyleLookToggle(helmetToggle, cosmetics.HelmetHidden, "Helmet hidden - click to show it", "Hide the helmet");
        StyleLookToggle(costumeToggle, cosmetics.CostumeHidden, "Costume hidden - click to show it", "Hide the costume");
        StyleLookToggle(rankAuraToggle, !userOption.OwnRankAuraShown, "Rank aura hidden - click to show it", "Hide your rank aura");
        if (rankAuraToggle != null && GodotObject.IsInstanceValid(rankAuraToggle)) rankAuraToggle.Visible = SelfHasRankAura;
    }

    private static void StyleLookToggle(Button? button, bool hidden, string hiddenTip, string shownTip)
    {
        if (button == null || !GodotObject.IsInstanceValid(button)) return;
        var tint = hidden ? new Color(UiTheme.TextHi, LookToggleHiddenAlpha) : UiTheme.GoldBright;
        button.AddThemeColorOverride("icon_normal_color", tint);
        button.AddThemeColorOverride("icon_hover_color", hidden ? UiTheme.TextHi : Colors.White);
        button.TooltipText = hidden ? hiddenTip : shownTip;
    }

    internal const float LookToggleSize = 22f;
    internal const float LookToggleInset = 2f;
    private const int LookToggleIcon = 16;
    private const float LookToggleHiddenAlpha = 0.35f;

    private const string RankAuraNode = "rank_fx";
    private const string RankAuraRetiredNode = "rank_fx_retired";
    private static readonly StringName RankAuraFxMeta = "rank_fx_name";

    private void RefreshRankAura(Node3D? body, int personalRank, bool stealthed, bool king = false, bool shown = true)
    {
        if (body == null || !GodotObject.IsInstanceValid(body)) return;
        string? want = null;
        if (shown && !stealthed && !NationRankAura.HiddenIn(context.Zone))
        {
            int fxId = NationRankAura.FxIdFor(personalRank, king);
            if (fxId != NationRankAura.NoFx) want = Fx.NameForId(fxId);
        }
        var current = body.GetNodeOrNull<Node3D>(RankAuraNode);
        string? have = current != null && current.HasMeta(RankAuraFxMeta) ? current.GetMeta(RankAuraFxMeta).AsString() : null;
        if (want == have) return;
        if (current != null)
        {
            current.Name = RankAuraRetiredNode;
            Fx.Free(current);
        }
        if (want == null) return;
        var aura = Fx.Spawn(want, body, Vector3.Zero, oneShot: false);
        if (aura == null) return;
        aura.Name = RankAuraNode;
        aura.SetMeta(RankAuraFxMeta, want);
    }

    internal bool SelfHasRankAura => NationRankAura.FxIdFor(self.PersonalRank, self.IsKing) != NationRankAura.NoFx;

    private void OnRankAuraShown(int charId, bool shown)
    {
        RefreshRankAuraFor(charId, context.Entities.StealthIds.Contains(charId));
        if (charId == context.Self.CharId) RefreshLookToggles();
    }

    internal void RefreshRankAuraFor(int charId, bool stealthed)
    {
        if (charId == context.Self.CharId) RefreshRankAura(context.Self.Body, self.PersonalRank, stealthed, self.IsKing, userOption.OwnRankAuraShown);
        else if (context.Entities.Views.TryGetValue(charId, out var e) && !e.IsNpc)
        {
            RefreshRankAura(e.Body, e.PersonalRank, stealthed, shown: userOption.RankAuraShown(charId));
            string? symbol = RankSymbol.PlatePath(e.KnightsRank, e.PersonalRank);
            e.Plate?.SetRank(symbol != null ? GD.Load<Texture2D>(symbol) : null);
            e.Plate?.SetNameMark(NameMark.For(e.ClanFame, e.ClanFlag, context.Zone, e.Nation == self.Nation));
        }
    }

    private void RefreshPlateTitles()
    {
        context.Self.Plate()?.RefreshTitle();
        foreach (var e in context.Entities.Views.Values) e.Plate?.RefreshTitle();
    }

    private void RedressOthersForCospre()
    {
        foreach (var e in context.Entities.Views.Values)
            if (!e.IsNpc) context.Entities.Redress(e, force: true);
    }

    internal static int[]? ShownCospre(int[]? gear) => CospreFilter.Hide(gear, Config.HiddenCospre);
}
