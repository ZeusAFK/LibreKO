namespace LibreKO.Network;

public static class NationTransferWire
{
    public const NationTransferResult SubmitCompleted = (NationTransferResult)2;
    public const int FailedText = 16700;

    private static readonly Dictionary<byte, int> RefusalTexts = new()
    {
        [(byte)NationTransferResult.InClan] = 16702,
        [(byte)NationTransferResult.IsKing] = 16703,
        [(byte)NationTransferResult.WrongCharacter] = 16705,
        [(byte)NationTransferResult.NoCharacter] = 16706,
        [(byte)NationTransferResult.NoItem] = 16710,
    };

    private static readonly Dictionary<byte, int> SubmitRefusalTexts = new()
    {
        [(byte)NationTransferResult.InInvestigationTeam] = 16704,
        [(byte)NationTransferResult.MakeOverActive] = 18906,
        [(byte)NationTransferResult.NotEnoughCoins] = 11303,
    };

    public static List<NationTransferCandidate>? ReadCandidates(Packet p)
    {
        var list = new List<NationTransferCandidate>();
        var slots = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            int count = p.ReadByte();
            for (int i = 0; i < count; i++)
            {
                int slot = p.ReadShort();
                string name = p.ReadString();
                int race = p.ReadByte();
                int nation = p.ReadByte();
                int cls = p.ReadShort();
                int face = p.ReadByte();
                int hair = p.ReadInt();
                if (slot < 0 || !slots.Add(slot) || name.Length == 0 || !names.Add(name)
                    || nation is not (Nations.Karus or Nations.ElMorad) || Nations.OfClass(cls) != nation
                    || (list.Count > 0 && list[0].Nation != nation)
                    || !GenderChange.AllowedRaces(cls).Contains(race))
                    return null;
                list.Add(new NationTransferCandidate(slot, name, race, nation, cls, face, hair));
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }
        return p.RemainingBytes == 0 ? list : null;
    }

    public static bool PicksMatch(IReadOnlyList<NationTransferCandidate> candidates, IReadOnlyList<NationTransferPick> picks)
    {
        if (picks.Count == 0 || picks.Count != candidates.Count || picks.Count > byte.MaxValue
            || picks.Select(pick => pick.Slot).Distinct().Count() != picks.Count)
            return false;
        return picks.All(pick => candidates.FirstOrDefault(candidate => candidate.Slot == pick.Slot) is { } candidate
                                 && candidate.Name == pick.Name
                                 && pick.Face is >= byte.MinValue and <= byte.MaxValue
                                 && GenderChange.AllowedRaces(candidate.Class).Contains(pick.Race));
    }

    public static bool IsRefusal(NationTransferResult result) =>
        result is NationTransferResult.Failed or NationTransferResult.InClan or NationTransferResult.IsKing or NationTransferResult.WrongCharacter or NationTransferResult.NoCharacter or NationTransferResult.NoItem;

    public static bool IsSubmitSuccess(NationTransferResult result) =>
        result is NationTransferResult.Accepted or SubmitCompleted;

    public static bool IsRefusal(NationTransferSubOpcode sub, byte result) =>
        sub == NationTransferSubOpcode.Submit ? !IsSubmitSuccess((NationTransferResult)result) : IsRefusal((NationTransferResult)result);

    public static int RefusalText(NationTransferSubOpcode sub, byte result) =>
        sub == NationTransferSubOpcode.Submit && SubmitRefusalTexts.TryGetValue(result, out int submitText) ? submitText
        : RefusalTexts.TryGetValue(result, out int text) ? text
        : FailedText;
}

public enum NationTransferSubOpcode : byte
{
    WarStatus = 1,
    OpenBox = 2,
    Submit = 3,
    ErrorBox = 16,
}

public enum NationTransferResult : byte
{
    Failed = 0,
    Accepted = 1,
    InClan = 2,
    IsKing = 3,
    InInvestigationTeam = 4,
    WrongCharacter = 5,
    NoCharacter = 6,
    NoItem = 7,
    WarRunning = 8,
    MakeOverActive = 9,
    NotEnoughCoins = 10,
}

public sealed record NationTransferCandidate(int Slot, string Name, int Race, int Nation, int Class, int Face, int Hair);

public readonly record struct NationTransferPick(int Slot, string Name, int Race, int Face, int Hair);
