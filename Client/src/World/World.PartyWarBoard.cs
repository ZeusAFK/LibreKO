using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
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
    private static readonly Vector2 WarBoardDefaultPos = new(HudAnchor.Edge + PartyHudWidth + 12f, PartyHudTop);
    private static readonly Color WarBoardCoolColour = Color.Color8(255, 128, 128);
    private static readonly Color MeterDamageFill = Color.Color8(182, 132, 13);
    private static readonly Color MeterHealingFill = Color.Color8(29, 165, 162);
    private static readonly Dictionary<int, Color> WarBoardClassFills = new()
    {
        [1] = Color.Color8(112, 9, 33),
        [2] = Color.Color8(47, 98, 48),
        [3] = Color.Color8(109, 54, 112),
        [4] = Color.Color8(0, 57, 112),
        [5] = Color.Color8(116, 99, 34),
    };

    private sealed class WarBoardRow
    {
        public HBoxContainer Line = null!;
        public TextureRect Icon = null!;
        public WarBoardBar Bar = null!;
        public Label Name = null!;
        public Label Percent = null!;
    }

    private readonly Dictionary<int, int> _warBoardRowShares = new();
    private readonly List<GameWarBoardRow> _warBoardLines = new();
    private readonly WarBoardRow[] _warBoardRows = new WarBoardRow[PartyWarBoard.MaxRows];
    private TextureButton _meterToggle = null!;
    private PanelContainer? _warBoard;
    private TextureButton _warBoardDeal = null!;
    private TextureButton _warBoardHeal = null!;
    private Label _warBoardCool = null!;
    private byte _warBoardMode = Config.PartyBoardMode;
    private bool _warBoardPending;
    private int _warBoardCooldown;
    private bool _warBoardInParty;
    private double _meterToggleSentAt = double.NegativeInfinity;
    private double _warBoardSentAt = double.NegativeInfinity;

    private void PartyWarBoardInit()
    {
        Net.I.PartyWarBoardEvent += OnPartyWarBoard;
        Net.I.PartyMemberEvent += OnWarBoardPartyMember;
        Net.I.PartyRemovedEvent += OnWarBoardPartyRemoved;
        Net.I.PartyDisbandEvent += OnWarBoardPartyGone;
        _warBoardInParty = InParty;
        _warBoard ??= BuildWarBoard();
    }

    private void PartyWarBoardDispose()
    {
        Net.I.PartyWarBoardEvent -= OnPartyWarBoard;
        Net.I.PartyMemberEvent -= OnWarBoardPartyMember;
        Net.I.PartyRemovedEvent -= OnWarBoardPartyRemoved;
        Net.I.PartyDisbandEvent -= OnWarBoardPartyGone;
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

    private void AddMeterButtons(HBoxContainer headRow)
    {
        _meterToggle = WarBoardButton("meter_toggle", "meter_toggle_hover", "");
        _meterToggle.Pressed += ToggleRowMeter;
        headRow.AddChild(_meterToggle);
        var board = WarBoardButton("meter_board", "meter_board_hover", "Party War Board");
        board.Pressed += ToggleWarBoard;
        headRow.AddChild(board);
        RefreshMeterToggle();
    }

    private void RefreshMeterToggle()
    {
        byte mode = Net.I.PartyRowMeterMode;
        _meterToggle.TextureNormal = WarBoardTexture(mode == PartyWarBoard.ModeOff ? "meter_toggle_off" : "meter_toggle");
        _meterToggle.TooltipText = mode switch
        {
            PartyWarBoard.ModeDamage => "Party meter: damage dealt",
            PartyWarBoard.ModeHealing => "Party meter: healing",
            _ => "Party meter: off",
        };
    }

    private void FillPartyMeter(PartyMeter meter, int memberId)
    {
        byte mode = Net.I.PartyRowMeterMode;
        meter.Visible = mode != PartyWarBoard.ModeOff;
        if (!meter.Visible) return;
        meter.Fill = mode == PartyWarBoard.ModeHealing ? MeterHealingFill : MeterDamageFill;
        meter.Target = PartyWarBoard.Fill(_warBoardRowShares.GetValueOrDefault(memberId));
    }

    private void ToggleRowMeter()
    {
        double left = PartyWarBoard.SecondsLeft(_meterToggleSentAt, Now(), PartyWarBoard.RowToggleSeconds);
        if (left > 0)
        {
            CombatNotice(AvailableInText(left));
            return;
        }
        _meterToggleSentAt = Now();
        Net.I.SendPartyWarBoard(PartyWarBoard.TargetRows, PartyWarBoard.NextRowMode(Net.I.PartyRowMeterMode));
        _warBoardRowShares.Clear();
        RefreshMeterToggle();
        RefreshPartyHud();
        PluginNotifyParty();
    }

    private static string AvailableInText(double secondsLeft) =>
        ItemData.Text(PartyWarBoard.TextAvailableIn, "It is available in %d seconds")
            .Replace("%d", Math.Ceiling(secondsLeft).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void OnPartyWarBoard(WarBoardReply reply)
    {
        if (reply.Target == PartyWarBoard.TargetRows)
        {
            if (reply.Mode != Net.I.PartyRowMeterMode) return;
            _warBoardRowShares.Clear();
            foreach (var share in reply.Shares) _warBoardRowShares[share.MemberId] = share.Share;
            RefreshPartyHud();
            PluginNotifyParty();
            return;
        }
        if (reply.Target == PartyWarBoard.TargetBoard && _warBoard is { Visible: true } && reply.Mode == _warBoardMode)
            FillWarBoard(reply.Shares);
    }

    private void OnWarBoardPartyMember(PartyMember member)
    {
        if (_warBoardInParty || !InParty) return;
        _warBoardInParty = true;
        if (Net.I.PartyRowMeterMode != PartyWarBoard.ModeDamage)
            Net.I.SendPartyWarBoard(PartyWarBoard.TargetRows, Net.I.PartyRowMeterMode);
        if (_warBoard is { Visible: true }) SendWarBoardMode();
    }

    private void OnWarBoardPartyRemoved(int memberId, string who)
    {
        if (memberId == _myId || !InParty) OnWarBoardPartyGone();
    }

    private void OnWarBoardPartyGone()
    {
        _warBoardInParty = false;
        _warBoardRowShares.Clear();
        _warBoardPending = false;
        _warBoardLines.Clear();
        if (_warBoard != null)
        {
            _warBoard.Visible = false;
            ShowWarBoardCooldown(0);
        }
        PluginNotifyParty();
    }

    private void ToggleWarBoard()
    {
        if (_warBoard is { Visible: true })
        {
            CloseWarBoard();
            return;
        }
        _warBoard ??= BuildWarBoard();
        _warBoard.Visible = true;
        FillWarBoard(Array.Empty<WarBoardShare>());
        PluginNotifyParty();
        RequestWarBoardMode(_warBoardMode == PartyWarBoard.ModeOff ? PartyWarBoard.DefaultBoardMode(_selfClass) : _warBoardMode);
    }

    private void CloseWarBoard()
    {
        if (_warBoard == null) return;
        _warBoard.Visible = false;
        _warBoardPending = false;
        ShowWarBoardCooldown(0);
        if (InParty) Net.I.SendPartyWarBoard(PartyWarBoard.TargetBoard, PartyWarBoard.ModeOff);
        PluginNotifyParty();
    }

    private void RequestWarBoardMode(byte mode)
    {
        _warBoardMode = mode;
        Config.SetPartyBoardMode(mode);
        _warBoardDeal.ButtonPressed = mode == PartyWarBoard.ModeDamage;
        _warBoardHeal.ButtonPressed = mode == PartyWarBoard.ModeHealing;
        PluginNotifyParty();
        if (PartyWarBoard.SecondsLeft(_warBoardSentAt, Now(), PartyWarBoard.BoardTabSeconds) > 0)
        {
            _warBoardPending = true;
            return;
        }
        SendWarBoardMode();
    }

    private void SendWarBoardMode()
    {
        _warBoardPending = false;
        _warBoardSentAt = Now();
        if (InParty) Net.I.SendPartyWarBoard(PartyWarBoard.TargetBoard, _warBoardMode);
    }

    private void WarBoardTick(double now)
    {
        if (_warBoard == null || !_warBoard.Visible) return;
        double left = PartyWarBoard.SecondsLeft(_warBoardSentAt, now, PartyWarBoard.BoardTabSeconds);
        if (_warBoardPending && left <= 0) SendWarBoardMode();
        int cooldown = PartyWarBoard.CooldownShown(_warBoardPending, left);
        if (cooldown == _warBoardCooldown) return;
        ShowWarBoardCooldown(cooldown);
        PluginNotifyParty();
    }

    private void ShowWarBoardCooldown(int seconds)
    {
        _warBoardCooldown = seconds;
        _warBoardCool.Visible = seconds > 0;
        _warBoardCool.Text = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private PanelContainer BuildWarBoard()
    {
        var layer = new CanvasLayer { Layer = 64 };
        AddChild(layer);
        PluginHudSeam(layer, LibreKO.Plugins.HudPart.WarBoard);
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
        _warBoardAlpha = alpha;
        alpha.ValueChanged += value =>
        {
            panel.SelfModulate = new Color(1, 1, 1, (float)value);
            Config.SetPartyBoardAlpha((float)value);
        };
        head.AddChild(alpha);
        _warBoardDeal = WarBoardTab("deal", "Damage dealt", PartyWarBoard.ModeDamage);
        head.AddChild(_warBoardDeal);
        _warBoardHeal = WarBoardTab("heal", "Healing", PartyWarBoard.ModeHealing);
        head.AddChild(_warBoardHeal);
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
            _warBoardRows[i] = BuildWarBoardRow();
            rows.AddChild(_warBoardRows[i].Line);
        }
        _warBoardCool = UiTheme.Text("", WarBoardCoolFont, WarBoardCoolColour, HorizontalAlignment.Center);
        _warBoardCool.VerticalAlignment = VerticalAlignment.Center;
        _warBoardCool.MouseFilter = Control.MouseFilterEnum.Ignore;
        _warBoardCool.Visible = false;
        stack.AddChild(_warBoardCool);

        HudLayout.Attach(panel, WarBoardLayoutId, head, () => WarBoardDefaultPos);
        return panel;
    }

    private TextureButton WarBoardTab(string art, string tooltip, byte mode)
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
            Bar = new WarBoardBar(PartyBarTrack)
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

    private HSlider? _warBoardAlpha;

    private void SetWarBoardAlpha(float alpha)
    {
        if (_warBoardAlpha != null) _warBoardAlpha.Value = alpha;
        else Config.SetPartyBoardAlpha(alpha);
        PluginNotifyParty();
    }

    private void FillWarBoard(IReadOnlyList<WarBoardShare> shares)
    {
        var ranked = PartyWarBoard.Ranked(shares);
        _warBoardLines.Clear();
        for (int i = 0; i < PartyWarBoard.MaxRows; i++)
        {
            var row = _warBoardRows[i];
            bool used = i < ranked.Count && MemberOf(ranked[i].MemberId) is not null;
            row.Line.Visible = used;
            if (!used) continue;
            var share = ranked[i];
            var member = MemberOf(share.MemberId)!.Value;
            int cls = member.CharId == _myId ? _selfClass : member.Class;
            row.Icon.Texture = UiIcons.Get(PartyHud.ClassIcon(cls));
            row.Icon.TooltipText = ClassName(cls);
            row.Bar.Fill = WarBoardClassFills.GetValueOrDefault(CharacterClassCatalog.Family(cls), MeterDamageFill);
            row.Bar.Value = PartyWarBoard.Fill(share.Share);
            row.Name.Text = PartyWarBoard.RowName(i + 1, member.Name);
            row.Percent.Text = PartyWarBoard.Percent(share.Share);
            _warBoardLines.Add(new GameWarBoardRow(i + 1, member.Name, cls, share.Share));
        }
        PluginNotifyParty();
    }

    private PartyMember? MemberOf(int memberId)
    {
        foreach (var member in PartyMembers)
            if (member.CharId == memberId) return member;
        return null;
    }

    private sealed partial class WarBoardBar : Control
    {
        private readonly Color _track;
        private float _value;
        private Color _fill = Colors.White;

        public WarBoardBar(Color track) => _track = track;

        public float Value
        {
            get => _value;
            set { _value = value; QueueRedraw(); }
        }

        public Color Fill
        {
            get => _fill;
            set { _fill = value; QueueRedraw(); }
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), _track);
            if (_value > 0) DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * _value, Size.Y)), _fill);
        }
    }
}
