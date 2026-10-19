using Godot;

namespace LibreKO.Features.Hud;

internal sealed partial class PartyWarBoardFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly PartySession party;
    private readonly PartyWarBoardSession partyWarBoard;
    private readonly SelfSession self;
    private readonly IPartyWarBoardRequests warBoardRequests;
    private readonly PartyFeature partyHud;

    public PartyWarBoardFeature(IWorldContext context, PartySession party, PartyWarBoardSession partyWarBoard, SelfSession self, IPartyWarBoardRequests partyWarBoardRequests, PartyFeature partyHud)
    {
        this.context = context;
        this.party = party;
        this.partyWarBoard = partyWarBoard;
        this.self = self;
        warBoardRequests = partyWarBoardRequests;
        this.partyHud = partyHud;
    }

    public void Tick(double delta, double nowSec) => WarBoardTick(nowSec);

    private const string WarBoardLayoutId = "party_war_board";
    private const string WarBoardArt = "res://assets/ui/war_board/";
    private const float WarBoardWidth = 295f;
    private const float WarBoardRowHeight = 17f;
    private const float WarBoardButtonSize = 16f;
    private const float WarBoardAlphaWidth = 64f;
    private const int WarBoardTitleFont = 12;
    private const int WarBoardRowFont = 11;
    private const int WarBoardCoolFont = 36;
    private const float WarBoardTextInset = 4f;
    private static readonly Vector2 WarBoardDefaultPos = new(HudAnchor.Edge + UiKit.PartyHudWidth + 12f, UiKit.PartyHudTop);
    private static readonly Color WarBoardCoolColour = Color.Color8(255, 128, 128);
    private static readonly Color MeterDamageFill = Color.Color8(182, 132, 13);

    private static readonly Dictionary<int, Color> WarBoardClassFills = new()
    {
        [1] = Color.Color8(112, 9, 33),
        [2] = Color.Color8(47, 98, 48),
        [3] = Color.Color8(109, 54, 112),
        [4] = Color.Color8(0, 57, 112),
        [5] = Color.Color8(116, 99, 34),
    };

    internal sealed class WarBoardRow
    {
        public HBoxContainer Line = null!;
        public TextureRect Icon = null!;
        public WarBoardBar Bar = null!;
        public Label Name = null!;
        public Label Percent = null!;
    }

    internal Dictionary<int, int> WarBoardRowShares { get; } = new();

    internal List<GameWarBoardRow> WarBoardLines { get; } = new();
    private readonly WarBoardRow[] warBoardRows = new WarBoardRow[PartyWarBoard.MaxRows];
    internal PanelContainer? Panel { get; set; }
    internal TextureButton WarBoardDeal { get; private set; } = null!;
    private TextureButton warBoardHeal = null!;
    private Label warBoardCool = null!;
    internal PartyBoardMeterMode WarBoardMode { get; set; } = Config.PartyBoardMode;
    private bool warBoardPending;
    internal int WarBoardCooldown { get; private set; }
    private bool warBoardIn;
    private double meterToggleSentAt = double.NegativeInfinity;
    private double warBoardSentAt = double.NegativeInfinity;

    public void Build()
    {
        partyHud.Attach(this);
        if (!PluginHost.Ui.HudHidden(LibreKO.Plugins.HudPart.Party)) KeepRowMeterOff();
        partyWarBoard.PartyWarBoardEvent += OnWarBoard;
        party.PartyMemberEvent += OnWarBoardMember;
        party.PartyRemovedEvent += OnWarBoardRemoved;
        party.PartyDisbandEvent += OnWarBoardGone;
        warBoardIn = party.InParty;
        Panel ??= BuildWarBoard();
    }

    public void Dispose()
    {
        partyHud.Detach(this);
        partyWarBoard.PartyWarBoardEvent -= OnWarBoard;
        party.PartyMemberEvent -= OnWarBoardMember;
        party.PartyRemovedEvent -= OnWarBoardRemoved;
        party.PartyDisbandEvent -= OnWarBoardGone;
    }

    private static Texture2D? WarBoardTexture(string name) =>
        ResourceLoader.Load<Texture2D>(WarBoardArt + name + ".png");

    private static TextureButton WarBoardButton(string normal, string hover, string tooltip)
    {
        var button = new TextureButton
        {
            TextureNormal = WarBoardTexture(normal),
            TextureHover = WarBoardTexture(hover),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(WarBoardButtonSize, WarBoardButtonSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
        };
        return button;
    }

    private void KeepRowMeterOff()
    {
        if (Config.PartyRowMeter == PartyBoardMeterMode.Off) return;
        if (party.InParty) warBoardRequests.Send(PartyWarBoardTarget.Rows, PartyBoardMeterMode.Off);
        else Config.SetPartyRowMeter(PartyBoardMeterMode.Off);
    }

    internal void ToggleRowMeter()
    {
        double left = PartyWarBoard.SecondsLeft(meterToggleSentAt, context.Now, PartyWarBoard.RowToggleSeconds);
        if (left > 0)
        {
            context.Log.Notice(AvailableInText(left));
            return;
        }
        meterToggleSentAt = context.Now;
        warBoardRequests.Send(PartyWarBoardTarget.Rows, PartyWarBoard.NextRowMode(Config.PartyRowMeter));
        WarBoardRowShares.Clear();
        context.Plugins.Party();
    }

    private static string AvailableInText(double secondsLeft) =>
        ItemData.Text(PartyWarBoard.TextAvailableIn, "It is available in %d seconds")
        .Replace("%d", Math.Ceiling(secondsLeft).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void OnWarBoard(WarBoardReply reply)
    {
        if (reply.Target == PartyWarBoardTarget.Rows)
        {
            if (reply.Mode != Config.PartyRowMeter) return;
            WarBoardRowShares.Clear();
            foreach (var share in reply.Shares) WarBoardRowShares[share.MemberId] = share.Share;
            context.Plugins.Party();
            return;
        }
        if (reply.Target == PartyWarBoardTarget.Board && Panel is { Visible: true } && reply.Mode == WarBoardMode)
            FillWarBoard(reply.Shares);
    }

    private void OnWarBoardMember(PartyMember member)
    {
        if (warBoardIn || !party.InParty) return;
        warBoardIn = true;
        if (Config.PartyRowMeter != PartyBoardMeterMode.Damage)
            warBoardRequests.Send(PartyWarBoardTarget.Rows, Config.PartyRowMeter);
        if (Panel is { Visible: true }) SendWarBoardMode();
    }

    private void OnWarBoardRemoved(int memberId, string who)
    {
        if (memberId == context.Self.CharId || !party.InParty) OnWarBoardGone();
    }

    private void OnWarBoardGone()
    {
        warBoardIn = false;
        WarBoardRowShares.Clear();
        warBoardPending = false;
        WarBoardLines.Clear();
        if (Panel != null)
        {
            Panel.Visible = false;
            ShowWarBoardCooldown(0);
        }
        context.Plugins.Party();
    }

    internal void ToggleWarBoard()
    {
        if (Panel is { Visible: true })
        {
            CloseWarBoard();
            return;
        }
        Panel ??= BuildWarBoard();
        Panel.Visible = true;
        FillWarBoard(Array.Empty<WarBoardShare>());
        partyHud.RefreshConfig();
        context.Plugins.Party();
        RequestWarBoardMode(WarBoardMode == PartyBoardMeterMode.Off ? PartyWarBoard.DefaultBoardMode(self.Class) : WarBoardMode);
    }

    private void CloseWarBoard()
    {
        if (Panel == null) return;
        Panel.Visible = false;
        warBoardPending = false;
        ShowWarBoardCooldown(0);
        if (party.InParty) warBoardRequests.Send(PartyWarBoardTarget.Board, PartyBoardMeterMode.Off);
        partyHud.RefreshConfig();
        context.Plugins.Party();
    }

    internal void RequestWarBoardMode(PartyBoardMeterMode mode)
    {
        WarBoardMode = mode;
        Config.SetPartyBoardMode(mode);
        if (Panel != null)
        {
            WarBoardDeal.ButtonPressed = mode == PartyBoardMeterMode.Damage;
            warBoardHeal.ButtonPressed = mode == PartyBoardMeterMode.Healing;
        }
        context.Plugins.Party();
        if (PartyWarBoard.SecondsLeft(warBoardSentAt, context.Now, PartyWarBoard.BoardTabSeconds) > 0)
        {
            warBoardPending = true;
            return;
        }
        SendWarBoardMode();
    }

    private void SendWarBoardMode()
    {
        warBoardPending = false;
        warBoardSentAt = context.Now;
        if (party.InParty) warBoardRequests.Send(PartyWarBoardTarget.Board, WarBoardMode);
    }

    private void WarBoardTick(double now)
    {
        if (Panel == null || !Panel.Visible) return;
        double left = PartyWarBoard.SecondsLeft(warBoardSentAt, now, PartyWarBoard.BoardTabSeconds);
        if (warBoardPending && left <= 0) SendWarBoardMode();
        int cooldown = PartyWarBoard.CooldownShown(warBoardPending, left);
        if (cooldown == WarBoardCooldown) return;
        ShowWarBoardCooldown(cooldown);
        context.Plugins.Party();
    }

    private void ShowWarBoardCooldown(int seconds)
    {
        WarBoardCooldown = seconds;
        warBoardCool.Visible = seconds > 0;
        warBoardCool.Text = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal PanelContainer BuildWarBoard()
    {
        var layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);
        context.Hud.PluginSeam(layer, LibreKO.Plugins.HudPart.WarBoard);
        var panel = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(WarBoardWidth, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        layer.AddChild(panel);

        var body = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 1);
        panel.AddChild(body);

        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        head.AddThemeConstantOverride("separation", 5);
        body.AddChild(head);
        var title = UiTheme.Text("Party War Board", WarBoardTitleFont, UiTheme.GoldBright);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        var alpha = new HSlider
        {
            MinValue = PartyWarBoard.MinAlpha,
            MaxValue = 1,
            Step = 0.05,
            Value = Config.PartyBoardAlpha,
            CustomMinimumSize = new Vector2(WarBoardAlphaWidth, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Background transparency",
        };
        panel.SelfModulate = new Color(1, 1, 1, Config.PartyBoardAlpha);
        warBoardAlpha = alpha;
        alpha.ValueChanged += value =>
        {
            panel.SelfModulate = new Color(1, 1, 1, (float)value);
            Config.SetPartyBoardAlpha((float)value);
        };
        head.AddChild(alpha);
        WarBoardDeal = WarBoardTab("deal", "Damage dealt", PartyBoardMeterMode.Damage);
        head.AddChild(WarBoardDeal);
        warBoardHeal = WarBoardTab("heal", "Healing", PartyBoardMeterMode.Healing);
        head.AddChild(warBoardHeal);
        var close = WarBoardButton("close", "close_hover", "Close");
        close.TexturePressed = WarBoardTexture("close_pressed");
        close.Pressed += CloseWarBoard;
        head.AddChild(close);

        var stack = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        body.AddChild(stack);
        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 1);
        stack.AddChild(rows);
        for (int i = 0; i < PartyWarBoard.MaxRows; i++)
        {
            warBoardRows[i] = BuildWarBoardRow();
            rows.AddChild(warBoardRows[i].Line);
        }
        warBoardCool = UiTheme.Text("", WarBoardCoolFont, WarBoardCoolColour, HorizontalAlignment.Center);
        warBoardCool.VerticalAlignment = VerticalAlignment.Center;
        warBoardCool.MouseFilter = Control.MouseFilterEnum.Ignore;
        warBoardCool.Visible = false;
        stack.AddChild(warBoardCool);

        HudLayout.Attach(panel, WarBoardLayoutId, head, () => WarBoardDefaultPos);
        return panel;
    }

    private TextureButton WarBoardTab(string art, string tooltip, PartyBoardMeterMode mode)
    {
        var tab = WarBoardButton(art + "_off", art + "_hover", tooltip);
        tab.ToggleMode = true;
        tab.TexturePressed = WarBoardTexture(art + "_on");
        tab.Pressed += () => RequestWarBoardMode(mode);
        return tab;
    }

    private static WarBoardRow BuildWarBoardRow()
    {
        var row = new WarBoardRow
        {
            Line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore },
            Icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(WarBoardButtonSize, WarBoardButtonSize),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            },
            Bar = new WarBoardBar(PartyFeature.BarTrack)
            {
                CustomMinimumSize = new Vector2(0, WarBoardRowHeight),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            },
            Name = UiTheme.Text("", WarBoardRowFont, Colors.White),
            Percent = UiTheme.Text("", WarBoardRowFont, Colors.White, HorizontalAlignment.Right),
        };
        row.Line.AddThemeConstantOverride("separation", 4);
        row.Line.AddChild(row.Icon);
        row.Line.AddChild(row.Bar);
        foreach (var label in new[] { row.Name, row.Percent })
        {
            label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            label.OffsetLeft = label.OffsetRight = 0;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.AddThemeConstantOverride("outline_size", 3);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            row.Bar.AddChild(label);
        }
        row.Name.OffsetLeft = WarBoardTextInset;
        row.Percent.OffsetRight = -WarBoardTextInset;
        row.Name.ClipText = true;
        row.Line.Visible = false;
        return row;
    }

    private HSlider? warBoardAlpha;

    internal void SetWarBoardAlpha(float alpha)
    {
        if (warBoardAlpha != null) warBoardAlpha.Value = alpha;
        else Config.SetPartyBoardAlpha(alpha);
        context.Plugins.Party();
    }

    internal void FillWarBoard(IReadOnlyList<WarBoardShare> shares)
    {
        var ranked = PartyWarBoard.Ranked(shares);
        WarBoardLines.Clear();
        for (int i = 0; i < PartyWarBoard.MaxRows; i++)
        {
            var row = warBoardRows[i];
            bool used = i < ranked.Count && MemberOf(ranked[i].MemberId) is not null;
            row.Line.Visible = used;
            if (!used) continue;
            var share = ranked[i];
            var member = MemberOf(share.MemberId)!.Value;
            int cls = member.CharId == context.Self.CharId ? self.Class : member.Class;
            row.Icon.Texture = UiIcons.Get(PartyHud.ClassIcon(cls));
            row.Icon.TooltipText = CharacterClassCatalog.DisplayName(cls);
            row.Bar.Fill = WarBoardClassFills.GetValueOrDefault(CharacterClassCatalog.Family(cls), MeterDamageFill);
            row.Bar.Value = PartyWarBoard.Fill(share.Share);
            row.Name.Text = PartyWarBoard.RowName(i + 1, member.Name);
            row.Percent.Text = PartyWarBoard.Percent(share.Share);
            WarBoardLines.Add(new GameWarBoardRow(i + 1, member.Name, cls, share.Share));
        }
        context.Plugins.Party();
    }

    private PartyMember? MemberOf(int memberId)
    {
        foreach (var member in party.Members)
            if (member.CharId == memberId) return member;
        return null;
    }

    internal sealed partial class WarBoardBar : Control
    {
        private readonly Color track;
        private float value;
        private Color fill = Colors.White;

        public WarBoardBar(Color track) => this.track = track;

        public float Value
        {
            get => value;
            set { this.value = value; QueueRedraw(); }
        }

        public Color Fill
        {
            get => fill;
            set { fill = value; QueueRedraw(); }
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), track);
            if (value > 0) DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * value, Size.Y)), fill);
        }
    }
}
