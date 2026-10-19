using System;
using Godot;

namespace LibreKO;

public partial class Flipbook : TextureRect
{
    private Texture2D[] frames = Array.Empty<Texture2D>();
    private int frame;
    private double elapsed, frameTime;
    private Action? done;

    public Flipbook()
    {
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.Scale;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(false);
    }

    public void Play(Texture2D[] frames, double fps, Action? done = null)
    {
        this.frames = frames;
        frame = 0;
        elapsed = 0;
        frameTime = 1.0 / Math.Max(1.0, fps);
        this.done = done;
        Texture = frames.Length > 0 ? frames[0] : null;
        Visible = frames.Length > 0;
        SetProcess(frames.Length > 0);
        if (frames.Length == 0) Finish();
    }

    public void Stop()
    {
        SetProcess(false);
        done = null;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        elapsed += delta;
        while (elapsed >= frameTime)
        {
            elapsed -= frameTime;
            frame++;
            if (frame >= frames.Length)
            {
                SetProcess(false);
                Finish();
                return;
            }
            Texture = frames[frame];
        }
    }

    private void Finish()
    {
        var done = this.done;
        this.done = null;
        done?.Invoke();
    }
}
