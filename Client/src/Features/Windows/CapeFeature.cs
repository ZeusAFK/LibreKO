using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class CapeFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly CapeSession cape;
    private readonly ClanSession clan;
    private readonly GmFxSession gmFx;
    private readonly SelfSession self;
    private readonly ICapeRequests requests;
    private readonly EntityLooksSystem entityLooks;
    private readonly IInteractionDialogs interactionDialogs;

    public CapeFeature(IWorldContext context, CapeSession cape, ClanSession clan, GmFxSession gmFx, SelfSession self, ICapeRequests requests, EntityLooksSystem entityLooks, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.cape = cape;
        this.clan = clan;
        this.gmFx = gmFx;
        this.self = self;
        this.requests = requests;
        this.entityLooks = entityLooks;
        this.interactionDialogs = interactionDialogs;
    }

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;

    private const int PanelWidth = 460;
    private const int ColourColumns = 9;
    private const int SwatchSize = 46;
    private const int No = -1;
    private const int PatternSampleColour = 1;
    private const int BusyText = 16810;

    private GridContainer patternRow = null!;
    private GridContainer colourGrid = null!;
    private Label chosenLbl = null!;
    private Label reqLbl = null!;
    private Label priceLbl = null!;
    private Label contLbl = null!;
    private Label noteLbl = null!;
    private int pattern = -1;
    private int choice = No;
    private HSlider redSlider = null!;
    private HSlider greenSlider = null!;
    private HSlider blueSlider = null!;
    private Label rVal = null!;
    private Label gVal = null!;
    private Label bVal = null!;
    private Label status = null!;

    private Label hint = null!;
    private bool previewing;
    private CheckBox ticket = null!;
    private Button buyBtn = null!;
    private bool shown;

    public bool IsOpen => shown;

    private bool requestInFlight;

    private bool ImChief => clan.MyClan.IsChief;

    private void OnGraphicsChanged()
    {
        if (Cape.Enabled == Config.Capes) return;
        Cape.Enabled = Config.Capes;
        RefreshAllCapes();
    }

    public void Build()
    {
        Config.GraphicsChanged += OnGraphicsChanged;
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        cape.CapeResultEvent += OnResult;
        clan.MyClanChangedEvent += OnMyClan;
        clan.ClanCapeUpdateEvent += OnClanUpdate;
        clan.ClanCapeNpcEvent += OnNpc;
        cape.CapeResetEvent += OnReset;
    }

    public void Dispose()
    {
        Config.GraphicsChanged -= OnGraphicsChanged;
        cape.CapeResultEvent -= OnResult;
        clan.MyClanChangedEvent -= OnMyClan;
        clan.ClanCapeUpdateEvent -= OnClanUpdate;
        clan.ClanCapeNpcEvent -= OnNpc;
        cape.CapeResetEvent -= OnReset;
    }

    private void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("cape", "Clan Cape", bodyMinWidth: PanelWidth)
        { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var r = Panel.Body;
        r.AddThemeConstantOverride("separation", 8);

        r.AddChild(UiTheme.SectionTitle("Pattern"));
        patternRow = new GridContainer { Columns = ColourColumns };
        patternRow.AddThemeConstantOverride("h_separation", 4);
        patternRow.AddThemeConstantOverride("v_separation", 4);
        r.AddChild(patternRow);

        r.AddChild(UiTheme.SectionTitle("Colour"));
        var colourScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(1, 132),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        r.AddChild(colourScroll);
        colourGrid = new GridContainer
        {
            Columns = ColourColumns,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        colourGrid.AddThemeConstantOverride("h_separation", 4);
        colourGrid.AddThemeConstantOverride("v_separation", 4);
        colourScroll.AddChild(colourGrid);

        var chosen = UiTheme.Section();
        chosen.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        r.AddChild(chosen);
        var chosenMargin = new MarginContainer();
        UiTheme.Margins(chosenMargin, 8, 6, 8, 6);
        chosen.AddChild(chosenMargin);
        var chosenCol = new VBoxContainer();
        chosenCol.AddThemeConstantOverride("separation", 1);
        chosenMargin.AddChild(chosenCol);
        chosenLbl = UiTheme.Text("Pick a pattern, then a colour", 14, UiTheme.GoldBright);
        chosenCol.AddChild(chosenLbl);
        reqLbl = UiTheme.Text("Every cape has a clan rank it needs.", 12, UiTheme.TextLo);
        chosenCol.AddChild(reqLbl);
        priceLbl = UiTheme.Text("Dyeing the one you own costs clan points.", 12, UiTheme.TextLo);
        chosenCol.AddChild(priceLbl);
        contLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        chosenCol.AddChild(contLbl);
        noteLbl = UiTheme.Text("", 12, UiTheme.TextHi);
        noteLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        chosenCol.AddChild(noteLbl);
        chosen.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

        r.AddChild(UiTheme.SectionTitle("Dye"));

        redSlider = BuildColorRow(r, "R", out rVal);
        greenSlider = BuildColorRow(r, "G", out gVal);
        blueSlider = BuildColorRow(r, "B", out bVal);

        ticket = new CheckBox { Text = "Pay with a castellan ticket", FocusMode = Control.FocusModeEnum.None };
        r.AddChild(ticket);

        var actionRow = new HBoxContainer(); actionRow.AddThemeConstantOverride("separation", 8);
        buyBtn = new Button { Text = "Buy / Apply", FocusMode = Control.FocusModeEnum.None };
        buyBtn.Pressed += OnBuyPressed;
        actionRow.AddChild(buyBtn);
        r.AddChild(actionRow);

        hint = UiTheme.Text("", 12, UiTheme.TextLo);
        r.AddChild(hint);
        status = HudStyle.Label(13);
        r.AddChild(status);

        OnDyeChanged();
        UpdateGate();
        PluginHost.Game.AttachCapeShop(new PluginShopBridge(this));
    }

    private HSlider BuildColorRow(VBoxContainer parent, string channel, out Label valLbl)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        var lbl = HudStyle.Label(13); lbl.Text = channel; lbl.CustomMinimumSize = new Vector2(70, 0);
        row.AddChild(lbl);
        var slider = new HSlider
        {
            MinValue = 0, MaxValue = 255, Step = 1, Value = 0,
            CustomMinimumSize = new Vector2(160, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        slider.ValueChanged += _ => { OnDyeChanged(); };
        row.AddChild(slider);
        valLbl = UiTheme.Text("0", 12, UiTheme.GoldBright);
        valLbl.CustomMinimumSize = new Vector2(34, 0);
        row.AddChild(valLbl);
        parent.AddChild(row);
        return slider;
    }

    private void OnNpc()
    {
        context.NpcDialog.Close();
        if (!shown) Toggle();
    }

    private void Toggle()
    {
        if (shown) { Close(); return; }
        Show();
        SetStatus("", false);

        var worn = self.LastEnter;
        current = worn.CapeId;
        choice = No;
        previewing = false;
        redSlider.Value = worn.CapeR;
        greenSlider.Value = worn.CapeG;
        blueSlider.Value = worn.CapeB;

        BuildCatalogue();
        UpdateGate();
        PluginNotifyShop();
    }

    internal void Show()
    {
        Panel.Visible = true;
        shown = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        RevertPreview();
        PluginNotifyShop();
    }

    private void OnBuyPressed()
    {
        if (requestInFlight) return;
        if (!ImChief) { SetStatus("Only the clan chief can change the cape.", true); return; }

        int capeId = choice;
        byte rr = (byte)redSlider.Value, gg = (byte)greenSlider.Value, bb = (byte)blueSlider.Value;
        if (capeId < 0 && rr == 0 && gg == 0 && bb == 0)
        {
            SetStatus("Pick a cape, or choose a dye colour to repaint the one you have.", true);
            return;
        }

        bool painting = rr != 0 || gg != 0 || bb != 0;
        bool castle = Cape.TryGet(capeId, out var def) && def.Castle;
        if (ticket.ButtonPressed)
        {
            if (capeId < 0 || !Domain.CapeShop.VoucherAllowed(castle))
            {
                SetStatus(ItemData.Text(Domain.CapeShop.VoucherRefusedText, "You cannot purchase this item using exchange voucher"), true);
                return;
            }
            Notice.Confirm(context.Root, ItemData.Text(Domain.CapeShop.VoucherConfirmText, "Do you wish to purchase with the exchange voucher?"),
                "OK", "Cancel", () => SendBuy(CapeSubOpcode.TicketPurchase, capeId, rr, gg, bb));
            return;
        }
        string cont = ContLine(capeId < 0 ? Domain.CapeShop.PaintCont : Domain.CapeShop.ContCost(def.Points, painting));
        string noah = NoahLine(capeId < 0 ? 0 : def.Price);
        int confirm = Domain.CapeShop.ConfirmText(castle);
        string fallback = castle
            ? "If you are not a member of current Castellan clan, \n%s\n%s will be used.\\Do you wish to continue with the purchase?"
            : "Purchase price:\n%s\n%s\n Do you wish to purchase it?";
        Notice.Confirm(context.Root, Domain.CapeShop.Fill(ItemData.Text(confirm, fallback), cont, noah), "OK", "Cancel",
            () => SendBuy(CapeSubOpcode.NormalPurchase, capeId, rr, gg, bb));
    }

    private static string ContLine(int cont) =>
        Domain.CapeShop.Fill(ItemData.Text(Domain.CapeShop.ContLineText, "%s Clan CONT"), Domain.CapeShop.Amount(cont));

    private static string NoahLine(int coins) =>
        Domain.CapeShop.Fill(ItemData.Text(Domain.CapeShop.NoahLineText, "%s Noah"), Domain.CapeShop.Amount(coins));

    private void SendBuy(CapeSubOpcode op, int capeId, byte rr, byte gg, byte bb)
    {
        if (requestInFlight) return;
        if (!requests.Buy((byte)op, capeId, rr, gg, bb))
        {
            SetStatus(ItemData.Text(BusyText, "Try again later"), true);
            return;
        }
        requestInFlight = true;
        buyBtn.Disabled = true;
        SetStatus("Requesting…", false);
    }

    private void OnResult(CapeChangeReply reply)
    {
        if (!requestInFlight) return;
        requestInFlight = false;
        UpdateGate();

        if (reply.Succeeded)
        {
            if (shown)
            {
                if (reply.CapeId >= 0) Select(reply.CapeId);
                redSlider.Value = reply.R; greenSlider.Value = reply.G; blueSlider.Value = reply.B;
                OnDyeChanged();
            }
            var me = self.LastEnter;
            entityLooks.Dress(context.Self.Visual, reply.CapeId >= 0 ? reply.CapeId : me.CapeId, reply.R, reply.G, reply.B, false, me.Race);
            previewing = shown;
            current = reply.CapeId >= 0 ? reply.CapeId : current;
            string done = ItemData.Text(TextChanged, "Cape has been changed");
            SetStatus(done, false);
            context.Log.Notice(done);
        }
        else
        {
            string refusal = Refusal(reply.ClanOrResult);
            SetStatus(refusal, true);
            context.Log.Notice(refusal);
        }
    }

    private const int TextChanged = 10320;

    private static string Refusal(int result) => result switch
    {
        -1 => ItemData.Text(10305, "You don't have the authorization to purchase a cape."),
        -2 => ItemData.Text(10306, "Cannot find this Knights."),
        -5 => ItemData.Text(10307, "You cannot buy this from the store."),
        -6 => ItemData.Text(10308, "Your Knights ranking is too low to purchase a cape."),
        -7 => ItemData.Text(10309, "You don't have enough Coins to purchase a cape."),
        -8 => ItemData.Text(10310, "You don't have the necessary item for purchasing a cape."),
        -9 => ItemData.Text(15714, "CONT is insufficient."),
        -10 => ItemData.Text(10323, "Castellan can't use it"),
        _ => ItemData.Text(10304, "You cannot purchase a cape."),
    };

    private void OnReset()
    {
        requestInFlight = false;
        if (shown) UpdateGate();
    }

    private void OnMyClan(MyClanInfo info)
    {
        if (!shown) return;
        UpdateGate();
        if (pattern >= 0) ShowPattern(pattern);
    }

    private bool Allowed(Cape.CapeDef def) =>
        ClanTypes.MeetsCapeRank(clan.MyClan.Flag, clan.MyClan.Grade, def.Ranking, def.Grade);

    private static string NeedName(Cape.CapeDef def) =>
        def.Ranking <= ClanTypes.Promoted && def.Grade > 0
            ? $"{RankName(def.Ranking)} grade {def.Grade}"
            : RankName(def.Ranking);

    private static string RankName(int rank) => rank switch
    {
        <= 1 => "Clan",
        2 => "Training Knights",
        <= 7 => $"Accredited Knights grade {8 - rank}",
        <= 12 => $"Royal Knights grade {13 - rank}",
        _ => "Unknown",
    };

    private static Control Swatch(int c, int m, Color dye, bool locked)
    {
        var box = new Control
        {
            CustomMinimumSize = new Vector2(SwatchSize, SwatchSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var cloth = Cape.ClothTexture(c);
        if (cloth != null)
        {
            var baseTex = new TextureRect
            {
                Texture = cloth,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = dye * (locked ? 0.4f : 1f),
            };
            baseTex.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            box.AddChild(baseTex);
        }
        var mark = Cape.MarkTexture(m);
        if (mark != null)
        {
            var over = new TextureRect
            {
                Texture = mark,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = new Color(1f, 1f, 1f, locked ? 0.4f : 1f),
            };
            over.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            box.AddChild(over);
        }
        return box;
    }

    internal void BuildCatalogue()
    {
        foreach (var c in patternRow.GetChildren()) c.QueueFree();

        var patterns = new SortedSet<int>();
        foreach (var (_, def) in Cape.Catalogue)
            if (def.Price > 0 || def.Points > 0) patterns.Add(def.M);

        foreach (int pattern in patterns)
        {
            int which = pattern;
            var button = new Button
            {
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(SwatchSize, SwatchSize),
                TooltipText = pattern == 0 ? "Plain" : $"Pattern {pattern}",
            };
            var art = Swatch(PatternSampleColour, pattern, Colors.White, locked: false);
            art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            button.AddChild(art);
            button.Pressed += () => ShowPattern(which);
            patternRow.AddChild(button);
        }

        ShowPattern(patterns.Count > 0 ? (patterns.Contains(pattern) ? pattern : 0) : -1);
    }

    internal void ShowPattern(int pattern)
    {
        this.pattern = pattern;
        int index = 0;
        foreach (var child in patternRow.GetChildren())
        {
            if (child is Button b) b.ButtonPressed = index == pattern || (pattern < 0 && index == 0);
            index++;
        }

        foreach (var c in colourGrid.GetChildren()) c.QueueFree();

        var ids = new List<int>();
        foreach (var (id, def) in Cape.Catalogue)
            if (def.M == pattern && (def.Price > 0 || def.Points > 0)) ids.Add(id);
        ids.Sort();

        foreach (int id in ids)
        {
            if (!Cape.TryGet(id, out var def)) continue;
            bool locked = clan.MyClan.InClan && !Allowed(def);
            int which = id;

            var cell = new Button
            {
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(SwatchSize, SwatchSize),
                TooltipText = CellTip(def, locked),
            };
            var art = Swatch(def.C, def.M, Colors.White, locked);
            art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            cell.AddChild(art);
            if (locked)
            {
                var bar = UiTheme.Text("locked", 10, UiTheme.TextDim);
                bar.MouseFilter = Control.MouseFilterEnum.Ignore;
                bar.HorizontalAlignment = HorizontalAlignment.Center;
                bar.VerticalAlignment = VerticalAlignment.Bottom;
                bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                cell.AddChild(bar);
            }
            cell.Pressed += () => Select(which);
            colourGrid.AddChild(cell);
        }

        if (choice >= 0 && Cape.TryGet(choice, out var chosen) && chosen.M == pattern)
            Select(choice);
    }

    private string CellTip(Cape.CapeDef def, bool locked)
    {
        string cost = $"{NoahLine(def.Price)}\n{ContLine(def.Points)}";
        string need = Domain.CapeShop.Fill(ItemData.Text(Domain.CapeShop.GradeLineText, "Purchase Grade: %s and above"), NeedName(def));
        return locked ? $"{def.Name}\n{cost}\n{need} — your clan is {ClanTypes.Standing(clan.MyClan.Flag, clan.MyClan.Grade)}"
                      : $"{def.Name}\n{cost}\n{need}";
    }

    private int current = No;

    internal void Select(int capeId)
    {
        if (!Cape.TryGet(capeId, out var def)) return;
        choice = capeId;

        int index = 0;
        var ids = new List<int>();
        foreach (var (id, d) in Cape.Catalogue)
            if (d.M == def.M && (d.Price > 0 || d.Points > 0)) ids.Add(id);
        ids.Sort();
        foreach (var child in colourGrid.GetChildren())
        {
            if (child is Button b && index < ids.Count) b.ButtonPressed = ids[index] == capeId;
            index++;
        }

        chosenLbl.Text = def.M > 0 ? $"{def.Name} (pattern {def.M})" : def.Name;
        reqLbl.Text = Domain.CapeShop.Fill(ItemData.Text(Domain.CapeShop.GradeLineText, "Purchase Grade: %s and above"), NeedName(def));
        bool worn = capeId == current;
        priceLbl.Text = worn ? "" : NoahLine(def.Price);
        contLbl.Text = worn ? "" : ContLine(def.Points);
        noteLbl.Text = worn ? ItemData.Text(Domain.CapeShop.NowEquippedText, "Now equipped") : def.Desc;

        bool locked = clan.MyClan.InClan && !Allowed(def);
        reqLbl.AddThemeColorOverride("font_color", locked ? UiTheme.Bad : UiTheme.TextLo);
        RefreshPreview();
        PluginNotifyShop();
    }

    private void OnDyeChanged()
    {
        int rr = (int)redSlider.Value, gg = (int)greenSlider.Value, bb = (int)blueSlider.Value;
        rVal.Text = rr.ToString();
        gVal.Text = gg.ToString();
        bVal.Text = bb.ToString();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (buyBtn != null)
            buyBtn.Text = choice >= 0 ? "Buy cape" : "Apply dye";

        if (!shown || context.Self.Visual == null) return;

        int previewId = choice >= 0 ? choice : current;
        if (!Cape.IsRenderable(previewId)) return;

        var me = self.LastEnter;
        entityLooks.Dress(context.Self.Visual, previewId, (int)redSlider.Value, (int)greenSlider.Value, (int)blueSlider.Value,
            false, me.Race, highDetail: true);
        previewing = true;
    }

    private void RevertPreview()
    {
        if (!previewing) return;
        previewing = false;
        entityLooks.DressSelf();
    }

    internal void UpdateGate()
    {
        bool chief = ImChief;
        buyBtn.Disabled = !chief || requestInFlight;
        if (!clan.MyClan.InClan)
            hint.Text = "Join a clan to buy a cape.";
        else if (!chief)
            hint.Text = "Only the clan chief can change the cape.";
        else if (clan.MyClan.Flag < ClanTypes.Promoted)
            hint.Text = "Your clan must be promoted (Official) before buying a cape.";
        else
            hint.Text = "Custom dye costs 36,000 clan points.";
    }

    private void SetStatus(string text, bool warn)
    {
        status.Text = text;
        PluginNotifyShop();
        status.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }

    internal void BuildTab(VBoxContainer col)
    {
        col.AddChild(new Label { Text = "Cape" });

        var enabled = new CheckBox
        {
            Text = "Enable capes",
            ButtonPressed = Cape.Enabled,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Off frees every attached cape (self + everyone in range), "
                        + "so the cloth simulation stops costing frame time too.",
        };
        enabled.Toggled += on =>
        {
            Cape.Enabled = on;
            RefreshAllCapes();
        };
        col.AddChild(enabled);

        var dbg = new CheckBox { Text = "Show collider + drape", FocusMode = Control.FocusModeEnum.None };
        dbg.Toggled += on => Cape.DrawDebug = on;
        col.AddChild(dbg);
        col.AddChild(new Label
        {
            Text = "green = body capsules\nyellow = equipment contacts\n"
                 + "cyan = simulated cloth grid",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        stats = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        stats.AddThemeColorOverride("font_color", new Color(0.7f, 0.95f, 1f));
        col.AddChild(stats);
    }

    private Label? stats;

    internal void RefreshAllCapes()
    {
        entityLooks.DressSelf();
        foreach (var e in context.Entities.PlayerCapes())
            entityLooks.Dress(e.Body, e.CapeId, e.R, e.G, e.B, e.IsGm, e.Race);
    }

    internal void UpdateStats()
    {
        if (stats == null) return;
        var capeNode = context.Self.Visual?.GetNodeOrNull<Cape>("Cape");
        stats.Text = capeNode == null
            ? "no cape on self"
            : $"cloth {capeNode.Resolution}\ncollider {capeNode.ColliderSegments()} shapes\n"
            + $"inside body {capeNode.PenetrationDepth() * 100f:0.0} cm\n"
            + $"swing off drape {capeNode.DeviationNow() * 100f:0.0} cm";
    }

    private void OnClanUpdate(ClanCapeChange change)
    {
        if (clan.MyClan.InClan && clan.MyClan.ClanId == change.ClanId) current = change.CapeId;
        foreach (var e in context.Entities.PlayerCapes())
            if (e.KnightsId == change.ClanId && e.KnightsId != 0)
            {
                context.Entities.SetCape(e.Id, change.CapeId, change.R, change.G, change.B);
                entityLooks.Dress(e.Body, change.CapeId, change.R, change.G, change.B, e.IsGm, e.Race);
            }
        if (clan.MyClan.InClan && clan.MyClan.ClanId == change.ClanId && change.ClanId != 0)
        {
            var me = self.LastEnter;
            entityLooks.Dress(context.Self.Visual, change.CapeId, change.R, change.G, change.B, gmFx.Visible(context.Self.CharId, context.Self.IsGm), me.Race);
        }
    }

    private void PluginNotifyShop() => Callable.From(PluginHost.Game.RaiseCapeShop).CallDeferred();

    private void BuyWithVoucher()
    {
        ticket.ButtonPressed = true;
        OnBuyPressed();
        ticket.ButtonPressed = false;
    }

    private sealed class PluginShopBridge : IGameCapeShop
    {
        private readonly CapeFeature cape;

        public PluginShopBridge(CapeFeature cape) => this.cape = cape;

        public bool IsOpen => cape.IsOpen;

        public IReadOnlyList<GameCape> Capes
        {
            get
            {
                var capes = new List<GameCape>();
                foreach (var (id, def) in Cape.Catalogue)
                    if (def.Price > 0 || def.Points > 0)
                        capes.Add(new GameCape(id, def.C, def.M, def.Name, cape.clan.MyClan.InClan && !cape.Allowed(def), def.Castle));
                capes.Sort((a, b) => a.Id.CompareTo(b.Id));
                return capes;
            }
        }

        public int Choice => cape.choice;
        public int Current => cape.current;
        public string MoneyLine => cape.priceLbl.Text;
        public string ContLine => cape.contLbl.Text;
        public string GradeLine => cape.reqLbl.Text;
        public bool GradeShort => Cape.TryGet(cape.choice, out var def) && cape.clan.MyClan.InClan && !cape.Allowed(def);
        public string Notice => cape.noteLbl.Text;
        public string Status => cape.status.Text.Length > 0 ? cape.status.Text : cape.hint.Text;
        public bool CanBuy => !cape.buyBtn.Disabled;
        public bool CanVoucher => !cape.buyBtn.Disabled && Cape.TryGet(cape.choice, out var def) && def.Castle;
        public void Select(int capeId) => cape.Select(capeId);
        public void Buy() => cape.OnBuyPressed();
        public void Voucher() => cape.BuyWithVoucher();
        public void Close() => cape.Close();

        event Action? IGameCapeShop.Changed { add { } remove { } }
    }
}
