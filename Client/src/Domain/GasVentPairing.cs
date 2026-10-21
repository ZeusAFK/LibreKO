namespace LibreKO.Domain;

public static class GasVentPairing
{
    public static List<(int Vent, int Spray)> Pair(IReadOnlyList<(float X, float Z)> vents, IReadOnlyList<(float X, float Z)> sprays, float maxDistance)
    {
        var candidates = new List<(float Distance, int Vent, int Spray)>();
        for (int vent = 0; vent < vents.Count; vent++)
            for (int spray = 0; spray < sprays.Count; spray++)
            {
                float dx = vents[vent].X - sprays[spray].X, dz = vents[vent].Z - sprays[spray].Z;
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance <= maxDistance) candidates.Add((distance, vent, spray));
            }
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var ventTaken = new bool[vents.Count];
        var sprayTaken = new bool[sprays.Count];
        var pairs = new List<(int Vent, int Spray)>();
        foreach (var (_, vent, spray) in candidates)
        {
            if (ventTaken[vent] || sprayTaken[spray]) continue;
            ventTaken[vent] = sprayTaken[spray] = true;
            pairs.Add((vent, spray));
        }
        return pairs;
    }
}
