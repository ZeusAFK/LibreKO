using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _eventExitLayer = null!;
    private Button _eventExitBtn = null!;
    private ConfirmationDialog _eventExitDialog = null!;

    private void EventExitInit()
    {
        _eventExitLayer = new CanvasLayer { Layer = 90 };
        AddChild(_eventExitLayer);

        _eventExitBtn = new Button
        {
            Text = "🚪 Exit Event",
            CustomMinimumSize = new Vector2(115, 30),
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Leave the event and return to Moradon\n(Keluar dari event dan kembali ke Moradon)",
            Visible = ZoneCatalog.IsEventZone(_zone)
        };

        _eventExitBtn.AnchorLeft = 1f;
        _eventExitBtn.AnchorRight = 1f;
        _eventExitBtn.AnchorTop = 0f;
        _eventExitBtn.AnchorBottom = 0f;
        _eventExitBtn.OffsetLeft = -135f;
        _eventExitBtn.OffsetRight = -20f;
        _eventExitBtn.OffsetTop = 180f;
        _eventExitBtn.OffsetBottom = 210f;

        var normalBox = UiTheme.Panel(4, true);
        normalBox.BorderColor = new Color("b8860b"); // Dark goldenrod
        normalBox.BgColor = new Color(0.15f, 0.05f, 0.05f, 0.90f); // Deep crimson tinted glass

        var hoverBox = UiTheme.Panel(4, true);
        hoverBox.BorderColor = new Color("ffd700"); // Bright gold
        hoverBox.BgColor = new Color(0.30f, 0.08f, 0.08f, 0.95f);

        _eventExitBtn.AddThemeStyleboxOverride("normal", normalBox);
        _eventExitBtn.AddThemeStyleboxOverride("hover", hoverBox);
        _eventExitBtn.AddThemeStyleboxOverride("pressed", hoverBox);
        _eventExitBtn.AddThemeColorOverride("font_color", new Color("ffeedd"));
        _eventExitBtn.AddThemeColorOverride("font_hover_color", new Color("ffffff"));
        _eventExitBtn.AddThemeFontSizeOverride("font_size", 12);

        _eventExitDialog = new ConfirmationDialog { Title = "Exit Event" };
        _eventExitDialog.DialogText = "Are you sure you want to leave the event and return to Moradon?\n(Apakah Anda yakin ingin keluar dari event dan kembali ke Moradon?)";
        _eventExitDialog.GetOkButton().Text = "Leave Event";
        _eventExitDialog.GetCancelButton().Text = "Cancel";
        _eventExitDialog.Confirmed += EventExitConfirm;

        _eventExitBtn.Pressed += () => _eventExitDialog.PopupCentered();

        _eventExitLayer.AddChild(_eventExitBtn);
        _eventExitLayer.AddChild(_eventExitDialog);
    }

    private void EventExitConfirm()
    {
        Net.I.SendBifrostDisband();
        Chat.Info("Exiting event, returning to Moradon…");
        Audio.Play(Sfx.WarpZone, _self.GlobalPosition);
    }

    private void EventExitDispose()
    {
        if (IsInstanceValid(_eventExitLayer))
            _eventExitLayer.QueueFree();
    }
}
