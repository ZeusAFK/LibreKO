using System.Diagnostics;
using Godot;

namespace LibreKO;

public partial class FrameProbe : Node
{
    private const float Smoothing = 0.05f;

    private static long processStart, processEnd, drawStart, drawEnd, lastProcessStart, queueEnd, timersAt;
    private static double process, flush, draw, other, frame, queue, xforms, tail;
    private static bool hooked;
    private bool last;

    internal static (double Process, double Flush, double Draw, double Other) Last { get; private set; }

    internal static bool Detailed;

    internal static string Line() =>
        $"frame split: process {process:0.0}   flush {flush:0.0} (queue {queue:0.0} xforms {xforms:0.0} tail {tail:0.0})   draw {draw:0.0}   other {other:0.0}   (frame {frame:0.0} ms)";

    internal static void Install(Node root)
    {
        if (hooked) return;
        hooked = true;
        root.AddChild(new FrameProbe { Name = "FrameProbeFirst", ProcessPriority = int.MinValue });
        root.AddChild(new FrameProbe { Name = "FrameProbeLast", ProcessPriority = int.MaxValue, last = true });
        RenderingServer.FramePreDraw += () => drawStart = Stopwatch.GetTimestamp();
        RenderingServer.FramePostDraw += OnPostDraw;
    }

    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        if (last)
        {
            processEnd = now;
            queueEnd = timersAt = 0;
            if (!Detailed) return;
            Callable.From(() => queueEnd = Stopwatch.GetTimestamp()).CallDeferred();
            GetTree().CreateTimer(0.0, true, false, true).Timeout += () => timersAt = Stopwatch.GetTimestamp();
            return;
        }
        SpikeLog.Frame(delta);
        if (lastProcessStart != 0 && drawEnd > lastProcessStart)
        {
            double other = Ms(drawEnd, now);
            Last = Last with { Other = other };
            Blend(ref FrameProbe.other, other);
        }
        if (lastProcessStart != 0) Blend(ref frame, Ms(lastProcessStart, now));
        processStart = lastProcessStart = now;
    }

    private static void OnPostDraw()
    {
        drawEnd = Stopwatch.GetTimestamp();
        if (processStart == 0 || processEnd < processStart || drawStart < processEnd) return;
        double process = Ms(processStart, processEnd), flush = Ms(processEnd, drawStart), draw = Ms(drawStart, drawEnd);
        Last = (process, flush, draw, Last.Other);
        Blend(ref FrameProbe.process, process);
        Blend(ref FrameProbe.flush, flush);
        Blend(ref FrameProbe.draw, draw);
        if (queueEnd >= processEnd && timersAt >= queueEnd && drawStart >= timersAt)
        {
            Blend(ref queue, Ms(processEnd, queueEnd));
            Blend(ref xforms, Ms(queueEnd, timersAt));
            Blend(ref tail, Ms(timersAt, drawStart));
        }
    }

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static void Blend(ref double value, double sample) => value += (sample - value) * Smoothing;
}
