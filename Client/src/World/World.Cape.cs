using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _capeLayer = null!;
    private HudWindow _capePanel = null!;
    private const int CapePanelWidth = 460;
    private const int CapeColourColumns = 9;
    private const int CapeSwatchSize = 46;
    private const int NoCape = -1;
    private const int CapePatternSampleColour = 1;

    private GridContainer _capePatternRow = null!;
    private GridContainer _capeColourGrid = null!;
    private Label _capeChosenLbl = null!, _capeReqLbl = null!, _capePriceLbl = null!;
    private int _capePattern = -1;
    private int _capeChoice = NoCape;
    private HSlider _capeR = null!, _capeG = null!, _capeB = null!;
    private Label _capeRVal = null!, _capeGVal = null!, _capeBVal = null!;
    private Label _capeStatus = null!, _capeHint = null!;
    private bool _capePreviewing;
    private CheckBox _capeTicket = null!;
    private Button _capeBuyBtn = null!;
    private bool _capeShown;
    private bool _capeRequestInFlight;

    private MyClanInfo _capeMyClan;

    private bool CapeImChief => _capeMyClan.InClan
        && string.Equals(_capeMyClan.Chief, Net.I.LastEnter.Name, System.StringComparison.OrdinalIgnoreCase);

    private void CapeInit()
    {
        BuildCapePanel();
        Net.I.CapeResultEvent += OnCapeResult;
        Net.I.MyClanInfoEvent += OnCapeMyClan;
        Net.I.ClanCapeUpdateEvent += OnClanCapeUpdate;
        Net.I.ClanCapeNpcEvent += OnCapeNpc;
    }

    private void CapeDispose()
    {
        Net.I.CapeResultEvent -= OnCapeResult;
        Net.I.MyClanInfoEvent -= OnCapeMyClan;
        Net.I.ClanCapeUpdateEvent -= OnClanCapeUpdate;
        Net.I.ClanCapeNpcEvent -= OnCapeNpc;
    }

    private void BuildCapePanel()
    {
        _capeLayer = new CanvasLayer { Layer = 74 };
        AddChild(_capeLayer);

        _capePanel = new HudWindow("cape", "Clan Cape", new Vector2(220, 130), bodyMinWidth: CapePanelWidth)
        { Visible = false };
        _capePanel.Closed += CloseCape;
        _capeLayer.AddChild(_capePanel);

        var r = _capePanel.Body;
        r.AddThemeConstantOverride("separation", 8);

        r.AddChild(UiTheme.SectionTitle("Pattern"));
        _capePatternRow = new GridContainer { Columns = CapeColourColumns };
        _capePatternRow.AddThemeConstantOverride("h_separation", 4);
        _capePatternRow.AddThemeConstantOverride("v_separation", 4);
        r.AddChild(_capePatternRow);

        r.AddChild(UiTheme.SectionTitle("Colour"));
        var colourScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(1, 132),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        r.AddChild(colourScroll);
        _capeColourGrid = new GridContainer
        {
            Columns = CapeColourColumns,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _capeColourGrid.AddThemeConstantOverride("h_separation", 4);
        _capeColourGrid.AddThemeConstantOverride("v_separation", 4);
        colourScroll.AddChild(_capeColourGrid);

        var chosen = UiTheme.Section();
        chosen.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        r.AddChild(chosen);
        var chosenMargin = new MarginContainer();
        UiTheme.Margins(chosenMargin, 8, 6, 8, 6);
        chosen.AddChild(chosenMargin);
        var chosenCol = new VBoxContainer();
        chosenCol.AddThemeConstantOverride("separation", 1);
        chosenMargin.AddChild(chosenCol);
        _capeChosenLbl = UiTheme.Text("Pick a pattern, then a colour", 14, UiTheme.GoldBright);
        chosenCol.AddChild(_capeChosenLbl);
        _capeReqLbl = UiTheme.Text("Every cape has a clan rank it needs.", 12, UiTheme.TextLo);
        chosenCol.AddChild(_capeReqLbl);
        _capePriceLbl = UiTheme.Text("Dyeing the one you own costs clan points.", 12, UiTheme.TextLo);
        chosenCol.AddChild(_capePriceLbl);
        chosen.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

        r.AddChild(UiTheme.SectionTitle("Dye"));

        _capeR = BuildColorRow(r, "R", out _capeRVal);
        _capeG = BuildColorRow(r, "G", out _capeGVal);
        _capeB = BuildColorRow(r, "B", out _capeBVal);

        _capeTicket = new CheckBox { Text = "Pay with a castellan ticket", FocusMode = Control.FocusModeEnum.None };
        r.AddChild(_capeTicket);

        var actionRow = new HBoxContainer(); actionRow.AddThemeConstantOverride("separation", 8);
        _capeBuyBtn = new Button { Text = "Buy / Apply", FocusMode = Control.FocusModeEnum.None };
        _capeBuyBtn.Pressed += OnCapeBuyPressed;
        actionRow.AddChild(_capeBuyBtn);
        r.AddChild(actionRow);

        _capeHint = UiTheme.Text("", 12, UiTheme.TextLo);
        r.AddChild(_capeHint);
        _capeStatus = HudStyle.Label(13);
        r.AddChild(_capeStatus);

        OnCapeDyeChanged();
        UpdateCapeGate();
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
        slider.ValueChanged += _ => { OnCapeDyeChanged(); };
        row.AddChild(slider);
        valLbl = UiTheme.Text("0", 12, UiTheme.GoldBright);
        valLbl.CustomMinimumSize = new Vector2(34, 0);
        row.AddChild(valLbl);
        parent.AddChild(row);
        return slider;
    }

    private void OnCapeNpc()
    {
        CloseNpcDialog();
        if (!_capeShown) ToggleCape();
    }

    private void ToggleCape()
    {
        if (_capeShown) { CloseCape(); return; }
        _capePanel.Visible = true;
        _capeShown = true;
        _capeRequestInFlight = false;
        SetCapeStatus("", false);
        Net.I.SendClanInfoRequest();

        var worn = Net.I.LastEnter;
        _capeCurrent = worn.CapeId;
        _capeChoice = NoCape;
        _capePreviewing = false;
        _capeR.Value = worn.CapeR;
        _capeG.Value = worn.CapeG;
        _capeB.Value = worn.CapeB;

        BuildCapeCatalogue();
        UpdateCapeGate();
    }

    private void CloseCape()
    {
        if (!_capeShown) return;
        _capeShown = false;
        _capePanel.Visible = false;
        RevertCapePreview();
    }

    private void OnCapeBuyPressed()
    {
        if (_capeRequestInFlight) return;
        if (!CapeImChief) { SetCapeStatus("Only the clan chief can change the cape.", true); return; }

        int capeId = _capeChoice;
        byte rr = (byte)_capeR.Value, gg = (byte)_capeG.Value, bb = (byte)_capeB.Value;
        if (capeId < 0 && rr == 0 && gg == 0 && bb == 0)
        {
            SetCapeStatus("Pick a cape, or choose a dye colour to repaint the one you have.", true);
            return;
        }

        byte op = _capeTicket.ButtonPressed ? Net.CapeOpTicket : Net.CapeOpBuy;
        _capeRequestInFlight = true;
        _capeBuyBtn.Disabled = true;
        SetCapeStatus("Requesting…", false);
        Net.I.SendCapeBuy(op, capeId, rr, gg, bb);
    }

    private void OnCapeResult(bool ok, int a, int capeId, int rr, int gg, int bb)
    {
        _capeRequestInFlight = false;
        UpdateCapeGate();

        if (ok)
        {
            if (capeId >= 0) SelectCape(capeId);
            _capeR.Value = rr; _capeG.Value = gg; _capeB.Value = bb;
            OnCapeDyeChanged();
            var me = Net.I.LastEnter;
            DressCape(_selfVisual, capeId >= 0 ? capeId : me.CapeId, rr, gg, bb, me.Authority == 0, _selfRace);
            _capePreviewing = false;
            _capeCurrent = capeId >= 0 ? capeId : _capeCurrent;
            string what = capeId >= 0 ? $"cape #{capeId}" : "cape dye";
            SetCapeStatus($"Applied {what}.", false);
            Chat.Info($"[Clan] Cape updated ({what}).");
        }
        else
        {
            SetCapeStatus(a switch
            {
                -2 => "You're not in a clan.",
                -5 => "That cape design isn't available.",
                -6 => "Your clan's rank is too low.",
                -7 => "Not enough gold.",
                -8 => "You need a castellan ticket for that.",
                -9 => "Not enough clan points in the fund.",
                -10 => "A castellan can't use that.",
                _ => "The cape change was refused (chief-only, promoted clan, and not while busy).",
            }, true);
        }
    }

    private void OnCapeMyClan(MyClanInfo info)
    {
        _capeMyClan = info;
        if (_capeShown) UpdateCapeGate();
    }

    private static int CapeRankOf(MyClanInfo clan)
    {
        int grade = Mathf.Clamp(clan.Grade <= 0 ? 5 : clan.Grade, 1, 5);
        return clan.Flag switch
        {
            >= 4 => 13 - grade,
            3 => 8 - grade,
            2 => 2,
            _ => 1,
        };
    }

    private static string CapeRankName(int rank) => rank switch
    {
        <= 1 => "Clan",
        2 => "Training Knights",
        <= 7 => $"Accredited Knights grade {8 - rank}",
        <= 12 => $"Royal Knights grade {13 - rank}",
        _ => "Unknown",
    };

    private static Control CapeSwatch(int c, int m, Color dye, bool locked)
    {
        var box = new Control
        {
            CustomMinimumSize = new Vector2(CapeSwatchSize, CapeSwatchSize),
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

    private void BuildCapeCatalogue()
    {
        foreach (var c in _capePatternRow.GetChildren()) c.QueueFree();

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
                CustomMinimumSize = new Vector2(CapeSwatchSize, CapeSwatchSize),
                TooltipText = pattern == 0 ? "Plain" : $"Pattern {pattern}",
            };
            var art = CapeSwatch(CapePatternSampleColour, pattern, Colors.White, locked: false);
            art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            button.AddChild(art);
            button.Pressed += () => ShowCapePattern(which);
            _capePatternRow.AddChild(button);
        }

        ShowCapePattern(patterns.Count > 0 ? (patterns.Contains(_capePattern) ? _capePattern : 0) : -1);
    }

    private void ShowCapePattern(int pattern)
    {
        _capePattern = pattern;
        int index = 0;
        foreach (var child in _capePatternRow.GetChildren())
        {
            if (child is Button b) b.ButtonPressed = index == pattern || (pattern < 0 && index == 0);
            index++;
        }

        foreach (var c in _capeColourGrid.GetChildren()) c.QueueFree();
        int myRank = CapeRankOf(_capeMyClan);

        var ids = new List<int>();
        foreach (var (id, def) in Cape.Catalogue)
            if (def.M == pattern && (def.Price > 0 || def.Points > 0)) ids.Add(id);
        ids.Sort();

        foreach (int id in ids)
        {
            if (!Cape.TryGet(id, out var def)) continue;
            bool locked = _capeMyClan.InClan && myRank < def.Ranking;
            int which = id;

            var cell = new Button
            {
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(CapeSwatchSize, CapeSwatchSize),
                TooltipText = CapeCellTip(def, locked),
            };
            var art = CapeSwatch(def.C, def.M, Colors.White, locked);
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
            cell.Pressed += () => SelectCape(which);
            _capeColourGrid.AddChild(cell);
        }

        if (_capeChoice >= 0 && Cape.TryGet(_capeChoice, out var chosen) && chosen.M == pattern)
            SelectCape(_capeChoice);
    }

    private string CapeCellTip(Cape.CapeDef def, bool locked)
    {
        string cost = def.Points > 0 ? $"{def.Points:n0} clan points" : $"{def.Price:n0} gold";
        string need = $"needs {CapeRankName(def.Ranking)}";
        return locked ? $"{def.Name}\n{cost}\n{need} — your clan is {CapeRankName(CapeRankOf(_capeMyClan))}"
                      : $"{def.Name}\n{cost}\n{need}";
    }

    private int _capeCurrent = NoCape;

    private void SelectCape(int capeId)
    {
        if (!Cape.TryGet(capeId, out var def)) return;
        _capeChoice = capeId;

        int index = 0;
        var ids = new List<int>();
        foreach (var (id, d) in Cape.Catalogue)
            if (d.M == def.M && (d.Price > 0 || d.Points > 0)) ids.Add(id);
        ids.Sort();
        foreach (var child in _capeColourGrid.GetChildren())
        {
            if (child is Button b && index < ids.Count) b.ButtonPressed = ids[index] == capeId;
            index++;
        }

        _capeChosenLbl.Text = def.M > 0 ? $"{def.Name} (pattern {def.M})" : def.Name;
        _capeReqLbl.Text = $"Requires {CapeRankName(def.Ranking)}";
        _capePriceLbl.Text = def.Points > 0
            ? $"{def.Points:n0} clan points"
            : $"{def.Price:n0} gold";

        bool locked = _capeMyClan.InClan && CapeRankOf(_capeMyClan) < def.Ranking;
        _capeReqLbl.AddThemeColorOverride("font_color", locked ? UiTheme.Bad : UiTheme.TextLo);
        RefreshCapePreview();
    }

    private void OnCapeDyeChanged()
    {
        int rr = (int)_capeR.Value, gg = (int)_capeG.Value, bb = (int)_capeB.Value;
        _capeRVal.Text = rr.ToString();
        _capeGVal.Text = gg.ToString();
        _capeBVal.Text = bb.ToString();
        RefreshCapePreview();
    }

    private void RefreshCapePreview()
    {
        if (_capeBuyBtn != null)
            _capeBuyBtn.Text = _capeChoice >= 0 ? "Buy cape" : "Apply dye";

        if (!_capeShown || _selfVisual == null) return;

        int previewId = _capeChoice >= 0 ? _capeChoice : _capeCurrent;
        if (!Cape.IsRenderable(previewId)) return;

        var me = Net.I.LastEnter;
        DressCape(_selfVisual, previewId, (int)_capeR.Value, (int)_capeG.Value, (int)_capeB.Value,
            me.Authority == 0, me.Race, highDetail: true);
        _capePreviewing = true;
    }

    private void RevertCapePreview()
    {
        if (!_capePreviewing) return;
        _capePreviewing = false;
        DressSelfCape();
    }

    private void UpdateCapeGate()
    {
        bool chief = CapeImChief;
        _capeBuyBtn.Disabled = !chief || _capeRequestInFlight;
        if (!_capeMyClan.InClan)
            _capeHint.Text = "Join a clan to buy a cape.";
        else if (!chief)
            _capeHint.Text = "Only the clan chief can change the cape.";
        else if (_capeMyClan.Flag < 2)
            _capeHint.Text = "Your clan must be promoted (Official) before buying a cape.";
        else
            _capeHint.Text = "Custom dye costs 36,000 clan points.";
    }

    private void SetCapeStatus(string text, bool warn)
    {
        _capeStatus.Text = text;
        _capeStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }

    private void BuildCapeTab(VBoxContainer col)
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
        _capeStats = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _capeStats.AddThemeColorOverride("font_color", new Color(0.7f, 0.95f, 1f));
        col.AddChild(_capeStats);
    }

    private Label? _capeStats;

    private void RefreshAllCapes()
    {
        DressSelfCape();
        foreach (var e in _ents.Values)
            if (!e.IsNpc)
                DressCape(e.Body, e.CapeId, e.CapeR, e.CapeG, e.CapeB, e.IsGm, e.Race);
    }

    private void UpdateCapeStats()
    {
        if (_capeStats == null) return;
        var cape = _selfVisual?.GetNodeOrNull<Cape>("Cape");
        _capeStats.Text = cape == null
            ? "no cape on self"
            : $"cloth {cape.Resolution}\ncollider {cape.ColliderSegments()} shapes\n"
            + $"inside body {cape.PenetrationDepth() * 100f:0.0} cm\n"
            + $"swing off drape {cape.DeviationNow() * 100f:0.0} cm";
    }

    private static (int Id, Color Dye) ResolveCape(int capeId, int r, int g, int b, bool isGm)
    {
        if (isGm && !Cape.IsRenderable(capeId)) capeId = Cape.GmCapeId;
        if (capeId == Cape.GmCapeId) return (capeId, Colors.White);
        return (capeId, new Color(r / 255f, g / 255f, b / 255f));
    }

    private static void DressCape(Node3D? body, int capeId, int r, int g, int b, bool isGm, int race,
                                  bool highDetail = false)
    {
        if (body == null || !GodotObject.IsInstanceValid(body)) return;
        var (id, dye) = ResolveCape(capeId, r, g, b, isGm);
        var existing = body.GetNodeOrNull<Cape>("Cape");
        if (!Cape.Enabled || !Cape.IsRenderable(id))
        {
            existing?.QueueFree();
            return;
        }
        // A cape freed earlier this frame is still findable; detach it so the name is free to re-attach.
        if (existing != null && existing.IsQueuedForDeletion())
        {
            body.RemoveChild(existing);
            existing = null;
        }
        if (existing != null && GodotObject.IsInstanceValid(existing))
        {
            existing.SetCape(id, dye);
            return;
        }
        Cape.Attach(body, id, dye, race, highDetail);
    }

    private void DressSelfCape()
    {
        var me = Net.I.LastEnter;
        bool gm = me.Authority == 0;
        DressCape(_selfVisual, me.CapeId, me.CapeR, me.CapeG, me.CapeB, gm, _selfRace, highDetail: true);
        var (id, _) = ResolveCape(me.CapeId, me.CapeR, me.CapeG, me.CapeB, gm);
        GD.Print($"[cape] self: sent={me.CapeId} worn={(Cape.IsRenderable(id) ? id : 0)} "
               + $"dye=({me.CapeR},{me.CapeG},{me.CapeB}) gm={gm} "
               + $"cloth={_selfVisual?.GetNodeOrNull<Cape>("Cape")?.Resolution ?? "-"}");
    }

    private static void DressEntityCape(Node3D body, EntitySnapshot info)
    {
        if (info.IsNpc) return;
        DressCape(body, info.CapeId, info.CapeR, info.CapeG, info.CapeB, info.IsGm, info.Race);
    }

    private void OnClanCapeUpdate(int clanId, int capeId, int r, int g, int b)
    {
        if (_capeMyClan.InClan && _capeMyClan.ClanId == clanId) _capeCurrent = capeId;
        foreach (var e in _ents.Values)
            if (!e.IsNpc && e.KnightsId == clanId && e.KnightsId != 0)
            {
                e.CapeId = capeId; e.CapeR = r; e.CapeG = g; e.CapeB = b;
                DressCape(e.Body, capeId, r, g, b, e.IsGm, e.Race);
            }
        if (_capeMyClan.InClan && _capeMyClan.ClanId == clanId && clanId != 0)
        {
            var me = Net.I.LastEnter;
            DressCape(_selfVisual, capeId, r, g, b, me.Authority == 0, me.Race);
        }
    }
}
