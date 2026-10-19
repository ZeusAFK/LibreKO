using Godot;

namespace LibreKO.Features.Windows;

internal sealed class GenderChangeFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly GenderChangeSession genderChange;
    private readonly SelfSession self;
    private readonly IGenderChangeRequests requests;

    public GenderChangeFeature(IWorldContext context, GenderChangeSession genderChange, SelfSession self, IGenderChangeRequests requests)
    {
        this.context = context;
        this.genderChange = genderChange;
        this.self = self;
        this.requests = requests;
    }

    internal const int LookPreviewWidth = 220;
    internal const int LookPreviewHeight = 300;
    private const float LookTurnDegrees = 30f;

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    internal LookPreview Preview { get; private set; } = null!;
    internal LookEditor Editor { get; private set; } = null!;
    private Label status = null!;
    private Button confirm = null!;
    private bool shown, armed;

    public bool IsOpen => shown;

    internal bool InFlight { get; private set; }

    public void Build()
    {
        BuildPanel();
        genderChange.GenderChangeRefusedEvent += OnRefused;
        genderChange.GenderChangedEvent += OnChanged;
    }

    public void Dispose()
    {
        genderChange.GenderChangeRefusedEvent -= OnRefused;
        genderChange.GenderChangedEvent -= OnChanged;
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("genderchange", "Gender Change") { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        Panel.Body.AddChild(row);

        Preview = new LookPreview(LookPreviewWidth, LookPreviewHeight);
        row.AddChild(LookPreviewColumn(Preview));

        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 8);
        row.AddChild(form);
        Editor = new LookEditor();
        Editor.Changed += OnLookChanged;
        form.AddChild(Editor);

        status = HudStyle.Label(12);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.CustomMinimumSize = new Vector2(230, 0);
        form.AddChild(status);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        form.AddChild(actions);
        confirm = LookActionButton("Change", OnConfirmPressed);
        actions.AddChild(confirm);
        actions.AddChild(LookActionButton("Cancel", Close));
    }

    internal static Control LookPreviewColumn(LookPreview preview, bool zoom = false)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        column.AddChild(preview);
        var turnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        turnRow.AddThemeConstantOverride("separation", 8);
        turnRow.AddChild(LookEditor.StepButton("◀", () => preview.Turn(-LookTurnDegrees)));
        turnRow.AddChild(LookEditor.StepButton("▶", () => preview.Turn(LookTurnDegrees)));
        if (zoom) turnRow.AddChild(LookActionButton("Zoom", preview.ToggleZoom));
        column.AddChild(turnRow);
        return column;
    }

    internal static Button LookActionButton(string text, System.Action pressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += pressed;
        return button;
    }

    internal void Open()
    {
        if (InFlight) return;
        var me = self.LastEnter;
        if (!GenderChange.CanChange(me.Class))
        {
            context.Log.Notice(ItemData.Text(GenderChange.NotForClassText, "Your class cannot change gender."));
            return;
        }
        Editor.SetLocked(false);
        Editor.Load(GenderChange.AllowedRaces(me.Class), me.Race, me.Face, me.Hair);
        Disarm();
        ShowLook();
        Panel.Visible = true;
        shown = true;
        Preview.AlwaysRender = Panel.PluginReplaced;
        PluginHost.Game.RaiseGenderChange();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        Preview.AlwaysRender = false;
        Editor.CloseColourPicker();
        Preview.Clear();
        PluginHost.Game.RaiseGenderChange();
    }

    private void OnLookChanged()
    {
        if (InFlight) return;
        Disarm();
        ShowLook();
    }

    private void ShowLook()
    {
        Editor.Refresh();
        Preview.Show(Editor.Race, Editor.Face, Editor.HairStyle, Editor.HairColour);
        confirm.Disabled = InFlight;
        PluginHost.Game.RaiseGenderChange();
    }

    private void Disarm()
    {
        armed = false;
        confirm.Text = "Change";
        status.Text = "";
    }

    private void OnConfirmPressed()
    {
        if (!shown || InFlight || context.Self.Dead) return;
        if (!armed)
        {
            armed = true;
            confirm.Text = "Confirm";
            status.Text = $"Your {ItemData.DisplayName(GenderChange.Item).Trim()} will be used. Press Confirm to change.";
            return;
        }
        Submit();
    }

    internal void Submit()
    {
        if (InFlight || context.Self.Dead) return;
        InFlight = true;
        Editor.SetLocked(true);
        confirm.Disabled = true;
        status.Text = "Changing…";
        requests.Send(Editor.Race, Editor.Face, Editor.Hair);
        PluginHost.Game.RaiseGenderChange();
    }

    private void OnRefused(GenderChangeResult result)
    {
        if (!InFlight) return;
        InFlight = false;
        Editor.SetLocked(false);
        Disarm();
        confirm.Disabled = false;
        Close();
        Notice.Show(context.Root, result == GenderChangeResult.NoItem
            ? ItemData.Text(GenderChange.NoItemText, "It does not have items.")
            : ItemData.Text(GenderChange.FailedText, "Gender change failed."));
    }

    private void OnChanged()
    {
        if (!InFlight) return;
        InFlight = false;
        Editor.SetLocked(false);
        Close();
        Notice.Show(context.Root.GetTree().Root, ItemData.Text(GenderChange.ChangedText, "Change current character"));
    }
}
