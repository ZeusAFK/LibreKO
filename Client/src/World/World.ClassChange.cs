using Godot;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _classChangeLayer = null!;
    private HudWindow _classChangePanel = null!;
    private bool _classChangeShown;

    private const int RedistributionPanelWidth = 360;

    private void ClassChangeInit()
    {
        BuildClassChangePanel();
        Net.I.ClassChangeNpcEvent += OnClassChangeNpc;
    }

    private void ClassChangeDispose()
    {
        Net.I.ClassChangeNpcEvent -= OnClassChangeNpc;
    }

    private void BuildClassChangePanel()
    {
        _classChangeLayer = new CanvasLayer { Layer = 74 };
        AddChild(_classChangeLayer);

        _classChangePanel = new HudWindow("class_change", "Redistribution", new Vector2(300, 150), RedistributionPanelWidth) { Visible = false };
        _classChangePanel.Closed += CloseClassChange;
        _classChangeLayer.AddChild(_classChangePanel);

        var root = _classChangePanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var header = UiTheme.Text(
            "Every stat or mastery point goes back into its pool, for a fee. Your inventory must be empty to redistribute stats.",
            13, UiTheme.TextLo);
        header.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        header.CustomMinimumSize = new Vector2(RedistributionPanelWidth, 0);
        root.AddChild(header);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        root.AddChild(buttons);
        buttons.AddChild(RedistributeButton("Stat points", "Return every stat point to the pool", Net.ResetKindStat));
        buttons.AddChild(RedistributeButton("Mastery points", "Return every mastery point to the pool", Net.ResetKindSkill));
    }

    private Button RedistributeButton(string text, string tooltip, byte kind)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.Pressed += () => RequestReset(kind);
        return button;
    }

    private void OnClassChangeNpc()
    {
        CloseNpcDialog();
        _classChangePanel.Title = _vendorNpcName;
        _classChangePanel.Visible = true;
        _classChangeShown = true;
    }

    private void CloseClassChange()
    {
        if (!_classChangeShown) return;
        _classChangeShown = false;
        _classChangePanel.Visible = false;
    }
}
