using Godot;

namespace LibreKO.Features.Windows;

internal sealed class UiOpacityFeature : IFeature
{
    private readonly IWorldContext context;

    public UiOpacityFeature(IWorldContext context)
    {
        this.context = context;
    }

    private const string Node = "UiOpacity";

    public void Build()
    {
        context.Root.GetTree().NodeAdded += OnNodeAdded;
        Config.UiAlphaChanged += ApplyEverywhere;
        Callable.From(ApplyEverywhere).CallDeferred();
    }

    public void Dispose()
    {
        context.Root.GetTree().NodeAdded -= OnNodeAdded;
        Config.UiAlphaChanged -= ApplyEverywhere;
    }

    private void OnNodeAdded(Node node)
    {
        if (node is CanvasLayer layer) Callable.From(() => Apply(layer)).CallDeferred();
    }

    private void ApplyEverywhere()
    {
        foreach (var node in context.Root.GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false))
            if (node is CanvasLayer layer) Apply(layer);
    }

    private static void Apply(CanvasLayer layer)
    {
        if (!GodotObject.IsInstanceValid(layer) || !UiOpacityRules.Fades(layer.Layer)) return;
        var tint = layer.GetNodeOrNull<CanvasModulate>(Node);
        if (Config.UiAlpha >= UiOpacityRules.Opaque)
        {
            tint?.QueueFree();
            return;
        }
        if (tint == null)
        {
            tint = new CanvasModulate { Name = Node };
            layer.AddChild(tint);
        }
        tint.Color = new Color(1f, 1f, 1f, UiOpacityRules.Fraction(Config.UiAlpha));
    }
}
