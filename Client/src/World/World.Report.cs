using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _reportLayer = null!;
    private HudWindow _reportPanel = null!;
    private Label _reportGateLbl = null!, _reportStatus = null!, _reportPageLbl = null!;
    private LineEdit _reportTargetEdit = null!, _reportReasonEdit = null!;
    private Button _reportFileBtn = null!, _reportPrevBtn = null!, _reportNextBtn = null!;
    private VBoxContainer _reportVoteList = null!;
    private bool _reportShown;
    private bool _reportEligible;
    private byte _reportPage;
    private byte _reportTotalPages = 1;

    private void ReportInit()
    {
        BuildReportPanel();
        Net.I.ReportResultEvent += OnReportResult;
        Net.I.ReportListEvent += OnReportList;
        Net.I.ReportInspectorEvent += OnReportInspector;
    }

    private void ReportDispose()
    {
        Net.I.ReportResultEvent -= OnReportResult;
        Net.I.ReportListEvent -= OnReportList;
        Net.I.ReportInspectorEvent -= OnReportInspector;
    }

    private void BuildReportPanel()
    {
        _reportLayer = new CanvasLayer { Layer = 77 };
        AddChild(_reportLayer);

        _reportPanel = new HudWindow("report", Localization.Loc.Tr("Sheriff Reports"), new Vector2(170, 110)) { Visible = false };
        _reportPanel.Closed += CloseReport;
        _reportLayer.AddChild(_reportPanel);
        var r = _reportPanel.Body;
        r.AddThemeConstantOverride("separation", 8);

        _reportGateLbl = HudStyle.Label(13);
        r.AddChild(_reportGateLbl);

        r.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("File a Report")));
        var targetRow = new HBoxContainer(); targetRow.AddThemeConstantOverride("separation", 6);
        var targetLbl = HudStyle.Label(13); targetLbl.Text = Localization.Loc.Tr("Accused:"); targetLbl.CustomMinimumSize = new Vector2(64, 0);
        targetRow.AddChild(targetLbl);
        _reportTargetEdit = new LineEdit { PlaceholderText = Localization.Loc.Tr("player name"), CustomMinimumSize = new Vector2(220, 0) };
        targetRow.AddChild(_reportTargetEdit);
        r.AddChild(targetRow);

        var reasonRow = new HBoxContainer(); reasonRow.AddThemeConstantOverride("separation", 6);
        var reasonLbl = HudStyle.Label(13); reasonLbl.Text = Localization.Loc.Tr("Reason:"); reasonLbl.CustomMinimumSize = new Vector2(64, 0);
        reasonRow.AddChild(reasonLbl);
        _reportReasonEdit = new LineEdit { PlaceholderText = Localization.Loc.Tr("reason (max 512 chars)"), CustomMinimumSize = new Vector2(220, 0) };
        _reportReasonEdit.MaxLength = Net.ReportReasonMax;
        reasonRow.AddChild(_reportReasonEdit);
        r.AddChild(reasonRow);

        _reportFileBtn = new Button { Text = Localization.Loc.Tr("Submit Report"), FocusMode = Control.FocusModeEnum.None };
        _reportFileBtn.Pressed += OnFileReportPressed;
        r.AddChild(_reportFileBtn);

        r.AddChild(new HSeparator());

        var listRow = new HBoxContainer(); listRow.AddThemeConstantOverride("separation", 6);
        var listTitle = UiTheme.SectionTitle(Localization.Loc.Tr("Open Reports"));
        listTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        listRow.AddChild(listTitle);
        _reportPrevBtn = new Button { Text = Localization.Loc.Tr("Prev"), FocusMode = Control.FocusModeEnum.None };
        _reportPrevBtn.Pressed += () => { if (_reportPage > 0) { _reportPage--; RefreshReportList(); } };
        listRow.AddChild(_reportPrevBtn);
        _reportPageLbl = HudStyle.Label(13); _reportPageLbl.Text = "1 / 1";
        _reportPageLbl.CustomMinimumSize = new Vector2(56, 0);
        _reportPageLbl.HorizontalAlignment = HorizontalAlignment.Center;
        listRow.AddChild(_reportPageLbl);
        _reportNextBtn = new Button { Text = Localization.Loc.Tr("Next"), FocusMode = Control.FocusModeEnum.None };
        _reportNextBtn.Pressed += () => { if (_reportPage + 1 < _reportTotalPages) { _reportPage++; RefreshReportList(); } };
        listRow.AddChild(_reportNextBtn);
        var refreshBtn = new Button { Text = Localization.Loc.Tr("Refresh"), FocusMode = Control.FocusModeEnum.None };
        refreshBtn.Pressed += () => { _reportPage = 0; RefreshReportList(); };
        listRow.AddChild(refreshBtn);
        r.AddChild(listRow);

        _reportVoteList = ReportScroll(r, 200);

        _reportStatus = HudStyle.Label(13);
        r.AddChild(_reportStatus);
    }

    private static VBoxContainer ReportScroll(VBoxContainer parent, int height)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        return list;
    }

    private void ToggleReport()
    {
        if (_reportShown) { CloseReport(); return; }
        _reportEligible = _isGm;
        _reportPanel.Visible = true;
        _reportShown = true;
        _reportPage = 0;
        SetReportStatus("", false);
        ApplyReportGate();
        RefreshReportList();
    }

    private void CloseReport()
    {
        if (!_reportShown) return;
        _reportShown = false;
        _reportPanel.Visible = false;
    }

    private void ApplyReportGate()
    {
        if (_reportEligible)
        {
            _reportGateLbl.Text = Localization.Loc.Tr("You are a sheriff (King / GM). File reports and vote below.");
            _reportGateLbl.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        }
        else
        {
            _reportGateLbl.Text = Localization.Loc.Tr("Only the King or a GM may file reports or vote. (View only)");
            _reportGateLbl.AddThemeColorOverride("font_color", UiTheme.TextLo);
        }
        _reportTargetEdit.Editable = _reportEligible;
        _reportReasonEdit.Editable = _reportEligible;
        _reportFileBtn.Disabled = !_reportEligible;
    }

    private void RefreshReportList()
    {
        Net.I.SendReportListOpen(_reportPage);
        Net.I.SendReportInspector();
    }

    private void OnFileReportPressed()
    {
        if (!_reportEligible) { SetReportStatus(Localization.Loc.Tr("Only the King or a GM can file reports."), true); return; }
        string target = _reportTargetEdit.Text.Trim();
        string reason = _reportReasonEdit.Text.Trim();
        if (target.Length < 2) { SetReportStatus(Localization.Loc.Tr("Enter the accused player's name."), true); return; }
        if (reason.Length == 0) { SetReportStatus(Localization.Loc.Tr("Enter a reason for the report."), true); return; }
        if (reason.Length > Net.ReportReasonMax) { SetReportStatus(Localization.Loc.Tr("Reason is too long (max 512)."), true); return; }
        Net.I.SendReportFile(target, reason);
        SetReportStatus(Localization.Loc.Tr("Filing report…"), false);
    }

    private void OnReportResult(byte sub, bool ok)
    {
        switch (sub)
        {
            case Net.ReportFileSub:
                if (ok)
                {
                    SetReportStatus(Localization.Loc.Tr("Report filed."), false);
                    _reportTargetEdit.Text = "";
                    _reportReasonEdit.Text = "";
                    _reportPage = 0;
                    RefreshReportList();
                }
                else SetReportStatus(Localization.Loc.Tr("Couldn't file (not a sheriff, or unknown player / bad reason)."), true);
                break;
            case Net.ReportVoteYesSub:
            case Net.ReportVoteNoSub:
                if (ok) { SetReportStatus(Localization.Loc.Tr("Vote cast."), false); RefreshReportList(); }
                else SetReportStatus(Localization.Loc.Tr("Vote rejected (already voted, or report resolved)."), true);
                break;
            default:
                if (!ok) SetReportStatus(Localization.Loc.Tr("Action rejected by the server."), true);
                break;
        }
    }

    private void OnReportList(byte totalPages, List<Net.ReportEntry> entries)
    {
        _reportTotalPages = totalPages < 1 ? (byte)1 : totalPages;
        if (_reportPage >= _reportTotalPages) _reportPage = (byte)(_reportTotalPages - 1);
        _reportPageLbl.Text = $"{_reportPage + 1} / {_reportTotalPages}";
        _reportPrevBtn.Disabled = _reportPage == 0;
        _reportNextBtn.Disabled = _reportPage + 1 >= _reportTotalPages;

        foreach (var c in _reportVoteList.GetChildren()) c.QueueFree();
        if (entries.Count == 0)
        {
            var e = HudStyle.Label(13); e.Text = Localization.Loc.Tr("No open reports.");
            _reportVoteList.AddChild(e);
            return;
        }
        foreach (var entry in entries)
        {
            int id = entry.Id;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var vb = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            vb.AddThemeConstantOverride("separation", 1);
            row.AddChild(vb);

            var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
            var name = UiTheme.Text(entry.TargetName, 13, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(name);
            var tally = UiTheme.Text($"{Localization.Loc.Tr("Yes")} {entry.VoteYes}/3   {Localization.Loc.Tr("No")} {entry.VoteNo}/2", 12, UiTheme.TextLo);
            head.AddChild(tally);
            vb.AddChild(head);

            var reason = UiTheme.Text(entry.Reason, 11, UiTheme.TextLo);
            reason.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            vb.AddChild(reason);

            var btnRow = new HBoxContainer(); btnRow.AddThemeConstantOverride("separation", 6);
            btnRow.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            var yesBtn = new Button { Text = Localization.Loc.Tr("Vote Yes"), FocusMode = Control.FocusModeEnum.None, Disabled = !_reportEligible };
            yesBtn.Pressed += () => { Net.I.SendReportVoteYes(id); SetReportStatus(Localization.Loc.Tr("Voting yes…"), false); };
            btnRow.AddChild(yesBtn);
            var noBtn = new Button { Text = Localization.Loc.Tr("Vote No"), FocusMode = Control.FocusModeEnum.None, Disabled = !_reportEligible };
            noBtn.Pressed += () => { Net.I.SendReportVoteNo(id); SetReportStatus(Localization.Loc.Tr("Voting no…"), false); };
            btnRow.AddChild(noBtn);
            vb.AddChild(btnRow);

            _reportVoteList.AddChild(row);
        }
    }

    private void OnReportInspector(bool open, int openCount)
    {
        if (_reportShown)
            SetReportStatus(openCount > 0 ? $"{openCount} {Localization.Loc.Tr("report(s) still open.")}" : Localization.Loc.Tr("No reports open."), false);
    }

    private void SetReportStatus(string text, bool warn)
    {
        _reportStatus.Text = text;
        _reportStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
