using Godot;

namespace LibreKO;

internal sealed partial class UpgradeSocket : PanelContainer
{
    public event System.Action? Cleared;
    public event System.Action<ItemSlot>? Hovered;
    public event System.Action? Unhovered;
    private readonly TextureRect icon;
    private readonly Label label;
    private readonly Label countLabel;
    private readonly UpgradeBadge plus;
    private readonly bool interactive;
    private ItemSlot held;

    public UpgradeSocket(string labelText, float size, bool interactive = true)
    {
        this.interactive = interactive;
        CustomMinimumSize = new Vector2(size, size);
        AddThemeStyleboxOverride("panel", UiTheme.Slot());

        label = UiTheme.Text(labelText, 11, UiTheme.TextDim, HorizontalAlignment.Center);
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(label);

        icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(icon);

        countLabel = HudStyle.Label(11, HorizontalAlignment.Right);
        countLabel.SetAnchorsPreset(LayoutPreset.BottomRight);
        countLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(countLabel);

        plus = UpgradeBadge.Attach(this);

        MouseEntered += () => { if (!held.IsEmpty) Hovered?.Invoke(held); };
        MouseExited += () => Unhovered?.Invoke();
    }

    public void Set(int itemId, short count, short durability)
    {
        held = new ItemSlot { ItemId = itemId, Count = count, Durability = durability };
        icon.Texture = ItemIcons.Get(itemId);
        label.Visible = false;
        countLabel.Text = count > 1 ? count.ToString() : "";
        plus.Set(itemId);
        AddThemeStyleboxOverride("panel", UiTheme.Slot(UiTheme.Gold));
    }

    public void Clear()
    {
        held = default;
        icon.Texture = null;
        label.Visible = true;
        countLabel.Text = "";
        plus.Clear();
        AddThemeStyleboxOverride("panel", UiTheme.Slot());
        Unhovered?.Invoke();
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (!interactive) return;
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }
            or InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left })
            Cleared?.Invoke();
    }
}
