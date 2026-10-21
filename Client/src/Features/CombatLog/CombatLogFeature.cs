using Godot;

namespace LibreKO.Features.CombatLog;

internal sealed class CombatLogFeature : IFeature, ICombatLog
{
    private const float ToolRow = 22f;
    private const float ToolGap = 4f;
    private const int ToolSeparation = 3;
    private const int MaxLines = 120;
    private const int TabMargin = 7;
    private const int TabGap = 3;
    private const int BorderWidth = 1;
    private const int CornerRadius = 4;
    private const float ContentMargin = 7f;
    private const int FontSize = 12;
    private const string TextColour = "#d4d1c9";
    private const string LayoutKey = "hud_combat_log";
    private const string PreviewLayoutKey = "uilab_actual_combat_log";
    private static readonly Vector2 PanelSize = new(420, 205);
    private static readonly Vector2 PanelMinimum = new(290, 135);
    private static readonly Vector2 Edge = new(12, 77);
    private static readonly Vector2 FrameExtra = new(0, ToolRow + ToolGap);
    private static readonly Color EdgeColour = new(UiTheme.Edge, 0.46f);
    private static readonly Color RefusalColour = new("ff3b3b");
    private static readonly (LogTab Tab, string Caption)[] TabCaptions =
    {
        (LogTab.All, "All"), (LogTab.State, "Status"), (LogTab.Item, "Item"), (LogTab.System, "System"),
    };

    private readonly IWorldContext context;
    private readonly ChatSession chat;
    private readonly VanguardSession vanguard;
    private static TabbedLog log = new(MaxLines);
    private static int logOwner = -1;
    private readonly List<string> dropped = new();
    private VBoxContainer frame = null!;
    private PanelContainer root = null!;
    private Button move = null!;
    private Button lockButton = null!;
    private HudLayout? layout;
    private HudLogText text = null!;
    private StyleBoxFlat panelStyle = null!;

    public CombatLogFeature(IWorldContext context, ChatSession chat, VanguardSession vanguard)
    {
        this.context = context;
        this.chat = chat;
        this.vanguard = vanguard;
    }

    internal Control Frame => frame;

    internal static Vector2 Spot(Vector2 viewport)
    {
        Vector2 size = PanelSize + FrameExtra;
        return new Vector2(
            Mathf.Max(0f, viewport.X - Edge.X - size.X),
            Mathf.Max(0f, viewport.Y - Edge.Y - size.Y));
    }

    public void Build()
    {
        if (logOwner != context.Self.CharId)
        {
            log = new TabbedLog(MaxLines);
            logOwner = context.Self.CharId;
        }
        var layer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(layer);
        context.Hud.PluginSeam(layer, HudPart.CombatLog);

        frame = new VBoxContainer
        {
            Size = PanelSize + FrameExtra,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddThemeConstantOverride("separation", (int)ToolGap);
        layer.AddChild(frame);

        var tools = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, ToolRow),
        };
        tools.AddThemeConstantOverride("separation", ToolSeparation);
        var tabs = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tabs.AddThemeConstantOverride("separation", TabGap);
        var tabGroup = new ButtonGroup();
        foreach (var (tab, caption) in TabCaptions)
        {
            var button = new Button { Text = caption, ToggleMode = true, ButtonGroup = tabGroup, ButtonPressed = tab == log.Shown };
            HudToolButton.Style(button, TabMargin);
            button.Pressed += () => ShowTab(tab);
            tabs.AddChild(button);
        }
        tools.AddChild(tabs);
        tools.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var background = HudToolButton.Create("◐", null, "Background opacity");
        background.Pressed += CycleBackground;
        tools.AddChild(background);
        move = HudToolButton.Create("", "system/move", "Drag to move the combat log");
        tools.AddChild(move);
        lockButton = HudToolButton.Create("", "system/unlock", "Lock position and size");
        lockButton.Pressed += () => SetLocked(!Config.CombatLogLocked);
        tools.AddChild(lockButton);
        frame.AddChild(tools);

        root = new PanelContainer
        {
            CustomMinimumSize = PanelMinimum,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        panelStyle = new StyleBoxFlat { BorderColor = EdgeColour };
        panelStyle.SetBorderWidthAll(BorderWidth);
        panelStyle.SetCornerRadiusAll(CornerRadius);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            panelStyle.Set($"content_margin_{side}", ContentMargin);
        root.AddThemeStyleboxOverride("panel", panelStyle);
        frame.AddChild(root);
        ApplyBackground();

        text = new HudLogText
        {
            ScrollbarOnLeft = false,
            BbcodeEnabled = true,
            ScrollActive = true,
            ScrollFollowing = true,
            FitContent = false,
            SelectionEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        text.AddThemeFontSizeOverride("normal_font_size", FontSize);
        text.AddThemeColorOverride("default_color", new Color(TextColour));
        root.AddChild(text);
        if (log.ShownLines.Count > 0) text.Text = string.Join("\n", log.ShownLines);
        text.MetaHoverStarted += meta => MetaHoverStarted?.Invoke(meta);
        text.MetaHoverEnded += _ => context.Tooltips.Hide();

        AttachLayout(frame, () => Spot(context.Root.GetViewport().GetVisibleRect().Size));

        frame.Visible = Config.CombatLog;
        Config.EffectsChanged += ApplyVisibility;
        chat.DeathNoticeEvent += OnDeathNotice;
    }

    public void Dispose()
    {
        Config.EffectsChanged -= ApplyVisibility;
        chat.DeathNoticeEvent -= OnDeathNotice;
    }

    public void Refusal(int textId, string fallback) => Add(ItemData.Text(textId, fallback), CombatLogKind.Status, RefusalColour);

    public void Notice(string message) => Notice(message, null);

    public void Notice(string message, Color? tint)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        context.Floaters?.Notice(message);
        Add(message, CombatLogKind.Status, tint);
    }

