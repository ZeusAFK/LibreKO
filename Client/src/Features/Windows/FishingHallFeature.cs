using Godot;

namespace LibreKO.Features.Windows;

internal sealed class FishingHallFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly FishingHallSession fishingHall;
    private readonly SelfSession self;
    private readonly IFishingHallRequests requests;

    public FishingHallFeature(IWorldContext context, FishingHallSession fishingHall, SelfSession self, IFishingHallRequests requests)
    {
        this.context = context;
        this.fishingHall = fishingHall;
        this.self = self;
        this.requests = requests;
    }

    private CanvasLayer fishHallLayer = null!;
    private HudWindow fishHallPanel = null!;
    private VBoxContainer fishHallList = null!;
    private bool fishHallShown;

    public void Build()
    {
        fishHallLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(fishHallLayer);
        fishHallPanel = new HudWindow("fishinghall", "Fishing Hall of Fame") { Visible = false };
        fishHallPanel.Closed += Close;
        fishHallLayer.AddChild(fishHallPanel);
        var root = fishHallPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Top Anglers"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(320, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        fishHallList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        fishHallList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(fishHallList);

        fishingHall.FishingHallListEvent += OnList;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (fishHallShown) Close();
        else return false;
        return true;
    }

    public void Dispose()
    {
        fishingHall.FishingHallListEvent -= OnList;
    }

    internal void Toggle()
    {
        if (fishHallShown) { Close(); return; }
        fishHallPanel.Visible = true;
        fishHallShown = true;
        requests.List();
    }

    internal void Close()
    {
        if (!fishHallShown) return;
        fishHallShown = false;
        fishHallPanel.Visible = false;
    }

    private void OnList(List<FishingHallEntry> list)
    {
        foreach (var c in fishHallList.GetChildren()) c.QueueFree();
        string myName = self.LastEnter.Name ?? "";
        foreach (var e in list)
        {
            bool isMe = e.Name == myName;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row(selected: isMe));
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var rank = UiTheme.Text("#" + e.Rank, 13, e.Rank <= 3 ? UiTheme.Gold : UiTheme.TextLo);
            rank.CustomMinimumSize = new Vector2(38, 0);
            hb.AddChild(rank);

            var name = UiTheme.Text(e.Name, 13, isMe ? UiTheme.GoldBright : UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);

            hb.AddChild(UiTheme.Text(e.Score.ToString("N0"), 13, UiTheme.Gold));

            fishHallList.AddChild(row);
        }
        if (fishHallList.GetChildCount() == 0)
        {
            var none = HudStyle.Label(13); none.Text = "No anglers ranked yet.";
            fishHallList.AddChild(none);
        }
    }
}
