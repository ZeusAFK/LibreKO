using Godot;

namespace LibreKO.Features.Windows;

internal sealed class DailyRankFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly DailyRankSession dailyRank;
    private readonly IDailyRankRequests requests;
    private readonly TopIconsFeature topIcons;

    public DailyRankFeature(IWorldContext context, DailyRankSession dailyRank, IDailyRankRequests requests, TopIconsFeature topIcons)
    {
        this.context = context;
        this.dailyRank = dailyRank;
        this.requests = requests;
        this.topIcons = topIcons;
    }

    public void Tick(double delta, double nowSec)
    {
        DailyRankTick(delta);
    }

    private const string Art = "res://assets/ui/daily_rank/";
    private const string BoardsFile = Art + "boards.json";
    private const int NoBoard = -1;
    private const double BlinkSeconds = 1.0;
    private const int InfoLines = 3;
    private static readonly Vector2 Origin = new(44, 62);
    private static readonly Vector2 Canvas = new(495, 400);
    private static readonly Color InfoColor = new("ead98a");
    private static readonly Color PodiumColor = new("e4da96");
    private static readonly Color MineColor = new("adff2f");
    private static readonly Color UpColor = new("cd5c5c");
    private static readonly Color DownColor = new("87ceeb");

    private static readonly int[] RowTops = { 188, 212, 234, 256, 278, 300, 322, 343 };

    private static readonly int[] PodiumTops = { 185, 246, 306 };

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow? Window { get; set; }
    private Button drIcon = null!;
    private TextureRect drIconImage = null!;
    internal List<DailyRankBoardInfo> DrBoards { get; } = new();

    internal Dictionary<int, DailyRankBoardData> DrCache { get; } = new();

    private int drCurrent = NoBoard;
    private double drLastRequest = double.NegativeInfinity;
    private bool drBlink;
    private bool drBlinkLit;
    private double drBlinkIn;
    private RichTextLabel drInfo = null!;
    private OptionButton drPicker = null!;
    private readonly Label[] drPodium = new Label[DailyRank.PodiumPlaces];
    private readonly Label[] drRanks = new Label[DailyRank.RowsPerPage];
    private readonly Label[] drNames = new Label[DailyRank.RowsPerPage];
    private VScrollBar drScroll = null!;
    private Label drMyLabel = null!;
    private Label drMyPlace = null!;
    private Label drChange = null!;
    private TextureRect drChangeIcon = null!;
    private Texture2D? drUp;
    private Texture2D? drDown;
    private Texture2D? drSame;

    public void Build()
    {
        Layer = new CanvasLayer { Layer = UiLayers.SideWindows };
        context.Root.AddChild(Layer);
        LoadBoards();
        drIcon = topIcons.Add(TopIconSlot.DailyRank, "system/medal", GameText.System(DailyRank.TitleText, "Daily Rank"),
            OnIcon, out drIconImage);
        drIcon.Resized += PlaceIcon;
        Callable.From(PlaceIcon).CallDeferred();
        drBlink = DailyRank.ShouldBlink(Config.DailyRankLastViewDay, DateTime.Now);
        dailyRank.DailyRankBoardEvent += OnBoard;
        dailyRank.DailyRankResetEvent += OnReset;
    }

    public void Dispose()
    {
        dailyRank.DailyRankBoardEvent -= OnBoard;
        dailyRank.DailyRankResetEvent -= OnReset;
    }

    internal void LoadBoards()
    {
        DrBoards.Clear();
        using var file = Godot.FileAccess.Open(BoardsFile, Godot.FileAccess.ModeFlags.Read);
        if (file == null) return;
        foreach (var row in Json.ParseString(file.GetAsText()).AsGodotArray())
        {
            var board = row.AsGodotDictionary();
            var lines = board["lines"].AsGodotArray().Select(line => line.AsString()).ToArray();
            DrBoards.Add(new DailyRankBoardInfo(board["id"].AsInt32(), board["title"].AsString(), lines));
        }
    }

    private void PlaceIcon() => topIcons.Place(TopIconSlot.DailyRank, HudPlacement.DailyRankIcon);

    private void OnIcon()
    {
        Toggle();
        Config.SetDailyRankLastViewDay(DailyRank.DayKey(DateTime.Now));
        drBlink = false;
        drIconImage.SelfModulate = UiKit.TopIconColor(false);
    }

    private void DailyRankTick(double delta)
    {
        if (!drBlink || !GodotObject.IsInstanceValid(drIconImage)) return;
        drBlinkIn -= delta;
        if (drBlinkIn > 0) return;
        drBlinkIn = BlinkSeconds;
        drBlinkLit = !drBlinkLit;
        drIconImage.SelfModulate = UiKit.TopIconColor(drBlinkLit);
    }

    private void Toggle()
    {
        if (Window is { Visible: true })
        {
            Window.Visible = false;
            return;
        }
        Window ??= BuildDailyRank();
        if (drCurrent == NoBoard) ShowBoard(DailyRank.FirstBoard);
        else if (DailyRank.AlwaysRequested(drCurrent)) ShowBoard(drCurrent);
        else Render(drCurrent);
        UiKit.ShowOnTop(Window);
    }

    private void ShowBoard(int board)
    {
        if (!DailyRank.AlwaysRequested(board) && DrCache.ContainsKey(board))
        {
            Render(board);
            return;
        }
        double now = FrameClock.Now();
        double waited = now - drLastRequest;
        if (waited < DailyRank.RequestGapSeconds)
        {
            context.Log.Notice(GameText.System(DailyRank.TooSoonText, "It is available in %d seconds")
                .Replace("%d", ((int)(DailyRank.RequestGapSeconds - waited)).ToString()), UiTheme.Bad);
            return;
        }
        drLastRequest = now;
        requests.DailyRankRequest(board);
    }

    private void OnBoard(DailyRankBoardData data)
    {
        DrCache[data.Board] = data;
        if (Window != null) Render(data.Board);
    }

    private void OnReset()
    {
        DrCache.Clear();
        drCurrent = NoBoard;
        if (Window != null) Window.Visible = false;
    }

    internal HudWindow BuildDailyRank()
    {
        drUp = GD.Load<Texture2D>(Art + "change_up.png");
        drDown = GD.Load<Texture2D>(Art + "change_down.png");
        drSame = GD.Load<Texture2D>(Art + "change_same.png");

        var window = new HudWindow("daily_rank", GameText.System(DailyRank.TitleText, "Daily Rank"), new Vector2(320, 110)) { Visible = false };
        window.Closed += () => window.Visible = false;
        Layer.AddChild(window);
        var canvas = new Control { CustomMinimumSize = Canvas, MouseFilter = Control.MouseFilterEnum.Pass };
        window.Body.AddChild(canvas);

        drInfo = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        drInfo.AddThemeColorOverride("default_color", InfoColor);
        drInfo.AddThemeFontSizeOverride("normal_font_size", 12);
        Place(canvas, drInfo, new Rect2(54, 69, 473, 52));

        drPicker = UiTheme.Dropdown(DrBoards.Select(board => board.Title).ToList());
        drPicker.ItemSelected += index => ShowBoard(DrBoards[(int)index].Id);
        Place(canvas, drPicker, new Rect2(300, 125, 231, 26));

        var podium = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        podium.AddThemeStyleboxOverride("panel", UiTheme.Panel());
        Place(canvas, podium, new Rect2(50, 181, 212, 189));
        for (int i = 0; i < DailyRank.PodiumPlaces; i++)
        {
            var medal = new TextureRect
            {
                Texture = GD.Load<Texture2D>($"{Art}medal_{i + 1}.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            Place(canvas, medal, new Rect2(54, PodiumTops[i], 60, 60));
            drPodium[i] = UiTheme.Text("", 14, PodiumColor);
            drPodium[i].VerticalAlignment = VerticalAlignment.Center;
            drPodium[i].ClipText = true;
            Place(canvas, drPodium[i], new Rect2(121, PodiumTops[i], 144, 60));
        }

        var list = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        list.AddThemeStyleboxOverride("panel", UiTheme.Panel());
        list.GuiInput += OnListInput;
        Place(canvas, list, new Rect2(267, 181, 264, 197));
        for (int i = 0; i < DailyRank.RowsPerPage; i++)
        {
            drRanks[i] = UiTheme.Text("", 12, UiTheme.TextHi, HorizontalAlignment.Center);
            Place(canvas, drRanks[i], new Rect2(271, RowTops[i], 40, 18));
            drNames[i] = UiTheme.Text("", 12, UiTheme.TextHi);
            drNames[i].ClipText = true;
            Place(canvas, drNames[i], new Rect2(324, RowTops[i], 170, 18));
        }
        drScroll = new VScrollBar { MinValue = 0, Step = 1, Page = 1, MouseFilter = Control.MouseFilterEnum.Stop };
        drScroll.ValueChanged += _ => RenderRows();
        Place(canvas, drScroll, new Rect2(505, 185, 16, 190));

        drMyLabel = UiTheme.Text("", 12, MineColor, HorizontalAlignment.Right);
        Place(canvas, drMyLabel, new Rect2(161, 386, 112, 20));
        drMyPlace = UiTheme.Text("", 12, MineColor, HorizontalAlignment.Center);
        Place(canvas, drMyPlace, new Rect2(279, 386, 59, 20));
        drChangeIcon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Place(canvas, drChangeIcon, new Rect2(341, 384, 24, 24));
        drChange = UiTheme.Text("", 12, UiTheme.TextHi);
        Place(canvas, drChange, new Rect2(367, 386, 60, 20));

        var footer = UiTheme.Text(GameText.System(DailyRank.FooterText, "Daily Rank updates everyday and resets every month"),
            12, InfoColor, HorizontalAlignment.Center);
        footer.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Place(canvas, footer, new Rect2(54, 426, 473, 30));
        return window;
    }

    private static void Place(Control canvas, Control child, Rect2 reference)
    {
        canvas.AddChild(child);
        child.Position = reference.Position - Origin;
        child.Size = reference.Size;
        child.CustomMinimumSize = reference.Size;
    }

    private void OnListInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } wheel) return;
        if (wheel.ButtonIndex == MouseButton.WheelUp) drScroll.Value -= 1;
        else if (wheel.ButtonIndex == MouseButton.WheelDown) drScroll.Value += 1;
    }

    internal void Render(int board)
    {
        if (Window == null) return;
        drCurrent = board;
        var info = DrBoards.FirstOrDefault(b => b.Id == board);
        if (info != null)
        {
            drInfo.Text = string.Join("\n", info.Lines.Take(InfoLines));
            drPicker.Select(DrBoards.IndexOf(info));
        }

        DrCache.TryGetValue(board, out var data);
        var entries = data?.Entries ?? new List<DailyRankEntry>();
        for (int i = 0; i < DailyRank.PodiumPlaces; i++)
            drPodium[i].Text = i < entries.Count ? entries[i].Name : "";

        drMyLabel.Text = board == DailyRank.ClanBoard
            ? GameText.System(DailyRank.MyClanRankText, "My Clan Rank")
            : GameText.System(DailyRank.MyRankText, "My Rank:");
        drMyPlace.Text = DailyRank.PlaceShown(data?.MyPlace ?? 0);
        var (text, icon) = DailyRank.ChangeShown(board, data?.Change ?? 0);
        drChange.Text = text;
        drChange.AddThemeColorOverride("font_color", icon switch
        {
            DailyRankChangeIcon.Up => UpColor,
            DailyRankChangeIcon.Down => DownColor,
            _ => UiTheme.TextHi,
        });
        drChangeIcon.Texture = icon switch
        {
            DailyRankChangeIcon.Up => drUp,
            DailyRankChangeIcon.Down => drDown,
            DailyRankChangeIcon.Same => drSame,
            _ => null,
        };

        drScroll.MaxValue = DailyRank.PageCount(entries.Count);
        drScroll.SetValueNoSignal(0);
        RenderRows();
    }

    private void RenderRows()
    {
        var entries = DrCache.TryGetValue(drCurrent, out var data) ? data.Entries : new List<DailyRankEntry>();
        var page = DailyRank.Page(entries, (int)drScroll.Value).ToList();
        for (int i = 0; i < DailyRank.RowsPerPage; i++)
        {
            drRanks[i].Text = i < page.Count ? page[i].Rank.ToString() : "";
            drNames[i].Text = i < page.Count ? page[i].Name : "";
        }
    }
}
