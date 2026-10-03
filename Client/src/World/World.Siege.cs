using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _siegeLayer = null!;
    private HudWindow _siegePanel = null!;
    private Label _siegeOwnerLbl = null!, _siegeScheduleLbl = null!, _siegeStatusLbl = null!,
        _siegeMasterLbl = null!, _siegeHint = null!;
    private bool _siegeShown;

    private SiegeCastleOwner _siegeOwner;
    private SiegeSchedule _siegeSchedule;
    private SiegeMasterInfo _siegeMaster;
    private SiegeStatus _siegeStatus;
    private bool _haveOwner, _haveSchedule, _haveMaster, _haveStatus;

    private static readonly string[] SiegeDayNames =
        { "—", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    private void SiegeInit()
    {
        BuildSiegePanel();
        Net.I.SiegeCastleFlagEvent += OnSiegeCastleFlag;
        Net.I.SiegeScheduleEvent += OnSiegeSchedule;
        Net.I.SiegeMasterEvent += OnSiegeMaster;
        Net.I.SiegeStatusEvent += OnSiegeStatus;
    }

    private void SiegeDispose()
    {
        Net.I.SiegeCastleFlagEvent -= OnSiegeCastleFlag;
        Net.I.SiegeScheduleEvent -= OnSiegeSchedule;
        Net.I.SiegeMasterEvent -= OnSiegeMaster;
        Net.I.SiegeStatusEvent -= OnSiegeStatus;
    }

    private void BuildSiegePanel()
    {
        _siegeLayer = new CanvasLayer { Layer = 74 };
        AddChild(_siegeLayer);

        _siegePanel = new HudWindow("siege", Localization.Loc.Tr("Castle Siege War"), new Vector2(200, 110)) { Visible = false };
        _siegePanel.Closed += CloseSiege;
        _siegeLayer.AddChild(_siegePanel);

        var r = _siegePanel.Body;
        r.AddThemeConstantOverride("separation", 8);
        r.CustomMinimumSize = new Vector2(320, 0);

        r.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Castle Owner")));
        _siegeOwnerLbl = HudStyle.Label(13);
        _siegeOwnerLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        r.AddChild(_siegeOwnerLbl);

        r.AddChild(new HSeparator());

        r.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("War Schedule")));
        _siegeScheduleLbl = HudStyle.Label(13);
        _siegeScheduleLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        r.AddChild(_siegeScheduleLbl);

        r.AddChild(new HSeparator());

        r.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Castellan Clan")));
        _siegeMasterLbl = HudStyle.Label(13);
        _siegeMasterLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        r.AddChild(_siegeMasterLbl);

        r.AddChild(new HSeparator());

        r.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("War Status")));
        _siegeStatusLbl = HudStyle.Label(13);
        _siegeStatusLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        r.AddChild(_siegeStatusLbl);

        r.AddChild(new HSeparator());

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        _siegeHint = HudStyle.Label(12);
        _siegeHint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_siegeHint);
        var refresh = new Button { Text = Localization.Loc.Tr("Refresh"), FocusMode = Control.FocusModeEnum.None };
        refresh.AddThemeFontSizeOverride("font_size", 12);
        refresh.Pressed += RequestSiegeData;
        footer.AddChild(refresh);
        r.AddChild(footer);

        RenderSiegeBoard();
    }

    private void ToggleSiege()
    {
        if (_siegeShown) CloseSiege();
        else OpenSiege();
    }

    private void OpenSiege()
    {
        _siegePanel.Visible = true;
        _siegeShown = true;
        RequestSiegeData();
    }

    private void CloseSiege()
    {
        if (!_siegeShown) return;
        _siegeShown = false;
        _siegePanel.Visible = false;
    }

    private void RequestSiegeData()
    {
        _siegeHint.Text = Localization.Loc.Tr("Requesting war data…");
        Net.I.SendSiegeCastleFlag();
        Net.I.SendSiegeSchedule();
        Net.I.SendSiegeMaster();
        Net.I.SendSiegeStatus();
    }

    private void OnSiegeCastleFlag(SiegeCastleOwner owner)
    {
        _siegeOwner = owner;
        _haveOwner = true;
        if (_siegeShown) RenderSiegeBoard();
    }

    private void OnSiegeSchedule(SiegeSchedule schedule)
    {
        _siegeSchedule = schedule;
        _haveSchedule = true;
        if (_siegeShown) RenderSiegeBoard();
    }

    private void OnSiegeMaster(SiegeMasterInfo master)
    {
        _siegeMaster = master;
        _haveMaster = true;
        if (_siegeShown) RenderSiegeBoard();
    }

    private void OnSiegeStatus(SiegeStatus status)
    {
        _siegeStatus = status;
        _haveStatus = true;
        if (_siegeShown) RenderSiegeBoard();
    }

    private void RenderSiegeBoard()
    {
        if (!_haveOwner)
            _siegeOwnerLbl.Text = Localization.Loc.Tr("No data yet.");
        else if (_siegeOwner.HasOwner)
            _siegeOwnerLbl.Text = $"{Localization.Loc.Tr("Held by clan")} #{_siegeOwner.ClanId}  ({Localization.Loc.Tr("grade")} {_siegeOwner.Grade}, {Localization.Loc.Tr("flag")} {_siegeOwner.Flag})";
        else
            _siegeOwnerLbl.Text = Localization.Loc.Tr("The castle is unclaimed.");

        if (!_haveSchedule)
            _siegeScheduleLbl.Text = Localization.Loc.Tr("No data yet.");
        else if (_siegeSchedule.Scheduled)
            _siegeScheduleLbl.Text =
                $"{Localization.Loc.Tr("Castle")} {_siegeSchedule.CastleIndex}: {SiegeDayName(_siegeSchedule.WarDay)} " +
                $"{_siegeSchedule.WarHour:00}:{_siegeSchedule.WarMinute:00}";
        else
            _siegeScheduleLbl.Text = Localization.Loc.Tr("No war is currently scheduled.");

        if (!_haveMaster)
            _siegeMasterLbl.Text = Localization.Loc.Tr("No data yet.");
        else if (string.IsNullOrEmpty(_siegeMaster.ClanName))
            _siegeMasterLbl.Text = Localization.Loc.Tr("No castellan clan.");
        else
        {
            string req = (_siegeMaster.RequestDay != 0 || _siegeMaster.RequestHour != 0 || _siegeMaster.RequestMinute != 0)
                ? $"  •  {Localization.Loc.Tr("requested")} {SiegeDayName(_siegeMaster.RequestDay)} {_siegeMaster.RequestHour:00}:{_siegeMaster.RequestMinute:00}"
                : "";
            _siegeMasterLbl.Text =
                $"{_siegeMaster.ClanName}  ({SiegeNationName(_siegeMaster.Nation)}, {_siegeMaster.Members} {Localization.Loc.Tr("members")}){req}";
        }

        if (!_haveStatus)
            _siegeStatusLbl.Text = Localization.Loc.Tr("No data yet.");
        else
        {
            string phase = _siegeStatus.SiegeType == 0 ? Localization.Loc.Tr("Peace — no war in progress") : $"{Localization.Loc.Tr("War in progress")} ({Localization.Loc.Tr("type")} {_siegeStatus.SiegeType})";
            string clan = string.IsNullOrEmpty(_siegeStatus.ClanName)
                ? ""
                : $"\n{Localization.Loc.Tr("Defenders")}: {_siegeStatus.ClanName} ({SiegeNationName(_siegeStatus.Nation)}, {_siegeStatus.Members} {Localization.Loc.Tr("members")})";
            _siegeStatusLbl.Text = phase + clan;
        }

        if (_haveOwner || _haveSchedule || _haveMaster || _haveStatus)
            _siegeHint.Text = Localization.Loc.Tr("Press Z to close.");
    }

    private static string SiegeDayName(byte day) =>
        day < SiegeDayNames.Length ? Localization.Loc.Tr(SiegeDayNames[day]) : day.ToString();

    private static string SiegeNationName(byte nation) => nation switch
    {
        1 => Localization.Loc.Tr("Karus"),
        2 => Localization.Loc.Tr("El Morad"),
        _ => Localization.Loc.Tr("Neutral"),
    };
}
