using Godot;

namespace LibreKO.Presentation;

internal static class StealthLook
{
    internal const float StealthHiddenAlpha = 0f;

    private const string StealthShadowMeta = "stealth_shadow";
    private const string StealthVisibleMeta = "stealth_visible";

    internal static bool HiddenFromMe(EntityView e) => e.Infiltrating || (e.StealthUndetected && e.Attackable);

    internal static void StealthSetBodyAlpha(Node3D body, float alpha)
    {
        float transparency = Mathf.Clamp(1f - alpha, 0f, 1f);
        bool hidden = alpha <= StealthHiddenAlpha;
        foreach (var node in StealthDescendants(body))
            if (node is GeometryInstance3D gi)
            {
                StealthSetHidden(gi, hidden);
                if (!hidden) Fx.SetTransparency(gi, transparency);
                StealthSetShadow(gi, hidden);
            }
    }

    private static void StealthSetHidden(GeometryInstance3D gi, bool hidden)
    {
        if (hidden)
        {
            if (gi.HasMeta(StealthVisibleMeta)) return;
            gi.SetMeta(StealthVisibleMeta, gi.Visible);
            gi.Visible = false;
        }
        else if (gi.HasMeta(StealthVisibleMeta))
        {
            gi.Visible = gi.GetMeta(StealthVisibleMeta).AsBool();
            gi.RemoveMeta(StealthVisibleMeta);
        }
    }

    private static void StealthSetShadow(GeometryInstance3D gi, bool hidden)
    {
        if (hidden)
        {
            if (gi.HasMeta(StealthShadowMeta)) return;
            gi.SetMeta(StealthShadowMeta, (int)gi.CastShadow);
            gi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        else if (gi.HasMeta(StealthShadowMeta))
        {
            gi.CastShadow = (GeometryInstance3D.ShadowCastingSetting)gi.GetMeta(StealthShadowMeta).AsInt32();
            gi.RemoveMeta(StealthShadowMeta);
        }
    }

    internal static IEnumerable<Node> StealthDescendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var d in StealthDescendants(child))
                yield return d;
        }
    }
}
