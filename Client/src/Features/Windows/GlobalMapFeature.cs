using Godot;

namespace LibreKO.Features.Windows;

internal sealed class GlobalMapFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly GlobalMapSession globalMap;
    private readonly IGlobalMapRequests requests;

    public GlobalMapFeature(IWorldContext context, GlobalMapSession globalMap, IGlobalMapRequests requests)
    {
        this.context = context;
        this.globalMap = globalMap;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private VBoxContainer list = null!;
    private bool shown;

    public bool IsOpen => shown;

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        panel = new HudWindow("globalmap", "World Map", new Vector2(180, 120)) { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);

        var root = panel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("World Map"));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        var hZone = UiTheme.Text("Zone", 12, UiTheme.TextLo); hZone.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var hPop = UiTheme.Text("Players", 12, UiTheme.TextLo); hPop.CustomMinimumSize = new Vector2(70, 0);
        var hOwner = UiTheme.Text("Nation", 12, UiTheme.TextLo); hOwner.CustomMinimumSize = new Vector2(90, 0);
        header.AddChild(hZone);
        header.AddChild(hPop);
        header.AddChild(hOwner);
        root.AddChild(header);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);

        globalMap.GlobalMapListEvent += OnList;
    }

    public void Dispose()
    {
        globalMap.GlobalMapListEvent -= OnList;
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        panel.Visible = true;
        shown = true;
        requests.List();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        panel.Visible = false;
    }

    private void OnList(List<GlobalMapZone> zones)
    {
        foreach (var c in list.GetChildren()) c.QueueFree();

        int total = 0;
        foreach (var z in zones)
        {
            total += z.Population;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var name = UiTheme.Text(z.Name, 13, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);

            var pop = UiTheme.Text(z.Population.ToString(), 13, z.Population > 0 ? UiTheme.TextHi : UiTheme.TextLo);
            pop.CustomMinimumSize = new Vector2(70, 0);
            hb.AddChild(pop);

            var owner = UiTheme.Text(NationName(z.OwnerNation), 13, NationColor(z.OwnerNation));
            owner.CustomMinimumSize = new Vector2(90, 0);
            hb.AddChild(owner);

            list.AddChild(row);
        }

        if (list.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No zones available.";
            list.AddChild(e);
        }
        else
        {
            var footer = UiTheme.Text($"Total players online: {total}", 12, UiTheme.Gold);
            list.AddChild(footer);
        }
    }

    private static string NationName(byte nation) => nation switch
    {
        1 => "Karus",
        2 => "El Morad",
        _ => "Neutral",
    };

    private Color NationColor(byte nation) => nation switch
    {
        1 => new Color("d08a6a"),
        2 => new Color("6a9ad0"),
        _ => UiTheme.TextLo,
    };
}
