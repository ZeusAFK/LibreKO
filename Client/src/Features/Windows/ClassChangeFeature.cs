using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ClassChangeFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly DevelopSession develop;
    private readonly NpcQuestSession npcQuest;
    private readonly ResetConfirmFeature resetConfirm;
    private readonly IInteractionDialogs interactionDialogs;

    public ClassChangeFeature(IWorldContext context, DevelopSession develop, NpcQuestSession npcQuest, ResetConfirmFeature resetConfirm, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.develop = develop;
        this.npcQuest = npcQuest;
        this.resetConfirm = resetConfirm;
        this.interactionDialogs = interactionDialogs;
    }

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private bool shown;

    public bool IsOpen => shown;

    private const int RedistributionPanelWidth = 360;

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        develop.ClassChangeNpcEvent += OnNpc;
    }

    public void Dispose()
    {
        develop.ClassChangeNpcEvent -= OnNpc;
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("class_change", "Redistribution", bodyMinWidth: RedistributionPanelWidth) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var header = UiTheme.Text(
            "Every stat or mastery point goes back into its pool, for a fee. " + ResetConfirmFeature.ResetItemsEquippedText,
            13, UiTheme.TextLo);
        header.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        header.CustomMinimumSize = new Vector2(RedistributionPanelWidth, 0);
        root.AddChild(header);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        root.AddChild(buttons);
        buttons.AddChild(RedistributeButton("Stat points", "Return every stat point to the pool", ResetKind.Stat));
        buttons.AddChild(RedistributeButton("Mastery points", "Return every mastery point to the pool", ResetKind.Skill));
    }

    private Button RedistributeButton(string text, string tooltip, ResetKind kind)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.Pressed += () => resetConfirm.RequestReset(kind);
        return button;
    }

    internal void OnNpc()
    {
        context.NpcDialog.Close();
        Panel.Title = npcQuest.LastNpcName;
        Panel.Visible = true;
        shown = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
    }
}
