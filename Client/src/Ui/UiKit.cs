using Godot;

namespace LibreKO;

public static class UiKit
{
    public const int CharacterPageWidth = 560;

    public const float PartyHudWidth = 290f;
    public const float PartyHudTop = 176f;

    public const float TargetHudWidth = 240f;

    private const float MenuCursorGap = 2f;

    public static void PopupAtCursor(Node host, Popup menu, Vector2 cursor)
    {
        menu.ResetSize();
        var screen = host.GetViewport().GetVisibleRect().Size;
        menu.Position = (Vector2I)TooltipPlacement.AroundCursor(cursor, menu.Size, screen, MenuCursorGap, 0f);
        menu.Popup();
    }

    private const int ValueRowFontSize = 14;

    public static void ClearChildren(Node parent)
    {
        foreach (var c in parent.GetChildren())
        {
            parent.RemoveChild(c);
            c.QueueFree();
        }
    }

    public static ItemSlot TooltipItem(int itemId, int count = 1)
    {
        var def = ItemData.Get(itemId);
        int durability = (def?.Duration ?? 0) + (ItemData.ExtFor(itemId)?.DurationBonus ?? 0);
        return new ItemSlot
        {
            ItemId = itemId,
            Count = (short)count,
            Durability = (short)Mathf.Min(durability, short.MaxValue),
        };
    }

    public static HBoxContainer ValueRow(string name, string value, Color color)
    {
        var row = new HBoxContainer();
        var label = UiTheme.Text(name, ValueRowFontSize, UiTheme.TextHi);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        row.AddChild(UiTheme.Text(value, ValueRowFontSize, color, HorizontalAlignment.Right));
        return row;
    }

    public static string QuestRewardName(int displayItemId) => displayItemId switch
    {
        QuestData.CoinItemId => "Gold",
        QuestData.ExpItemId => "Experience",
        QuestData.LadderPointItemId => "Ladder points",
        _ => ItemData.DisplayName(displayItemId)
    };

    public static string GoldAmount(long value) => $"{value:n0} gold";

    public static void Wrap(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(1, 0);
    }

    public static Color TopIconColor(bool waiting) => waiting ? UiTheme.GoldBright : UiTheme.TextLo;

    public static void ShowOnTop(Control window)
    {
        window.Visible = true;
        window.GetParent()?.MoveChild(window, window.GetParent().GetChildCount() - 1);
    }

    public const float StatusHudGap = 6f;
    public const float HuntBarHeight = 4f;
    public const string BattleBannerLayoutId = "hud_battle_banner";
    public const string BattleBoardLayoutId = "hud_battle_score";

    public const int AnvilSocketGap = 5;
    public static readonly Color WhRefusalTint = Color.Color8(255, 59, 59);
    private const int UpgradeColumnSeparation = 6;
    private const int UpgradeCaptionFontSize = 11;
    private const int BenchSignFontSize = 22;
    private static readonly Color HuntTrack = new(0.05f, 0.05f, 0.05f, 0.9f);
    private static readonly Dictionary<Color, StyleBoxFlat> MeterFills = Shutdown.Track(new Dictionary<Color, StyleBoxFlat>());

    public static StyleBoxFlat CachedMeterFill(Color tint)
    {
        if (!MeterFills.TryGetValue(tint, out var fill))
            MeterFills[tint] = fill = UiTheme.MeterFill(tint);
        return fill;
    }

    public static Control UpgradeColumn(string caption, Control body)
    {
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", UpgradeColumnSeparation);
        col.AddChild(UiTheme.Text(caption, UpgradeCaptionFontSize, UiTheme.TextDim, HorizontalAlignment.Center));
        var centre = new CenterContainer();
        centre.AddChild(body);
        col.AddChild(centre);
        return col;
    }

    public static Control BenchSign(string sign) => UpgradeColumn(" ", UiTheme.Text(sign, BenchSignFontSize, UiTheme.GoldDark));

    public static bool IsStackable(int itemId) => ItemStacks.IsStackable(itemId);

    public static string ExpirationText(int expiresAt)
    {
        var at = System.DateTimeOffset.FromUnixTimeSeconds(expiresAt).LocalDateTime;
        return ItemData.Text(GameText.Expiration, "Expiration     :  %Y-%m-%d    %H:%M")
            .Replace("%Y", at.Year.ToString("0000")).Replace("%m", at.Month.ToString("00"))
            .Replace("%d", at.Day.ToString("00")).Replace("%H", at.Hour.ToString("00"))
            .Replace("%M", at.Minute.ToString("00"));
    }

    public static bool AsksStackAmount(ItemSlot slot) => Platform.TouchUi && IsStackable(slot.ItemId) && slot.Count > 1;

    public static Vector2 Footprint(Control window)
    {
        Vector2 min = window.GetCombinedMinimumSize();
        return new Vector2(Mathf.Max(window.Size.X, min.X), Mathf.Max(window.Size.Y, min.Y)) * window.Scale;
    }

    public static Control HuntBar(int count, int goal)
    {
        var track = new ColorRect
        {
            Color = HuntTrack,
            CustomMinimumSize = new Vector2(0, HuntBarHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var fill = new ColorRect
        {
            Color = count >= goal ? UiTheme.Good : UiTheme.GoldBright,
            AnchorRight = goal > 0 ? Mathf.Clamp((float)count / goal, 0f, 1f) : 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        track.AddChild(fill);
        return track;
    }

    public static readonly Color OrderMessageYellow = new(1f, 1f, 0f);

    public static string NationName(int nation) => nation switch
    {
        Nations.Karus => "Karus",
        Nations.ElMorad => "El Morad",
        _ => "Neutral",
    };

    public static Color NationColor(int nation) => nation switch
    {
        Nations.Karus => new Color("d08a4a"),
        Nations.ElMorad => new Color("5a9ad0"),
        _ => UiTheme.TextLo,
    };

    public static Button SubTabButton<TKey>(
        string label,
        TKey key,
        System.Action onPress,
        System.Collections.Generic.Dictionary<TKey, Button> registry)
        where TKey : notnull
    {
        var button = new Button
        {
            Text = label,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.Pressed += () => onPress();
        registry[key] = button;
        return button;
    }
}
