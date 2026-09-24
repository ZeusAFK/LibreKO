using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public readonly record struct VolleyCandidate(int Id, float X, float Z, float Radius);

public static class Volley
{
    private const float StepRadians = MathF.PI / 12f;

    public static float[] Offsets(int arrows)
    {
        int perSide = (Math.Max(1, arrows) - 1) >> 1;
        var offsets = new float[1 + 2 * perSide];
        for (int i = 1; i <= perSide; i++)
        {
            offsets[2 * i - 1] = -i * StepRadians;
            offsets[2 * i] = i * StepRadians;
        }
        return offsets;
    }

    public static int FirstAlong(float fromX, float fromZ, float toX, float toZ, IEnumerable<VolleyCandidate> candidates)
    {
        float dx = toX - fromX, dz = toZ - fromZ;
        float lengthSq = dx * dx + dz * dz;
        int best = -1;
        float bestT = float.MaxValue;
        foreach (var c in candidates)
        {
            float t = lengthSq > 0f ? ((c.X - fromX) * dx + (c.Z - fromZ) * dz) / lengthSq : 0f;
            t = Math.Clamp(t, 0f, 1f);
            float px = fromX + dx * t - c.X, pz = fromZ + dz * t - c.Z;
            if (px * px + pz * pz > c.Radius * c.Radius || t >= bestT) continue;
            bestT = t;
            best = c.Id;
        }
        return best;
    }
}