    public void Add(string message, CombatLogKind kind, Color? tint = null, string? markup = null, LogTab tab = LogTab.System)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        string color = tint?.ToHtml(false) ?? kind switch
        {
            CombatLogKind.Damage or CombatLogKind.Outgoing => "ffffff",
            CombatLogKind.Incoming or CombatLogKind.Spent => "ff3b3b",
            CombatLogKind.Recovery or CombatLogKind.Resource => "6565ff",
            CombatLogKind.National => "9b9bff",
            _ => "ffff00",
        };
        string line = $"[color=#{color}]{markup ?? BbCode.Esc(message)}[/color]";
        bool shown = log.Add(line, tab, dropped);
        if (text != null && shown)
        {
            bool rebuild = false;
            foreach (string droppedLine in dropped)
                for (int n = droppedLine.Split('\n').Length; n > 0 && !rebuild; n--)
                    rebuild = !text.RemoveParagraph(0, true);
            if (rebuild) text.Text = string.Join("\n", log.ShownLines);
            else
            {
                if (log.ShownLines.Count > 1) text.AppendText("\n");
                text.AppendText(line);
            }
        }
        context.Plugins.LogAdd(tab switch
        {
            LogTab.State => GameLogKind.Status,
            LogTab.Item => GameLogKind.Item,
            _ => GameLogKind.System,
        }, message, new Color("#" + color));
    }

    internal void AttachLayout(Control target, Func<Vector2> defaultPosition, bool persist = true)
    {
        target.Modulate = Colors.White;
        layout = HudLayout.Attach(
            target, persist ? LayoutKey : PreviewLayoutKey, move, defaultPosition,
            resizable: true,
            defaultSize: PanelSize + FrameExtra,
            minimumSize: PanelMinimum + FrameExtra,
            persist: persist,
            resizeCorner: HudLayout.Corner.TopLeft,
            resizeGripOffset: FrameExtra,
            moveGripOverlay: false);
        ApplyLock();
    }

    internal event Action<Variant>? MetaHoverStarted;

    private void OnDeathNotice(DeathNotice n)
    {
        bool listed = vanguard.ForgetWanted(n.VictimName);
        int textId = DeathNotices.TextFor(n.Kind, context.Zone, n.VictimIsKing, n.KillerNation, listed);
        string template = ItemData.Text(textId, "- %s defeat %s ( %d, %d ) -");
        string killer = NationColouredName(n.KillerName, n.KillerNation);
        string victim = NationColouredName(n.VictimName, n.VictimNation);
        string plain, markup;
        if (textId is Vanguard.KarusSlainText or Vanguard.ElMoradSlainText)
        {
            plain = TextTemplate.Fill(Vanguard.Plain(template), n.KillerName, n.VictimName, n.X, n.Z);
            markup = TextTemplate.Fill(Vanguard.Markup(BbCode.Esc(template)), BbCode.Esc(n.KillerName), BbCode.Esc(n.VictimName), n.X, n.Z);
        }
        else if (n.Kind == DeathNotices.DefeatedKind)
        {
            plain = TextTemplate.Fill(template, n.VictimName, n.KillerName);
            markup = TextTemplate.Fill(BbCode.Esc(template), victim, killer);
        }
        else
        {
            plain = TextTemplate.Fill(template, n.KillerName, n.VictimName, n.X, n.Z);
            markup = TextTemplate.Fill(BbCode.Esc(template), killer, victim, n.X, n.Z);
        }
        var colour = new Color("#" + DeathNotices.LineColourFor(context.Zone, n.KillerId == context.Self.CharId, n.Kind));
        context.Floaters?.Notice(plain);
        Add(plain, CombatLogKind.Status, colour, markup);
    }

    private static string NationColouredName(string name, int nation) =>
        DeathNotices.NameColour(nation) is { } colour ? $"[color=#{colour}]{BbCode.Esc(name)}[/color]" : BbCode.Esc(name);

    private void ApplyVisibility()
    {
        if (frame != null && GodotObject.IsInstanceValid(frame))
            frame.Visible = Config.CombatLog;
    }

    private void CycleBackground()
    {
        Config.SetCombatLogBackground((Config.CombatLogBackground + 1) % ChatPrefs.Backgrounds.Length);
        ApplyBackground();
    }

    private void ApplyBackground()
    {
        float alpha = ChatPrefs.Backgrounds[Config.CombatLogBackground];
        panelStyle.BgColor = new Color(0, 0, 0, alpha);
        panelStyle.BorderColor = alpha > 0 ? EdgeColour : Colors.Transparent;
    }

    private void SetLocked(bool locked)
    {
        Config.SetCombatLogLocked(locked);
        ApplyLock();
    }

    private void ApplyLock()
    {
        bool locked = Config.CombatLogLocked;
        if (layout != null) layout.Locked = locked;
        move.Visible = !locked;
        HudToolButton.ShowLocked(lockButton, locked);
    }

    private void ShowTab(LogTab tab)
    {
        if (!log.Show(tab) || text == null) return;
        text.Text = string.Join("\n", log.ShownLines);
    }
}
