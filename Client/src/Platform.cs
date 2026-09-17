using Godot;

namespace LibreKO;

public static class Platform
{
    public enum UiMode
    {
        Auto,
        Touch,
        Pointer,
    }

    public static UiMode? Override { get; set; }

    public static bool BundledContent => OS.HasFeature("android");

    public static bool HasTouchscreen => DisplayServer.IsTouchscreenAvailable();

    public static bool TouchUi => (Override ?? Config.TouchUi) switch
    {
        UiMode.Touch => true,
        UiMode.Pointer => false,
        _ => HasTouchscreen,
    };

    public static bool PointerUi => !TouchUi;

    public const float MenuDesignDpi = 190f;
    public const float MenuScaleMin = 1.2f;
    public const float MenuScaleMax = 2.6f;

    public static bool MenuScreens { get; set; }

    public static float? MenuScaleOverride { get; set; }

    public static float TouchMenuScale
    {
        get
        {
            if (!TouchUi) return 1f;
            if (MenuScaleOverride is { } forced) return forced;
            return Mathf.Clamp(ScreenDpi() / MenuDesignDpi, MenuScaleMin, MenuScaleMax);
        }
    }

    public static float MenuScale =>
        MenuScreens && TouchUi ? TouchMenuScale / Config.UiScaleMobileDefault : 1f;

    public const float TouchDesignDpi = 420f;
    public const float TouchPixelMin = 0.7f;
    public const float TouchPixelMax = 1.8f;

    public static float? TouchPixelOverride { get; set; }

    public static float TouchPixel
    {
        get
        {
            if (!TouchUi) return 1f;
            if (TouchPixelOverride is { } forced) return forced;
            return Mathf.Clamp(ScreenDpi() / TouchDesignDpi, TouchPixelMin, TouchPixelMax);
        }
    }

    public static T Pick<T>(T pointer, T touch) => TouchUi ? touch : pointer;

    private static float ScreenDpi()
    {
        if (DisplayServer.GetName() == "headless") return MenuDesignDpi;
        int dpi = DisplayServer.ScreenGetDpi();
        return dpi > 0 ? dpi : MenuDesignDpi;
    }
}
