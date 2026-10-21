using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ReportFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ReportSession report;
    private readonly IReportRequests requests;

    public ReportFeature(IWorldContext context, ReportSession report, IReportRequests requests)
    {
        this.context = context;
        this.report = report;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private Label gateLbl = null!;
    private Label status = null!;
    private Label pageLbl = null!;
    internal LineEdit TargetEdit { get; private set; } = null!;
    internal LineEdit ReasonEdit { get; private set; } = null!;
    private Button fileBtn = null!;
    private Button prevBtn = null!;
    private Button nextBtn = null!;
    private VBoxContainer voteList = null!;
    private bool shown;

    public bool IsOpen => shown;

    private bool eligible;
    private byte page;
    private byte totalPages = 1;

    public void Build()
    {
        BuildPanel();
        report.ReportResultEvent += OnResult;
        report.ReportListEvent += OnList;
        report.ReportInspectorEvent += OnInspector;
    }

    public void Dispose()
    {
        report.ReportResultEvent -= OnResult;
        report.ReportListEvent -= OnList;
        report.ReportInspectorEvent -= OnInspector;
    }

    private void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.TopPrompts };
        context.Root.AddChild(layer);

        panel = new HudWindow("report", "Sheriff Reports") { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);
        var r = panel.Body;
        r.AddThemeConstantOverride("separation", 8);

        gateLbl = HudStyle.Label(13);
        r.AddChild(gateLbl);

        r.AddChild(UiTheme.SectionTitle("File a Report"));
        var targetRow = new HBoxContainer(); targetRow.AddThemeConstantOverride("separation", 6);
        var targetLbl = HudStyle.Label(13); targetLbl.Text = "Accused:"; targetLbl.CustomMinimumSize = new Vector2(64, 0);
        targetRow.AddChild(targetLbl);
        TargetEdit = new LineEdit { PlaceholderText = "player name", CustomMinimumSize = new Vector2(220, 0) };
        targetRow.AddChild(TargetEdit);
        r.AddChild(targetRow);

        var reasonRow = new HBoxContainer(); reasonRow.AddThemeConstantOverride("separation", 6);
        var reasonLbl = HudStyle.Label(13); reasonLbl.Text = "Reason:"; reasonLbl.CustomMinimumSize = new Vector2(64, 0);
        reasonRow.AddChild(reasonLbl);
        ReasonEdit = new LineEdit { PlaceholderText = "reason (max 512 chars)", CustomMinimumSize = new Vector2(220, 0) };
        ReasonEdit.MaxLength = ReportWire.ReasonMax;
        reasonRow.AddChild(ReasonEdit);
        r.AddChild(reasonRow);

        fileBtn = new Button { Text = "Submit Report", FocusMode = Control.FocusModeEnum.None };
        fileBtn.Pressed += OnFilePressed;
        r.AddChild(fileBtn);

        r.AddChild(new HSeparator());

        var listRow = new HBoxContainer(); listRow.AddThemeConstantOverride("separation", 6);
        var listTitle = UiTheme.SectionTitle("Open Reports");
        listTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        listRow.AddChild(listTitle);
        prevBtn = new Button { Text = "Prev", FocusMode = Control.FocusModeEnum.None };
        prevBtn.Pressed += () => { if (page > 0) { page--; RefreshList(); } };
        listRow.AddChild(prevBtn);
        pageLbl = HudStyle.Label(13); pageLbl.Text = "1 / 1";
        pageLbl.CustomMinimumSize = new Vector2(56, 0);
        pageLbl.HorizontalAlignment = HorizontalAlignment.Center;
        listRow.AddChild(pageLbl);
        nextBtn = new Button { Text = "Next", FocusMode = Control.FocusModeEnum.None };
        nextBtn.Pressed += () => { if (page + 1 < totalPages) { page++; RefreshList(); } };
        listRow.AddChild(nextBtn);
        var refreshBtn = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refreshBtn.Pressed += () => { page = 0; RefreshList(); };
        listRow.AddChild(refreshBtn);
        r.AddChild(listRow);

        voteList = Scroll(r, 200);

        status = HudStyle.Label(13);
        r.AddChild(status);
    }

    private static VBoxContainer Scroll(VBoxContainer parent, int height)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        return list;
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        eligible = context.Self.IsGm;
        panel.Visible = true;
        shown = true;
        page = 0;
        SetStatus("", false);
        ApplyGate();
        RefreshList();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        panel.Visible = false;
    }

    private void ApplyGate()
    {
        if (eligible)
        {
            gateLbl.Text = "You are a sheriff. File reports and vote below.";
            gateLbl.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        }
        else
        {
            gateLbl.Text = "Only the King or a GM may file reports or vote. (View only)";
            gateLbl.AddThemeColorOverride("font_color", UiTheme.TextLo);
        }
        TargetEdit.Editable = eligible;
        ReasonEdit.Editable = eligible;
        fileBtn.Disabled = !eligible;
    }

    private void RefreshList()
    {
        requests.ListOpen(page);
        requests.Inspector();
    }

    private void OnFilePressed()
    {
        if (!eligible) { SetStatus("Only the King or a GM can file reports.", true); return; }
        string target = TargetEdit.Text.Trim();
        string reason = ReasonEdit.Text.Trim();
        if (target.Length < 2) { SetStatus("Enter the accused player's name.", true); return; }
        if (reason.Length == 0) { SetStatus("Enter a reason for the report.", true); return; }
        if (reason.Length > ReportWire.ReasonMax) { SetStatus("Reason is too long (max 512).", true); return; }
        requests.File(target, reason);
        SetStatus("Filing report…", false);
    }

    private void OnResult(ReportSubOpcode sub, bool ok)
    {
        switch (sub)
        {
            case ReportSubOpcode.File:
                if (ok)
                {
                    SetStatus("Report filed.", false);
                    TargetEdit.Text = "";
                    ReasonEdit.Text = "";
                    page = 0;
                    RefreshList();
                }
                else SetStatus("Couldn't file (not a sheriff, or unknown player / bad reason).", true);
                break;
            case ReportSubOpcode.VoteYes:
            case ReportSubOpcode.VoteNo:
                if (ok) { SetStatus("Vote cast.", false); RefreshList(); }
                else SetStatus("Vote rejected (already voted, or report resolved).", true);
                break;
            default:
                if (!ok) SetStatus("Action rejected by the server.", true);
                break;
        }
    }

    private readonly List<Button> voteButtons = new();

    private void OnList(byte totalPages, List<ReportEntry> entries)
    {
        this.totalPages = totalPages < 1 ? (byte)1 : totalPages;
        if (page >= this.totalPages) page = (byte)(this.totalPages - 1);
        pageLbl.Text = $"{page + 1} / {this.totalPages}";
        prevBtn.Disabled = page == 0;
        nextBtn.Disabled = page + 1 >= this.totalPages;

        foreach (var c in voteList.GetChildren()) c.QueueFree();
        voteButtons.Clear();
        if (entries.Count == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No open reports.";
            voteList.AddChild(e);
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
            var tally = UiTheme.Text($"Yes {entry.VoteYes}/3   No {entry.VoteNo}/2", 12, UiTheme.TextLo);
            head.AddChild(tally);
            vb.AddChild(head);

            var reason = UiTheme.Text(entry.Reason, 11, UiTheme.TextLo);
            reason.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            vb.AddChild(reason);

            var btnRow = new HBoxContainer(); btnRow.AddThemeConstantOverride("separation", 6);
            btnRow.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            var yesBtn = new Button { Text = "Vote Yes", FocusMode = Control.FocusModeEnum.None, Disabled = !eligible };
            yesBtn.Pressed += () => { requests.VoteYes(id); SetStatus("Voting yes…", false); };
            btnRow.AddChild(yesBtn);
            var noBtn = new Button { Text = "Vote No", FocusMode = Control.FocusModeEnum.None, Disabled = !eligible };
            noBtn.Pressed += () => { requests.VoteNo(id); SetStatus("Voting no…", false); };
            btnRow.AddChild(noBtn);
            voteButtons.Add(yesBtn);
            voteButtons.Add(noBtn);
            vb.AddChild(btnRow);

            voteList.AddChild(row);
        }
    }

    private void OnInspector(bool open, int openCount)
    {
        eligible = open || context.Self.IsGm;
        if (!shown) return;
        ApplyGate();
        foreach (var button in voteButtons) button.Disabled = !eligible;
        SetStatus(openCount > 0 ? $"{openCount} report(s) still open." : "No reports open.", false);
    }

    private void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
