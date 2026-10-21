using System;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public sealed partial class IconVeilView : Control
{
    private const string BadgeArt = "res://assets/ui/item_badges/";
    private static readonly Color ExhaustedWash = Color.Color8(200, 20, 20, 100);
    private static readonly Color RentedWash = Color.Color8(255, 255, 128, 70);
    private static readonly Color DuplicateWash = Color.Color8(40, 40, 40, 160);
    private static readonly Color SealedWash = Color.Color8(20, 20, 255, 70);

    private static Texture2D? notBound, bound;
    private IconWash wash;
    private IconBadge badge;
    private const double ExpiryCheckSeconds = 1;
    private ItemSlot shown;
    private double sinceCheck;

    public IconVeilView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public static IconVeilView Attach(Control parent)
    {
        var veil = new IconVeilView();
        parent.AddChild(veil);
        return veil;
    }

    public override void _Ready() => SetProcess(RunsOut);

    public override void _Process(double delta)
    {
        sinceCheck += delta;
        if (sinceCheck < ExpiryCheckSeconds) return;
        sinceCheck = 0;
        if (shown.HasExpired(DateTimeOffset.UtcNow.ToUnixTimeSeconds())) Set(shown);
    }

    private bool RunsOut => wash != IconWash.Expired && !shown.IsEmpty && shown.ExpiresAt > 0;

    public void Set(ItemSlot slot)
    {
        shown = slot;
        var wash = IconVeil.Wash(slot, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var badge = slot.IsEmpty ? IconBadge.None : IconVeil.Badge(ItemData.ShownFlag(slot));
        var changed = wash != this.wash || badge != this.badge;
        this.wash = wash;
        this.badge = badge;
        SetProcess(RunsOut);
        if (changed) QueueRedraw();
    }

    public void Clear() => Set(default);

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        Color? washColor = wash switch
        {
            IconWash.Exhausted or IconWash.Expired => ExhaustedWash,
            IconWash.Rented => RentedWash,
            IconWash.Duplicate => DuplicateWash,
            IconWash.Sealed => SealedWash,
            _ => null,
        };
        if (washColor is { } colour) DrawRect(rect, colour);
        var badgeTexture = badge switch
        {
            IconBadge.NotBound => notBound ??= GD.Load<Texture2D>(BadgeArt + "hermetic_seal.png"),
            IconBadge.Bound => bound ??= GD.Load<Texture2D>(BadgeArt + "belong.png"),
            _ => null,
        };
        if (badgeTexture != null) DrawTextureRect(badgeTexture, rect, false);
    }
}
