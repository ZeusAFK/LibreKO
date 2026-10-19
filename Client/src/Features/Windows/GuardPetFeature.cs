using Godot;

namespace LibreKO.Features.Windows;

internal sealed class GuardPetFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly GuardPetSession guardPet;
    private readonly IGuardPetRequests requests;

    public GuardPetFeature(IWorldContext context, GuardPetSession guardPet, IGuardPetRequests requests)
    {
        this.context = context;
        this.guardPet = guardPet;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;
    private PanelContainer bar = null!;
    private ProgressBar hpBar = null!;
    private Label hpLabel = null!;
    private bool active;

    public void Build()
    {
        BuildBar();
        guardPet.GuardPetStatusEvent += OnStatus;
        requests.Status();
    }

    public void Dispose()
    {
        guardPet.GuardPetStatusEvent -= OnStatus;
    }

    private void BuildBar()
    {
        layer = new CanvasLayer { Layer = UiLayers.Announcements };
        context.Root.AddChild(layer);

        bar = new PanelContainer
        {
            Visible = false,
            Position = new Vector2(20, 150),
            CustomMinimumSize = new Vector2(190, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("panel", UiTheme.Panel());
        layer.AddChild(bar);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8, 6, 8, 6);
        bar.AddChild(margin);

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

        hpLabel = UiTheme.Text("0 / 0", 11, UiTheme.TextHi, HorizontalAlignment.Right);
        hpLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        header.AddChild(hpLabel);

        hpBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 10),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        StyleHpBar();
        col.AddChild(hpBar);

        HudLayout.Attach(bar, "hud_guard_pet", header, () => bar.Position);
    }

    private void StyleHpBar()
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
        hpBar.AddThemeStyleboxOverride("background", bg);
        hpBar.AddThemeStyleboxOverride("fill", fill);
    }

    private void OnStatus(GuardPetStatus status)
    {
        bool wasActive = active;
        active = status.Active && status.MaxHp > 0;

        if (!active)
        {
            bar.Visible = false;
            if (wasActive) context.Log.Notice("Your guard pet is no longer deployed.");
            return;
        }

        int hp = Mathf.Clamp(status.Hp, 0, status.MaxHp);
        hpBar.MaxValue = status.MaxHp;
        hpBar.Value = hp;
        hpLabel.Text = $"{hp:n0} / {status.MaxHp:n0}";
        bar.Visible = true;

        if (!wasActive)
        {
            context.Log.Notice("Your guard pet is deployed.");
        }
    }
}
