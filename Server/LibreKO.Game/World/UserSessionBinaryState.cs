using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public static class UserSessionBinaryState
{
    public const int BytesPerItem = 8;
    public const int BytesPerFlag = 1;
    public const int BytesPerExpiry = 8;

    public static void LoadItems(ItemSlot[] inventory, byte[]? data) => LoadSlots(inventory, data);

    public static byte[] SerializeItems(ItemSlot[] inventory) => SerializeSlots(inventory);

    public static void LoadSlots(ItemSlot[] slots, byte[]? data)
    {
        foreach (var slot in slots)
            slot.Clear();
        if (data == null || data.Length == 0)
            return;

        var count = Math.Min(data.Length / BytesPerItem, slots.Length);
        var flagsOffset = slots.Length * BytesPerItem;
        var expiryOffset = flagsOffset + slots.Length * BytesPerFlag;

        for (var index = 0; index < count; index++)
        {
            var offset = index * BytesPerItem;
            slots[index].ItemId = BitConverter.ToInt32(data, offset);
            slots[index].Durability = BitConverter.ToInt16(data, offset + 4);
            slots[index].Count = BitConverter.ToUInt16(data, offset + 6);

            var flagAt = flagsOffset + index;
            if (flagAt < data.Length)
                slots[index].Flag = data[flagAt];

            var expiryAt = expiryOffset + index * BytesPerExpiry;
            if (expiryAt + BytesPerExpiry <= data.Length)
                slots[index].ExpiresAt = BitConverter.ToInt64(data, expiryAt);
        }
    }

    public static byte[] SerializeSlots(ItemSlot[] slots)
    {
        var flagsOffset = slots.Length * BytesPerItem;
        var expiryOffset = flagsOffset + slots.Length * BytesPerFlag;
        var data = new byte[expiryOffset + slots.Length * BytesPerExpiry];

        for (var index = 0; index < slots.Length; index++)
        {
            var offset = index * BytesPerItem;
            var slot = slots[index];
            BitConverter.TryWriteBytes(data.AsSpan(offset), slot.ItemId);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 4), slot.Durability);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 6), slot.Count);
            data[flagsOffset + index] = slot.Flag;
            BitConverter.TryWriteBytes(data.AsSpan(expiryOffset + index * BytesPerExpiry), slot.ExpiresAt);
        }

        return data;
    }

    public static void LoadWarehouse(ItemSlot[] warehouse, byte[]? data) => LoadSlots(warehouse, data);

    public static byte[] SerializeWarehouse(ItemSlot[] warehouse) => SerializeSlots(warehouse);

    public static byte[] SerializeQuestData(Dictionary<short, byte> questMap, Dictionary<short, ushort[]> killCountsMap,
        IReadOnlyDictionary<short, int>? dailyCompletionDays = null)
    {
        if (questMap.Count == 0)
            return [];

        const int bytesPerQuest = 7;
        var daily = dailyCompletionDays?.Where(pair => questMap.GetValueOrDefault(pair.Key) == 2).ToArray() ?? [];
        var data = new byte[2 + questMap.Count * bytesPerQuest + (daily.Length == 0 ? 0 : 6 + daily.Length * 6)];
        BitConverter.TryWriteBytes(data.AsSpan(0), (short)questMap.Count);

        var offset = 2;
        foreach (var (questId, status) in questMap)
        {
            BitConverter.TryWriteBytes(data.AsSpan(offset), questId);
            data[offset + 2] = status;

            if (killCountsMap.TryGetValue(questId, out var kills) && kills.Length >= 4)
            {
                data[offset + 3] = (byte)Math.Min((int)kills[0], 255);
                data[offset + 4] = (byte)Math.Min((int)kills[1], 255);
                data[offset + 5] = (byte)Math.Min((int)kills[2], 255);
                data[offset + 6] = (byte)Math.Min((int)kills[3], 255);
            }

            offset += bytesPerQuest;
        }

        if (daily.Length > 0)
        {
            BitConverter.TryWriteBytes(data.AsSpan(offset), DailyQuestMarker);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 4), (ushort)daily.Length);
            offset += 6;
            foreach (var (questId, day) in daily)
            {
                BitConverter.TryWriteBytes(data.AsSpan(offset), questId);
                BitConverter.TryWriteBytes(data.AsSpan(offset + 2), day);
                offset += 6;
            }
        }
        return data;
    }

    private const int DailyQuestMarker = 0x31594C44;

    public static void LoadQuestData(Dictionary<short, byte> questMap, Dictionary<short, ushort[]> killCountsMap, byte[]? data,
        Dictionary<short, int>? dailyCompletionDays = null)
    {
        questMap.Clear();
        killCountsMap.Clear();
        dailyCompletionDays?.Clear();
        if (data == null || data.Length < 2)
            return;

        var count = BitConverter.ToInt16(data, 0);
        if (count <= 0)
            return;

        // Detect format: 7-byte vs 3-byte based on data size
        var remainingBytes = data.Length - 2;
        var bytesPerQuest = remainingBytes / count >= 7 ? 7 : 3;
        var offset = 2;

        for (var index = 0; index < count && offset + 2 < data.Length; index++)
        {
            var questId = BitConverter.ToInt16(data, offset);
            var status = data[offset + 2];
            questMap[questId] = status;

            if (bytesPerQuest >= 7 && offset + 6 < data.Length)
            {
                var kills = new ushort[4];
                kills[0] = data[offset + 3];
                kills[1] = data[offset + 4];
                kills[2] = data[offset + 5];
                kills[3] = data[offset + 6];
                if (kills[0] > 0 || kills[1] > 0 || kills[2] > 0 || kills[3] > 0)
                    killCountsMap[questId] = kills;
            }

            offset += bytesPerQuest;
        }
        if (dailyCompletionDays is null || bytesPerQuest != 7 || offset + 6 > data.Length
            || BitConverter.ToInt32(data, offset) != DailyQuestMarker)
            return;
        var dailyCount = BitConverter.ToUInt16(data, offset + 4);
        offset += 6;
        if (dailyCount > count || offset + dailyCount * 6 != data.Length)
            return;
        for (var index = 0; index < dailyCount; index++, offset += 6)
        {
            var questId = BitConverter.ToInt16(data, offset);
            var day = BitConverter.ToInt32(data, offset + 2);
            if (questMap.GetValueOrDefault(questId) == 2 && day >= 0 && day <= DateOnly.MaxValue.DayNumber)
                dailyCompletionDays[questId] = day;
        }
    }
}
