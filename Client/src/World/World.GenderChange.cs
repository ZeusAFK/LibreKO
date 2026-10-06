using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int LookPreviewWidth = 220;
    private const int LookPreviewHeight = 300;
    private const float LookTurnDegrees = 30f;

    private CanvasLayer _genderLayer = null!;
    private HudWindow _genderPanel = null!;
    private LookPreview _genderPreview = null!;
    private LookEditor _genderEditor = null!;
    private Label _genderStatus = null!;
    private Button _genderConfirm = null!;
    private bool _genderShown, _genderArmed, _genderInFlight;

    private void GenderChangeInit()
    {
        BuildGenderPanel();
        Net.I.GenderChangeRefusedEvent += OnGenderChangeRefused;
        Net.I.GenderChangedEvent += OnGenderChanged;
    }

    private void GenderChangeDispose()
    {
        Net.I.GenderChangeRefusedEvent -= OnGenderChangeRefused;
        Net.I.GenderChangedEvent -= OnGenderChanged;
    }

    private void BuildGenderPanel()
    {
        _genderLayer = new CanvasLayer { Layer = 74 };
        AddChild(_genderLayer);

        _genderPanel = new HudWindow("genderchange", "Gender Change") { Visible = false };
        _genderPanel.Closed += CloseGenderChange;
        _genderLayer.AddChild(_genderPanel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        _genderPanel.Body.AddChild(row);

        _genderPreview = new LookPreview(LookPreviewWidth, LookPreviewHeight);
        row.AddChild(LookPreviewColumn(_genderPreview));

        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 8);
        row.AddChild(form);
        _genderEditor = new LookEditor();
        _genderEditor.Changed += OnGenderLookChanged;
        form.AddChild(_genderEditor);

        _genderStatus = HudStyle.Label(12);
        _genderStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _genderStatus.CustomMinimumSize = new Vector2(230, 0);
        form.AddChild(_genderStatus);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        form.AddChild(actions);
        _genderConfirm = LookActionButton("Change", OnGenderConfirmPressed);
        actions.AddChild(_genderConfirm);
        actions.AddChild(LookActionButton("Cancel", CloseGenderChange));
    }

    private static Control LookPreviewColumn(LookPreview preview)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        column.AddChild(preview);
        var turnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        turnRow.AddThemeConstantOverride("separation", 8);
        turnRow.AddChild(LookEditor.StepButton("◀", () => preview.Turn(-LookTurnDegrees)));
        turnRow.AddChild(LookEditor.StepButton("▶", () => preview.Turn(LookTurnDegrees)));
        column.AddChild(turnRow);
        return column;
    }

    private static Button LookActionButton(string text, System.Action pressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += pressed;
        return button;
    }

    private void OpenGenderChange()
    {
        var me = Net.I.LastEnter;
        if (!GenderChange.CanChange(me.Class))
        {
            CombatNotice(ItemData.Text(GenderChange.NotForClassText, "Your class cannot change gender."));
            return;
        }
        _genderInFlight = false;
        _genderEditor.Load(GenderChange.AllowedRaces(me.Class), me.Race, me.Face, me.Hair);
        DisarmGenderChange();
        ShowGenderLook();
        _genderPanel.Visible = true;
        _genderShown = true;
    }

    private void CloseGenderChange()
    {
        if (!_genderShown) return;
        _genderShown = false;
        _genderPanel.Visible = false;
        _genderPreview.Clear();
    }

    private void OnGenderLookChanged()
    {
        DisarmGenderChange();
        ShowGenderLook();
    }

    private void ShowGenderLook()
    {
        _genderEditor.Refresh();
        _genderPreview.Show(_genderEditor.Race, _genderEditor.Face, _genderEditor.HairStyle, _genderEditor.HairColour);
        _genderConfirm.Disabled = _genderInFlight;
    }

    private void DisarmGenderChange()
    {
        _genderArmed = false;
        _genderConfirm.Text = "Change";
        SetGenderStatus("", false);
    }

    private void OnGenderConfirmPressed()
    {
        if (_genderInFlight || _selfDead) return;
        if (!_genderArmed)
        {
            _genderArmed = true;
            _genderConfirm.Text = "Confirm";
            SetGenderStatus($"Your {ItemData.DisplayName(GenderChange.Item).Trim()} will be used. Press Confirm to change.", false);
            return;
        }
        _genderInFlight = true;
        _genderConfirm.Disabled = true;
        SetGenderStatus("Changing…", false);
        Net.I.SendGenderChange(_genderEditor.Race, _genderEditor.Face, _genderEditor.Hair);
    }

    private void OnGenderChangeRefused(int result)
    {
        _genderInFlight = false;
        DisarmGenderChange();
        _genderConfirm.Disabled = false;
        string text = result == Net.GenderChangeNoItem
            ? ItemData.Text(GenderChange.NoItemText, "It does not have items.")
            : ItemData.Text(GenderChange.FailedText, "Gender change failed.");
        if (_genderShown) SetGenderStatus(text, true);
        else CombatNotice(text);
    }

    private void OnGenderChanged()
    {
        _genderInFlight = false;
        CloseGenderChange();
    }

    private void SetGenderStatus(string text, bool warn)
    {
        _genderStatus.Text = text;
        _genderStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : Colors.White);
    }
}
