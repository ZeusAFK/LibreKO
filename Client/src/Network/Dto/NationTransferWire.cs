using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibreKO.Domain;

namespace LibreKO.Network;

public static class NationTransferWire
{
    public const byte Failed = 0;
    public const byte InClan = 2;
    public const byte IsKing = 3;
    public const byte WrongCharacter = 5;
    public const byte NoCharacter = 6;
    public const byte NoItem = 7;
    public const byte SubmitCompleted = 2;
    public const int FailedText = 16700;

    private static readonly Dictionary<byte, int> RefusalTexts = new()
    {
        [InClan] = 16702,
        [IsKing] = 16703,
        [WrongCharacter] = 16705,
        [NoCharacter] = 16706,
        [NoItem] = 16710,
    };

    private static readonly Dictionary<byte, int> SubmitRefusalTexts = new()
    {
        [4] = 16704,
        [9] = 18906,
        [10] = 11303,
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

    public static bool IsRefusal(byte result) =>
        result is Failed or InClan or IsKing or WrongCharacter or NoCharacter or NoItem;

    public static bool IsSubmitSuccess(byte result) =>
        result is Net.NationTransferAccepted or SubmitCompleted;

    public static bool IsRefusal(byte sub, byte result) =>
        sub == Net.NationTransferSubmit ? !IsSubmitSuccess(result) : IsRefusal(result);

    public static int RefusalText(byte sub, byte result) =>
        sub == Net.NationTransferSubmit && SubmitRefusalTexts.TryGetValue(result, out int submitText) ? submitText
        : RefusalTexts.TryGetValue(result, out int text) ? text
        : FailedText;
}
