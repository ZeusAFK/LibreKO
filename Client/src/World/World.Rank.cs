using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _rankLayer = null!;
    private HudWindow _rankPanel = null!;
    private Button _rankKarusTab = null!, _rankElmoradTab = null!;
    private VBoxContainer _rankRows = null!;
    private Label _rankMine = null!, _rankStatus = null!;
    private bool _rankShown;

    private int _rankNation = 1;
    private int _rankMyPlace, _rankMyLoyalty;
    private readonly List<RankEntry> _rankKarus = new();
    private readonly List<RankEntry> _rankElmorad = new();

    private void RankInit()
    {
        BuildRankPanel();
        Net.I.RankEvent += OnRankData;
    }

    private void RankDispose()
    {
        Net.I.RankEvent -= OnRankData;
    }

    private void BuildRankPanel()
    {
        _rankLayer = new CanvasLayer { Layer = 75 };
        AddChild(_rankLayer);

        _rankPanel = new HudWindow("rank", "Rankings", new Vector2(220, 90)) { Visible = false };
        _rankPanel.Closed += CloseRank;
        _rankLayer.AddChild(_rankPanel);

        var root = _rankPanel.Body;
        root.AddThemeConstantOverride("separation", 8);
        root.CustomMinimumSize = new Vector2(380, 0);

        root.AddChild(UiTheme.SectionTitle("Loyalty Ladder"));

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        _rankKarusTab = new Button { Text = "Karus", FocusMode = Control.FocusModeEnum.None, ToggleMode = true };
        _rankKarusTab.AddThemeFontSizeOverride("font_size", 13);
        _rankKarusTab.Pressed += () => SelectRankTab(1);
        tabs.AddChild(_rankKarusTab);
        _rankElmoradTab = new Button { Text = "El Morad", FocusMode = Control.FocusModeEnum.None, ToggleMode = true };
        _rankElmoradTab.AddThemeFontSizeOverride("font_size", 13);
        _rankElmoradTab.Pressed += () => SelectRankTab(2);
        tabs.AddChild(_rankElmoradTab);

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        tabs.AddChild(spacer);
        var refresh = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refresh.AddThemeFontSizeOverride("font_size", 12);
        refresh.Pressed += RequestRank;
        tabs.AddChild(refresh);
        root.AddChild(tabs);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        header.AddChild(RankCell("#", 34, UiTheme.TextLo, HorizontalAlignment.Center));
        header.AddChild(RankCell("Name", 150, UiTheme.TextLo));
        header.AddChild(RankCell("Clan", 120, UiTheme.TextLo));
        header.AddChild(RankCell("Loyalty", 70, UiTheme.TextLo, HorizontalAlignment.Right));
        root.AddChild(header);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(380, 320),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        root.AddChild(scroll);
        _rankRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rankRows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_rankRows);

        root.AddChild(new HSeparator());
        _rankMine = UiTheme.Text("", 13, UiTheme.GoldBright);
        root.AddChild(_rankMine);
        _rankStatus = HudStyle.Label(12);
        root.AddChild(_rankStatus);
    }

    private static Label RankCell(string text, int width, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var lbl = UiTheme.Text(text, 13, color, align);
        lbl.CustomMinimumSize = new Vector2(width, 0);
        lbl.ClipText = true;
        return lbl;
    }

    public void ToggleRank()
    {
        if (_rankShown) { CloseRank(); return; }
        OpenRank();
    }

    public void OpenRank()
    {
        if (_rankShown) return;
        int myNation = Net.I.LastEnter.Nation;
        _rankNation = myNation is Nations.Karus or Nations.ElMorad ? myNation : Nations.Karus;
        UpdateRankTabState();

        _rankPanel.Visible = true;
        _rankShown = true;
        RequestRank();
    }

    public void CloseRank()
    {
        if (!_rankShown) return;
        _rankShown = false;
        _rankPanel.Visible = false;
    }

    private void RequestRank()
    {
        _rankStatus.Text = "Loading…";
        Net.I.SendRankRequest(Net.RankTypePkZone);
    }

    private void SelectRankTab(int nation)
    {
        _rankNation = nation;
        UpdateRankTabState();
        RefreshRankRows();
    }

    private void UpdateRankTabState()
    {
        _rankKarusTab.ButtonPressed = _rankNation == Nations.Karus;
        _rankElmoradTab.ButtonPressed = _rankNation == Nations.ElMorad;
    }

    private void OnRankData(IReadOnlyList<RankEntry> karus, IReadOnlyList<RankEntry> elmorad, int myPlace, int myLoyalty)
    {
        _rankKarus.Clear();
        _rankKarus.AddRange(karus);
        _rankElmorad.Clear();
        _rankElmorad.AddRange(elmorad);
        _rankMyPlace = myPlace;
        _rankMyLoyalty = myLoyalty;

        if (_rankShown) RefreshRankRows();
        _rankMine.Text = _rankMyPlace > 0
            ? $"Your rank:  #{_rankMyPlace}    Loyalty {_rankMyLoyalty:n0}"
            : $"Your loyalty:  {_rankMyLoyalty:n0}";
    }

    private void RefreshRankRows()
    {
        foreach (var c in _rankRows.GetChildren()) c.QueueFree();

        var list = _rankNation == Nations.ElMorad ? _rankElmorad : _rankKarus;
        if (list.Count == 0)
        {
            var lbl = HudStyle.Label(13);
            lbl.Text = "No ranked players yet.";
            _rankRows.AddChild(lbl);
            _rankStatus.Text = "";
            return;
        }

        foreach (var e in list)
            _rankRows.AddChild(BuildRankRow(e));
        _rankStatus.Text = $"Top {list.Count} by daily loyalty";
    }

    private Control BuildRankRow(RankEntry e)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiTheme.Row());

        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        row.AddChild(hb);

        Color placeColor = e.Place switch
        {
            1 => UiTheme.GoldBright,
            2 => UiTheme.TextHi,
            3 => UiTheme.Gold,
            _ => UiTheme.TextLo,
        };
        hb.AddChild(RankCell(e.Place.ToString(), 34, placeColor, HorizontalAlignment.Center));
        hb.AddChild(RankCell(e.Name, 150, UiTheme.TextHi));
        hb.AddChild(RankCell(string.IsNullOrEmpty(e.ClanName) ? "—" : e.ClanName, 120, UiTheme.TextLo));
        hb.AddChild(RankCell($"{e.Loyalty:n0}", 70, UiTheme.Gold, HorizontalAlignment.Right));
        return row;
    }
}
