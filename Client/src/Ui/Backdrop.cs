using System.Collections.Generic;
using Godot;

namespace LibreKO;

public static class Backdrop
{
    public enum Fit
    {
        Cover,

        Framed,
    }

    public const string LoadingSet = "loading";
    public const string LoginSet = "login";

    public const string RetailLoading = "res://assets/backgrounds/loading.jpg";
    public const string RetailPreGame = "res://assets/backgrounds/pre-game.jpg";

    private const int MaxPerSet = 64;

    private static readonly Dictionary<string, string[]> Sets = new();
    private static readonly Dictionary<string, int> LastPick = new();
    private static readonly System.Random Rng = new();

    private static string? _loginArt;
    private static string? _loadingArt;

    public static string[] Paths(string set)
    {
        if (Sets.TryGetValue(set, out var cached)) return cached;
        var found = new List<string>();
        for (int i = 1; i <= MaxPerSet; i++)
        {
            string path = $"res://assets/backgrounds/{set}/{i:00}.jpg";
            if (!ResourceLoader.Exists(path)) break;
            found.Add(path);
        }
        cached = found.ToArray();
        Sets[set] = cached;
        return cached;
    }

    public static string Pick(string set, string fallback)
    {
        var paths = Paths(set);
        if (paths.Length == 0) return fallback;
        if (paths.Length == 1) return paths[0];

        int last = LastPick.TryGetValue(set, out int l) ? l : -1;
        int i;
        if (last < 0) i = Rng.Next(paths.Length);
        else
        {
            i = Rng.Next(paths.Length - 1);
            if (i >= last) i++;
        }
        LastPick[set] = i;
        return paths[i];
    }

    public static string LoginArt => _loginArt ??= Pick(LoginSet, RetailPreGame);

    public static Fit LoginFit => LoginArt == RetailPreGame ? Fit.Cover : Fit.Framed;

    public static void RerollLoginArt() => _loginArt = Pick(LoginSet, RetailPreGame);

    public static string LoadingArt => _loadingArt ??= Pick(LoadingSet, RetailLoading);

    public static void RerollLoadingArt() => _loadingArt = Pick(LoadingSet, RetailLoading);

    public static BackdropRect? Build(Node parent, string resPath, Fit fit = Fit.Cover, bool bottomScrim = false)
    {
        if (!ResourceLoader.Exists(resPath))
        {
            GD.PushWarning($"[ui] background not imported: {resPath}");
            return null;
        }
        var rect = new BackdropRect();
        rect.Setup(ResourceLoader.Load<Texture2D>(resPath), fit, bottomScrim);
        parent.AddChild(rect);
        parent.MoveChild(rect, 0);
        return rect;
    }
}

public partial class BackdropRect : Control
{
    private static float MaxCrop => Platform.Pick(0.34f, 0.58f);

    private static Shader? _blurShader;

    private TextureRect _fill = null!;
    private TextureRect _art = null!;
    private Backdrop.Fit _fit;
    private float _imgAspect = 4f / 3f;

    internal void Setup(Texture2D tex, Backdrop.Fit fit, bool bottomScrim)
    {
        _fit = fit;
        var size = tex.GetSize();
        _imgAspect = size.Y > 0 ? size.X / size.Y : 1f;

        Name = "Backdrop";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _fill = NewRect(tex, TextureRect.StretchModeEnum.KeepAspectCovered);
        _fill.Material = new ShaderMaterial { Shader = BlurShader() };
        _fill.TextureRepeat = TextureRepeatEnum.Disabled;
        AddChild(_fill);

        _art = NewRect(tex, TextureRect.StretchModeEnum.KeepAspectCovered);
        AddChild(_art);

        if (bottomScrim) AddChild(BottomScrim());

        Refit();
    }

    public Material? ArtMaterial => _art?.Material;

    public void SetArtMaterial(Material? material)
    {
        if (_art != null) _art.Material = material;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) Refit();
    }

    private void Refit()
    {
        if (_art == null) return;
        var box = Size;
        float boxAspect = box.Y > 0.001f ? box.X / box.Y : _imgAspect;
        float lo = Mathf.Min(_imgAspect, boxAspect), hi = Mathf.Max(_imgAspect, boxAspect);
        float crop = hi > 0.001f ? 1f - lo / hi : 0f;

        bool framed = crop > MaxCrop || (Platform.PointerUi && _fit == Backdrop.Fit.Framed);
        _art.StretchMode = framed
            ? TextureRect.StretchModeEnum.KeepAspectCentered
            : TextureRect.StretchModeEnum.KeepAspectCovered;
        _fill.Visible = framed && crop > 0.001f;
    }

    private static TextureRect NewRect(Texture2D tex, TextureRect.StretchModeEnum stretch)
    {
        var r = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = stretch,
            MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
        };
        r.SetAnchorsPreset(LayoutPreset.FullRect);
        return r;
    }

    private static Control BottomScrim()
    {
        var grad = new Gradient();
        grad.SetColor(0, new Color(0, 0, 0, 0));
        grad.SetColor(1, new Color(0.01f, 0.01f, 0.02f, 0.88f));
        var scrim = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = grad,
                Width = 4,
                Height = 256,
                FillFrom = new Vector2(0, 0),
                FillTo = new Vector2(0, 1),
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        scrim.AnchorLeft = 0f; scrim.AnchorRight = 1f;
        scrim.AnchorTop = 0.55f; scrim.AnchorBottom = 1f;
        return scrim;
    }

    private static Shader BlurShader() => _blurShader ??= Shaders.Get("backdrop_blur");
}
