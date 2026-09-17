using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _classChangeLayer = null!;
    private HudWindow _classChangePanel = null!;
    private Label _classChangeHeader = null!, _classChangeStatus = null!;
    private VBoxContainer _classChangeList = null!;
    private bool _classChangeShown;

    private const int JobChangeScroll = 700112000;
    private const int MasterSplitScroll = 700113000;
    private const int JobChangeToken = 900006000;
    private const byte KurianJob = 5;

    private void ClassChangeInit()
    {
        BuildClassChangePanel();
        Net.I.ClassChangeNpcEvent += OnClassChangeNpc;
        Net.I.JobChangeResultEvent += OnClassChangeResult;
    }

    private void ClassChangeDispose()
    {
        Net.I.ClassChangeNpcEvent -= OnClassChangeNpc;
        Net.I.JobChangeResultEvent -= OnClassChangeResult;
    }

    private void BuildClassChangePanel()
    {
        _classChangeLayer = new CanvasLayer { Layer = 74 };
        AddChild(_classChangeLayer);

        _classChangePanel = new HudWindow("class_change", "Class Change", new Vector2(300, 150), 360) { Visible = false };
        _classChangePanel.Closed += CloseClassChange;
        _classChangeLayer.AddChild(_classChangePanel);

        var root = _classChangePanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        _classChangeHeader = UiTheme.Text("", 13, UiTheme.TextLo);
        _classChangeHeader.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _classChangeHeader.CustomMinimumSize = new Vector2(360, 0);
        root.AddChild(_classChangeHeader);

        root.AddChild(UiTheme.SectionTitle("Change to"));

        _classChangeList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _classChangeList.AddThemeConstantOverride("separation", 4);
        root.AddChild(_classChangeList);

        root.AddChild(new HSeparator());
        _classChangeStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        _classChangeStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_classChangeStatus);
    }

    private void OnClassChangeNpc()
    {
        CloseNpcDialog();
        _classChangePanel.Title = _vendorNpcName;
        _classChangeStatus.Text = "";
        RefreshClassChange();
        _classChangePanel.Visible = true;
        _classChangeShown = true;
    }

    private void CloseClassChange()
    {
        if (!_classChangeShown) return;
        _classChangeShown = false;
        _classChangePanel.Visible = false;
    }

    private void RefreshClassChange()
    {
        foreach (var c in _classChangeList.GetChildren()) c.QueueFree();

        int cls = _selfClass;
        int tier = CharacterClassCatalog.Tier(cls);
        int family = CharacterClassCatalog.Family(cls);

        _classChangeHeader.Text =
            $"You are a {CharacterClassCatalog.SpecializationName(cls)} — {CharacterClassCatalog.TierName(cls)} tier. "
            + "Unequip everything and carry the promotion scroll before changing.";

        if (tier is < 1 or > 3)
        {
            _classChangeList.AddChild(UiTheme.Text("This character cannot change class.", 12, UiTheme.TextLo));
            return;
        }

        int offered = 0;
        for (byte job = 1; job <= 5; job++)
        {
            if (job == family) continue;
            if (tier == 3)
            {
                if (job != KurianJob)
                {
                    AddClassChangeRow(job, 1);
                    offered++;
                }
                AddClassChangeRow(job, 0);
                offered++;
            }
            else
            {
                AddClassChangeRow(job, 0);
                offered++;
            }
        }

        if (offered == 0)
            _classChangeList.AddChild(UiTheme.Text("No promotion is available here.", 12, UiTheme.TextLo));
    }

    private void AddClassChangeRow(byte job, byte changeType)
    {
        int target = TargetClassFor(job, changeType);

        var row = UiTheme.RowPanel();
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.AddChild(line);

        var name = UiTheme.Text(CharacterClassCatalog.SpecializationName(target), 13, UiTheme.TextHi);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(name);
        line.AddChild(UiTheme.Pill(CharacterClassCatalog.TierName(target), UiTheme.Gold));

        int scroll = changeType == 0 ? JobChangeScroll : MasterSplitScroll;
        bool haveScroll = Inv.CountOf(scroll) > 0 || Inv.CountOf(JobChangeToken) > 0;

        var btn = new Button
        {
            Text = "Change",
            FocusMode = Control.FocusModeEnum.None,
            Disabled = !haveScroll,
            TooltipText = haveScroll ? "" : $"Requires {ItemData.DisplayName(scroll)}",
        };
        btn.AddThemeFontSizeOverride("font_size", 12);
        byte j = job, t = changeType;
        btn.Pressed += () => RequestClassChange(j, t);
        line.AddChild(btn);

        _classChangeList.AddChild(row);
    }

    private int TargetClassFor(byte job, byte changeType)
    {
        int nationBase = _selfClass / 100 * 100;
        int tier = CharacterClassCatalog.Tier(_selfClass);

        if (job == KurianJob)
            return nationBase + tier switch { 1 => 13, 2 => 14, _ => 15 };

        return tier switch
        {
            1 => nationBase + job,
            2 => nationBase + 3 + 2 * job,
            _ => nationBase + (changeType == 1 ? 3 : 4) + 2 * job,
        };
    }

    private void RequestClassChange(byte job, byte changeType)
    {
        if (!_classChangeShown || _selfDead) return;
        _classChangeStatus.Text = "";
        Net.I.SendJobChange(changeType, job);
    }

    private void OnClassChangeResult(int code)
    {
        if (!_classChangeShown) return;
        _classChangeStatus.Text = code switch
        {
            1 => "Class changed.",
            2 => "That class is not a valid destination.",
            4 => "Take off your equipment first.",
            6 => "You need the promotion scroll, and a different class.",
            _ => "Class change failed.",
        };
        if (code == 1) foreach (var c in _classChangeList.GetChildren()) c.QueueFree();
    }
}
