using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const double RespawnRetrySeconds = 3.0;
    private const string RespawnHint = "Press OK to teleport back to the re-spawn point.";
    private const string RespawnSentHint = "Returning to the re-spawn point…";
    private const string RespawnRetryHint = "No answer from the server — press OK to try again.";

    private CanvasLayer _deathLayer = null!;
    private PanelContainer _deathPanel = null!;
    private Label _deathExpLbl = null!;
    private Label _deathHintLbl = null!;
    private Button _deathOkBtn = null!;
    private long _deathExpLost;
    private int _respawnToken;
    private bool _respawnPending;

    private void DeathDialogInit()
    {
        BuildDeathDialog();
        Net.I.DeathExpLossEvent += OnDeathExpLoss;
    }

    private void DeathDialogDispose()
    {
        Net.I.DeathExpLossEvent -= OnDeathExpLoss;
    }

    private void BuildDeathDialog()
    {
        _deathLayer = new CanvasLayer { Layer = 90, Visible = false };
        AddChild(_deathLayer);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        _deathLayer.AddChild(dim);

        _deathPanel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        _deathPanel.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        _deathLayer.AddChild(_deathPanel);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 26, 22, 26, 20);
        _deathPanel.AddChild(margin);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        col.AddThemeConstantOverride("separation", 8);
        margin.AddChild(col);

        _deathExpLbl = UiTheme.Text("", 14, UiTheme.Bad, HorizontalAlignment.Center);
        _deathExpLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_deathExpLbl);

        _deathHintLbl = UiTheme.Text(RespawnHint, 13, UiTheme.TextLo, HorizontalAlignment.Center);
        _deathHintLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_deathHintLbl);

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        _deathOkBtn = new Button
        {
            Text = "OK",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(120, 30),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        _deathOkBtn.Pressed += ConfirmRespawn;
        col.AddChild(_deathOkBtn);
    }

    private void OnDeathExpLoss(long lost)
    {
        _deathExpLost = lost;
        RefreshDeathDialogText();
    }

    private void RefreshDeathDialogText()
    {
        _deathExpLbl.Text = _deathExpLost > 0
            ? $"Lost {_deathExpLost:n0} experience points."
            : "You have been defeated.";
    }

    private void ShowDeathDialog()
    {
        RefreshDeathDialogText();
        ArmRespawnButton(RespawnHint);
        _deathLayer.Visible = true;
    }

    private void HideDeathDialog()
    {
        _deathExpLost = 0;
        _respawnPending = false;
        _respawnToken++;
        _deathLayer.Visible = false;
    }

    private void ArmRespawnButton(string hint)
    {
        _respawnPending = false;
        _deathOkBtn.Disabled = false;
        _deathHintLbl.Text = hint;
    }

    private void MarkRespawnPending()
    {
        _respawnPending = true;
        _deathOkBtn.Disabled = true;
        _deathHintLbl.Text = RespawnSentHint;
    }

    private void ConfirmRespawn()
    {
        if (!_selfDead) { HideDeathDialog(); return; }
        if (_respawnPending) return;

        MarkRespawnPending();

        int token = ++_respawnToken;
        Net.I.SendRegene();

        GetTree().CreateTimer(RespawnRetrySeconds).Timeout += () =>
        {
            if (token != _respawnToken || !_selfDead) return;
            ArmRespawnButton(RespawnRetryHint);
        };
    }
}
