using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _guardPetLayer = null!;
    private PanelContainer _guardPetBar = null!;
    private ProgressBar _guardPetHpBar = null!;
    private Label _guardPetHpLabel = null!;
    private bool _guardPetActive;

    private void GuardPetInit()
    {
        BuildGuardPetBar();
        Net.I.GuardPetStatusEvent += OnGuardPetStatus;
        Net.I.SendGuardPetStatus();
    }

    private void GuardPetDispose()
    {
        Net.I.GuardPetStatusEvent -= OnGuardPetStatus;
    }

    private void BuildGuardPetBar()
    {
        _guardPetLayer = new CanvasLayer { Layer = 68 };
        AddChild(_guardPetLayer);

        _guardPetBar = new PanelContainer
        {
            Visible = false,
            Position = new Vector2(20, 150),
            CustomMinimumSize = new Vector2(190, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _guardPetBar.AddThemeStyleboxOverride("panel", UiTheme.Panel());
        _guardPetLayer.AddChild(_guardPetBar);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8, 6, 8, 6);
        _guardPetBar.AddChild(margin);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 3);
        margin.AddChild(col);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        col.AddChild(header);

        var title = UiTheme.Text("Guard Pet", 12, UiTheme.Gold);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        header.AddChild(title);

        _guardPetHpLabel = UiTheme.Text("0 / 0", 11, UiTheme.TextHi, HorizontalAlignment.Right);
        _guardPetHpLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        header.AddChild(_guardPetHpLabel);

        _guardPetHpBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 10),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        StyleGuardPetHpBar();
        col.AddChild(_guardPetHpBar);

        HudLayout.Attach(_guardPetBar, "hud_guard_pet", header, () => _guardPetBar.Position);
    }

    private void StyleGuardPetHpBar()
    {
        var bg = new StyleBoxFlat
        {
            BgColor = UiTheme.SlotBg,
            BorderColor = UiTheme.SlotBorder,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        };
        bg.SetBorderWidthAll(1);
        var fill = new StyleBoxFlat
        {
            BgColor = UiTheme.Hp,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        };
        _guardPetHpBar.AddThemeStyleboxOverride("background", bg);
        _guardPetHpBar.AddThemeStyleboxOverride("fill", fill);
    }

    private void OnGuardPetStatus(GuardPetStatus status)
    {
        bool wasActive = _guardPetActive;
        _guardPetActive = status.Active && status.MaxHp > 0;

        if (!_guardPetActive)
        {
            _guardPetBar.Visible = false;
            if (wasActive) Chat.Info("Your guard pet is no longer deployed.");
            return;
        }

        int hp = Mathf.Clamp(status.Hp, 0, status.MaxHp);
        _guardPetHpBar.MaxValue = status.MaxHp;
        _guardPetHpBar.Value = hp;
        _guardPetHpLabel.Text = $"{hp:n0} / {status.MaxHp:n0}";
        _guardPetBar.Visible = true;

        if (!wasActive)
        {
            Chat.Info("Your guard pet is deployed.");
        }
    }
}
