using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LibreKO.Domain;

public sealed record AuctionScheduleLot(int Row, int Slot, int Item, int Count, long Start, int Step, bool Secret);

public readonly record struct AuctionOffer(int Slot, int ItemId, long CurrentBid, string TopBidder);

public sealed record AuctionLot(int Slot, int ItemId, int Count, long Current, int Step, string TopBidder)
{
    public long MinimumBid => Current + Step;
}

public readonly record struct AuctionToday(short Status, int Group, int Seconds, int Day, IReadOnlyList<AuctionOffer> Offers);

public readonly record struct AuctionBidRow(int Channel, byte Day, byte Slot, int ItemId, short Count, int Serial, byte Sequence, long Price, byte Status);

public readonly record struct AuctionResultLine(int ItemId, long Price, byte Status);

public readonly record struct AuctionUpcomingDay(int Offset, IReadOnlyList<AuctionScheduleLot> Lots);

public static class SpecialAuction
{
    public const int MythrilCheck = 1399299001;
    public const long CheckValue = 1_000_000_000;
    public const long CoinUnit = 1_000_000;
    public const int MaxMillions = 999;
    public const int DaysPerGroup = 13;
    public const int RowsPerGroup = 14;
    public const int LotsPerDay = 8;

    public const short NothingToBid = 0;
    public const short Bidding = 1;
    public const short CollectOnly = 2;
    public const short Settling = 3;

    public const byte TopBidder = 1;
    public const byte Outbid = 2;
    public const byte Won = 3;
    public const byte Cancelled = 4;

    public const short Success = 1;
    public const short MyInfoSettling = 2;

    public const int NoItemText = 43707;
    public const int CollectOnlyText = 43706;
    public const int SettlingText = 43708;
    public const int NotEnoughBalanceText = 43690;
    public const int WrongChecksText = 43688;
    public const int PriceNotAccurateText = 43689;
    public const int NoLotText = 43692;
    public const int EnterPriceText = 43714;
    public const int DatabaseErrorText = 43687;
    public const int ClaimLaterText = 43712;
    public const int SystemErrorText = 16506;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] LogColours =
    [
        "f0f8ff", "7fffd4", "ff7f50", "f0e68c", "dc143c", "00ffff", "ff1493", "f15f5f", "ffd700", "adff2f",
        "deb887", "ffffff", "ffff00", "b2ebf4",
    ];

    public static AuctionScheduleLot[] Parse(string json) =>
        JsonSerializer.Deserialize<AuctionScheduleLot[]>(json, Options) ?? Array.Empty<AuctionScheduleLot>();

    public static int Row(int group, int day) => RowsPerGroup * (group - 1) + day;

    public static IReadOnlyList<AuctionScheduleLot> LotsOf(IEnumerable<AuctionScheduleLot> table, int group, int day)
    {
        int row = Row(group, day);
        return table.Where(l => l.Row == row).OrderBy(l => l.Slot).ToList();
    }

    public static IReadOnlyList<AuctionLot> Live(IEnumerable<AuctionScheduleLot> table, AuctionToday today)
    {
        var scheduled = LotsOf(table, today.Group, today.Day).ToDictionary(l => l.Slot);
        var lots = new List<AuctionLot>();
        foreach (var offer in today.Offers)
        {
            if (!scheduled.TryGetValue(offer.Slot, out var lot) || lot.Item != offer.ItemId) continue;
            long current = offer.CurrentBid > 0 ? offer.CurrentBid : lot.Start;
            lots.Add(new AuctionLot(lot.Slot, lot.Item, lot.Count, current, lot.Step, offer.TopBidder));
        }
        return lots;
    }

    public static IReadOnlyList<AuctionUpcomingDay> Upcoming(IEnumerable<AuctionScheduleLot> table, int group, int day)
    {
        var days = new List<AuctionUpcomingDay>();
        for (int next = day + 1; next <= DaysPerGroup; next++)
            days.Add(new AuctionUpcomingDay(next - day, LotsOf(table, group, next)));
        return days;
    }

    public static int ClampMillions(long typed, long gold) =>
        (int)Math.Clamp(typed, 0, Math.Min(MaxMillions, Math.Max(0, gold) / CoinUnit));

    public static int ClampChecks(long typed, int checksInBag) => (int)Math.Clamp(typed, 0, Math.Max(0, checksInBag));

    public static long Total(int millions, int checks) => millions * CoinUnit + checks * CheckValue;

    public static int BidRefusal(AuctionLot? lot, int millions, int checks, int checksInBag)
    {
        if (lot == null) return NoLotText;
        long total = Total(millions, checks);
        if (total <= 0) return NotEnoughBalanceText;
        if (total < lot.MinimumBid) return NotEnoughBalanceText;
        if (checks > checksInBag) return WrongChecksText;
        return 0;
    }

    public static int BidResultText(short result) => result switch
    {
        1 => 43686,
        -1 => DatabaseErrorText,
        -2 => WrongChecksText,
        -3 => PriceNotAccurateText,
        -4 => NotEnoughBalanceText,
        -5 => 43691,
        -6 => NoLotText,
        -7 => 43695,
        -8 => 43696,
        -9 => 43703,
        _ => SystemErrorText,
    };

    public static bool Collected(short result) => result is 1 or 2;

    public static int CollectResultText(short result) => result switch
    {
        1 or 2 => 43697,
        0 => 43698,
        -1 => DatabaseErrorText,
        -2 => 43702,
        -3 => 43701,
        -4 => NotEnoughBalanceText,
        -5 => 43691,
        -6 => NoLotText,
        -7 => 43695,
        -8 => 43696,
        -9 => 43716,
        _ => SystemErrorText,
    };

    public static int ClaimResultText(short result) => result switch
    {
        1 => 43699,
        -1 => DatabaseErrorText,
        -2 => 43700,
        -3 => 43698,
        -4 => 10714,
        -5 => 43716,
        -6 => 33621,
        _ => SystemErrorText,
    };

    public static int StateText(byte status) => status switch
    {
        TopBidder => 43682,
        Outbid => 43683,
        Cancelled => 43709,
        _ => 0,
    };

    public static bool IsBid(byte status) => status is TopBidder or Outbid or Cancelled;

    public static bool CanRetract(byte status) => status is Outbid or Cancelled;

    public static string Clock(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    public static string InWords(long total)
    {
        if (total <= 0) return "";
        long billions = total / CheckValue;
        long millions = total % CheckValue / CoinUnit;
        var parts = new List<string>();
        if (billions > 0) parts.Add($"{billions:n0} billion");
        if (millions > 0) parts.Add($"{millions:n0} million");
        return string.Join(" ", parts);
    }

    public static string LogColour(int dayIndex) => dayIndex >= 0 && dayIndex < LogColours.Length ? LogColours[dayIndex] : "ffffff";

    public static List<int> CheckSlots(Inventory inventory)
    {
        var slots = new List<int>();
        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < inventory.Length; abs++)
            if (inventory[abs].ItemId == MythrilCheck) slots.Add(abs);
        return slots;
    }
}
