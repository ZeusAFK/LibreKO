using System;
using Godot;

namespace LibreKO;

public partial class Flipbook : TextureRect
{
    private Texture2D[] _frames = Array.Empty<Texture2D>();
    private int _frame;
    private double _elapsed, _frameTime;
    private Action? _done;

    public Flipbook()
    {
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.Scale;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(false);
    }

    public void Play(Texture2D[] frames, double fps, Action? done = null)
    {
        _frames = frames;
        _frame = 0;
        _elapsed = 0;
        _frameTime = 1.0 / Math.Max(1.0, fps);
        _done = done;
        Texture = frames.Length > 0 ? frames[0] : null;
        Visible = frames.Length > 0;
        SetProcess(frames.Length > 0);
        if (frames.Length == 0) Finish();
    }

    public void Stop()
    {
        SetProcess(false);
        _done = null;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        while (_elapsed >= _frameTime)
        {
            _elapsed -= _frameTime;
            _frame++;
            if (_frame >= _frames.Length)
            {
                SetProcess(false);
                Finish();
                return;
            }
            Texture = _frames[_frame];
        }
    }

    private void Finish()
    {
        var done = _done;
        _done = null;
        done?.Invoke();
    }
}
