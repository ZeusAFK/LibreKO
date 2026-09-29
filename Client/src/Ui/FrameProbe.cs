using System.Diagnostics;
using Godot;

namespace LibreKO;

public partial class FrameProbe : Node
{
    private const float Smoothing = 0.05f;

    private static long _processStart, _processEnd, _drawStart, _drawEnd, _lastProcessStart, _queueEnd, _timersAt;
    private static double _process, _flush, _draw, _other, _frame, _queue, _xforms, _tail;
    private static bool _hooked;
    private bool _last;

    internal static (double Process, double Flush, double Draw, double Other) Last { get; private set; }

    internal static bool Detailed;

    internal static string Line() =>
        $"frame split: process {_process:0.0}   flush {_flush:0.0} (queue {_queue:0.0} xforms {_xforms:0.0} tail {_tail:0.0})   draw {_draw:0.0}   other {_other:0.0}   (frame {_frame:0.0} ms)";

    internal static void Install(Node root)
    {
        if (_hooked) return;
        _hooked = true;
        root.AddChild(new FrameProbe { Name = "FrameProbeFirst", ProcessPriority = int.MinValue });
        root.AddChild(new FrameProbe { Name = "FrameProbeLast", ProcessPriority = int.MaxValue, _last = true });
        RenderingServer.FramePreDraw += () => _drawStart = Stopwatch.GetTimestamp();
        RenderingServer.FramePostDraw += OnPostDraw;
    }

    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        if (_last)
        {
            _processEnd = now;
            _queueEnd = _timersAt = 0;
            if (!Detailed) return;
            Callable.From(() => _queueEnd = Stopwatch.GetTimestamp()).CallDeferred();
            GetTree().CreateTimer(0.0, true, false, true).Timeout += () => _timersAt = Stopwatch.GetTimestamp();
            return;
        }
        SpikeLog.Frame(delta);
        if (_lastProcessStart != 0 && _drawEnd > _lastProcessStart)
        {
            double other = Ms(_drawEnd, now);
            Last = Last with { Other = other };
            Blend(ref _other, other);
        }
        if (_lastProcessStart != 0) Blend(ref _frame, Ms(_lastProcessStart, now));
        _processStart = _lastProcessStart = now;
    }

    private static void OnPostDraw()
    {
        _drawEnd = Stopwatch.GetTimestamp();
        if (_processStart == 0 || _processEnd < _processStart || _drawStart < _processEnd) return;
        double process = Ms(_processStart, _processEnd), flush = Ms(_processEnd, _drawStart), draw = Ms(_drawStart, _drawEnd);
        Last = (process, flush, draw, Last.Other);
        Blend(ref _process, process);
        Blend(ref _flush, flush);
        Blend(ref _draw, draw);
        if (_queueEnd >= _processEnd && _timersAt >= _queueEnd && _drawStart >= _timersAt)
        {
            Blend(ref _queue, Ms(_processEnd, _queueEnd));
            Blend(ref _xforms, Ms(_queueEnd, _timersAt));
            Blend(ref _tail, Ms(_timersAt, _drawStart));
        }
    }

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static void Blend(ref double value, double sample) => value += (sample - value) * Smoothing;
}
