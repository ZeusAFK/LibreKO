using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ChaosGuideFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly EntryPopupsFeature entryPopups;
    private readonly RankFeature rank;

    public ChaosGuideFeature(IWorldContext context, EntryPopupsFeature entryPopups, RankFeature rank)
    {
        this.context = context;
        this.entryPopups = entryPopups;
        this.rank = rank;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private const float ColumnWidth = 300f;
    private const int TextSize = 13;
    private const string Heading = "The Great Disorder — a doomed temple. Trust no one.";
    private const string Battle = "Kill everyone and survive at all costs!";
    private const string CubeIntro = "Item gained when coming in contact with the Chaos Cube!";
    private const string RewardIntro = "Blue Treasure Box, Green Treasure Box, Red Treasure Box, Oracle Voucher, Chaos Emblem";

    private static readonly (string Rank, string Gain)[] CubeRows =
    {
        ("Top 20%", "Light Pit"),
        ("Top 21% ~ 50%", "Drain Restore"),
        ("Bottom 51% ~ 100%", "Killing Blade (kills with one strike)"),
    };

    private static readonly (string Rank, string Gain)[] RewardRows =
    {
        ("Top 1% ~ 20%", "Chaos Emblem, Blue Treasure Box, Oracle Voucher"),
        ("21% ~ 40%", "Chaos Emblem, Green Treasure Box"),
        ("41% ~ 80%", "Chaos Emblem, Red Treasure Box"),
        ("81% ~ 100%", "Chaos Emblem"),
    };

    internal HudWindow? ChaosGuide { get; private set; }

    private CanvasLayer? layer;

    internal void ShowOnEntry()
    {
        if (context.Zone != TempleZones.ChaosDungeon || TimedItems.HiddenToday(Config.ChaosGuideHiddenDay, DateTime.Now)) return;
        entryPopups.QueuePopup(EntryPopup.ZoneGuide, () =>
        {
            ChaosGuide ??= BuildWindow();
            UiKit.ShowOnTop(ChaosGuide);
            return ChaosGuide;
        });
    }

    internal HudWindow BuildWindow()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        var window = new HudWindow("chaos_caption", "Chaos") { Visible = false };
        window.Closed += () => window.Visible = false;
        layer.AddChild(window);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        window.Body.AddChild(column);

        var heading = UiTheme.Text(Heading, 15, UiTheme.GoldBright, HorizontalAlignment.Right);
        column.AddChild(heading);

        column.AddChild(UiTheme.SectionTitle("Battle Method"));
        column.AddChild(UiTheme.Text(Battle, 16, UiTheme.GoldVivid, HorizontalAlignment.Center));

        var halves = new HBoxContainer();
        halves.AddThemeConstantOverride("separation", 16);
        column.AddChild(halves);
        halves.AddChild(Half("Acquire the Chaos Cube!", CubeIntro, CubeRows));
        halves.AddChild(Half("Reward", RewardIntro, RewardRows));

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        footer.AddThemeConstantOverride("separation", 10);
        var today = UiTheme.FlatCheck("Do not open this window today", false);
        today.Toggled += on =>
        {
            Config.SetChaosGuideHiddenDay(on ? TimedItems.DayKey(DateTime.Now) : 0);
            if (on) window.Visible = false;
        };
        footer.AddChild(today);
        footer.AddChild(GenderChangeFeature.LookActionButton("Close", () => window.Visible = false));
        column.AddChild(footer);
        return window;
    }

    private static Control Half(string title, string intro, (string Rank, string Gain)[] rows)
    {
        var half = new VBoxContainer { CustomMinimumSize = new Vector2(ColumnWidth, 0f) };
        half.AddThemeConstantOverride("separation", 6);
        half.AddChild(UiTheme.SectionTitle(title));

        var lead = UiTheme.Text(intro, TextSize, UiTheme.Gold);
        lead.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        lead.CustomMinimumSize = new Vector2(ColumnWidth, 0f);
        half.AddChild(lead);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (var (rankText, gain) in rows)
        {
            var label = UiTheme.Text(rankText, TextSize, UiTheme.TextHi);
            label.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            grid.AddChild(label);
            var value = UiTheme.Text(gain, TextSize, UiTheme.TextLo);
            value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.AddChild(value);
        }
        half.AddChild(grid);
        return half;
    }
}
