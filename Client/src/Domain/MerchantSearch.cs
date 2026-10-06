using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public readonly record struct MerchantSearchRow(int SellerId, string Seller, int ItemId, int Price, int Type, string ItemName);

public enum MerchantSearchSort
{
    Arrival,
    Name,
    PriceLowToHigh,
    PriceHighToLow,
}

public sealed class MerchantSearch
{
    public const int OfficialListItem = 810166000;
    public const int OfficialListSkill = 500126;
    public const int SellingType = 0;
    public const int BuyingType = 1;
    public const int RowsPerPage = 10;
    public const int PagesPerGroup = 8;
    public const int SlotsPerStall = 12;

    private readonly List<MerchantSearchRow> _rows = new();
    private List<MerchantSearchRow> _view = new();

    public int Type { get; private set; } = SellingType;
    public string Filter { get; private set; } = "";
    public MerchantSearchSort Sort { get; private set; } = MerchantSearchSort.Arrival;
    public int Page { get; private set; }

    public int Count => _view.Count;
    public int PageCount => Math.Max(1, (Count + RowsPerPage - 1) / RowsPerPage);
    public int Group => Page / PagesPerGroup;

    public static bool OpensWith(int skillId) => skillId == OfficialListSkill;

    public void Clear()
    {
        _rows.Clear();
        Rebuild(keepPage: false);
    }

    public void Add(IEnumerable<MerchantSearchRow> rows)
    {
        _rows.AddRange(rows.Where(row => row.ItemId != 0));
        Rebuild(keepPage: true);
    }

    public void SetType(int type)
    {
        Type = type == BuyingType ? BuyingType : SellingType;
        Rebuild(keepPage: false);
    }

    public void Search(string text)
    {
        Filter = (text ?? "").Trim();
        Rebuild(keepPage: false);
    }

    public void ShowAll() => Search("");

    public void SortBy(MerchantSearchSort sort)
    {
        Sort = sort;
        Rebuild(keepPage: false);
    }

    public void GoTo(int page) => Page = Math.Clamp(page, 0, PageCount - 1);

    public void Next() => GoTo(Page + 1);

    public void Previous() => GoTo(Page - 1);

    public IReadOnlyList<MerchantSearchRow> Visible() =>
        _view.Skip(Page * RowsPerPage).Take(RowsPerPage).ToList();

    public IEnumerable<int> GroupPages()
    {
        int first = Group * PagesPerGroup;
        for (int page = first; page < Math.Min(first + PagesPerGroup, PageCount); page++)
            yield return page;
    }

    private void Rebuild(bool keepPage)
    {
        IEnumerable<MerchantSearchRow> view = _rows.Where(row => row.Type == Type);
        if (Filter.Length > 0)
            view = view.Where(row => row.ItemName.Contains(Filter, StringComparison.OrdinalIgnoreCase));
        view = Sort switch
        {
            MerchantSearchSort.Name => view.OrderBy(row => row.ItemName, StringComparer.Ordinal),
            MerchantSearchSort.PriceLowToHigh => view.OrderBy(row => row.Price),
            MerchantSearchSort.PriceHighToLow => view.OrderByDescending(row => row.Price),
            _ => view,
        };
        _view = view.ToList();
        Page = keepPage ? Math.Clamp(Page, 0, PageCount - 1) : 0;
    }
}
