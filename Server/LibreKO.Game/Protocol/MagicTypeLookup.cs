using LibreKO.Common.Domain.Entities.GameData;
using System.Diagnostics.CodeAnalysis;

namespace LibreKO.Game.Protocol;

internal static class MagicTypeLookup
{
    public static bool TryResolve<TType>(
        IReadOnlyDictionary<int, TType> table,
        MagicData magic,
        int skillId,
        [NotNullWhen(true)]
        out TType? data)
        where TType : class
    {
        if (table.TryGetValue(skillId, out data))
            return true;

        if (magic.Etc > 0 && table.TryGetValue(magic.Etc, out data))
            return true;

        data = null;
        return false;
    }
}
