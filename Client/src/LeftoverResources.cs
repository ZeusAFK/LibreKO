using Godot;

namespace LibreKO;

internal static class LeftoverResources
{
    private const int MaxRounds = 8;

    internal static void Release()
    {
        double objects = Performance.GetMonitor(Performance.Monitor.ObjectCount);
        for (int round = 0; round < MaxRounds; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            double left = Performance.GetMonitor(Performance.Monitor.ObjectCount);
            if (left >= objects) return;
            objects = left;
        }
    }
}
