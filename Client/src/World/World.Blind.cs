using Godot;

namespace LibreKO;

public partial class World
{
    private const int BlindLayerIndex = 50;

    private ColorRect? _blindRect;
    private BlindEffect _blind;
    private float _blindAlpha;
    private bool _othersHidden;

    private void StartBlind(int buffType, double seconds)
    {
        var effect = BlindEffect.Start(buffType, Now(), seconds);
        if (effect.Mode == BlindMode.None) return;
        _blind = effect;
        EnsureBlindLayer();
        Deselect();
    }

    private void EnsureBlindLayer()
    {
        if (_blindRect != null) return;
        var layer = new CanvasLayer { Layer = BlindLayerIndex };
        AddChild(layer);
        _blindRect = new ColorRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _blindRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_blindRect);
    }

    private void BlindTick(double now)
    {
        if (_blindRect == null) return;
        HideOthersWhileBlind(_blind.HidesOthers(now));
        float alpha = _blind.Alpha(now);
        if (Mathf.IsEqualApprox(alpha, _blindAlpha)) return;
        _blindAlpha = alpha;
        _blindRect.Visible = alpha > 0f;
        float grey = _blind.Grey;
        _blindRect.Color = new Color(grey, grey, grey, alpha);
    }

    private void HideOthersWhileBlind(bool hide)
    {
        if (!hide && !_othersHidden) return;
        _othersHidden = hide;
        foreach (var e in _ents.Values)
            if (e.Body != null && GodotObject.IsInstanceValid(e.Body) && e.Body.Visible == hide
                && !(e.GateBlocker != null && e.Dead))
                e.Body.Visible = !hide;
    }

    private void OnSecondaryBuffLanded(SkillData.Skill s, int affected, int seconds, int speedPercent)
    {
        if (affected != _myId || _buffPanel == null || seconds <= 0) return;
        AddBuffChip(s, Now() + seconds);
        ApplySelfStatus(s.Buff2Type, speedPercent, seconds);
    }
}
