using Godot;

namespace LibreKO.Features.Social;

internal sealed class ClanMarksFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly SelfSession self;
    private readonly IClanRequests requests;
    private readonly EntitySystem entities;

    public ClanMarksFeature(IWorldContext context, ClanSession clan, SelfSession self, IClanRequests clanRequests, EntitySystem entities)
    {
        this.context = context;
        this.clan = clan;
        this.self = self;
        requests = clanRequests;
        this.entities = entities;
    }

    private const int SymbolCost = 5_000_000;
    private const int SymbolCostText = 2733;
    private const int SymbolOkText = 7610;
    private const int SymbolFailText = 7611;
    private const int SymbolAskText = 7617;
    private const int SymbolNoCoins = 14;
    private const float CardMarkSize = 40f;

    private readonly Dictionary<int, (int Version, Texture2D? Texture)> clanMarks = new();
    private readonly HashSet<int> markAsked = new();
    internal event Action<int>? MarkChanged;
    private TextureRect? cardMark;
    private Button? symbolBtn;
    private FileDialog? symbolPicker;

    public void Build()
    {
        clan.MyClanChangedEvent += OnMyClanChanged;
        entities.EntitySpawned += OnEntitySpawned;
        clan.ClanMarkEvent += OnMark;
        clan.ClanMarkRegisterEvent += OnMarkRegister;
    }

    public void Dispose()
    {
        clan.MyClanChangedEvent -= OnMyClanChanged;
        entities.EntitySpawned -= OnEntitySpawned;
        clan.ClanMarkEvent -= OnMark;
        clan.ClanMarkRegisterEvent -= OnMarkRegister;
    }

    private void OnMyClanChanged(MyClanInfo info) => RefreshSelfMark();

    private void OnEntitySpawned(EntityView e, EntitySnapshot info) =>
        e.Plate?.SetMark(PlateMark(Mark(info.KnightsId, info.ClanMarkVersion)));

    internal Texture2D? Mark(int clanId, int version)
    {
        if (clanId <= 0 || version <= 0) return null;
        if (clanMarks.TryGetValue(clanId, out var cached) && cached.Version == version) return cached.Texture;
        if (markAsked.Add(clanId)) requests.MarkRequest(clanId);
        return cached.Texture;
    }

    private void OnMark(int clanId, int version, byte[] data)
    {
        markAsked.Remove(clanId);
        var texture = ClanMarkCodec.Decode(data);
        clanMarks[clanId] = (version, texture);
        context.Entities.SetClanMark(clanId, version, PlateMark(texture));
        if (clan.MyClan.InClan && clan.MyClan.ClanId == clanId)
        {
            context.Self.Plate()?.SetMark(PlateMark(texture));
            RefreshCardMark();
        }
        MarkChanged?.Invoke(clanId);
    }

    internal void RefreshSelfMark()
    {
        var mark = clan.MyClan.InClan ? Mark(clan.MyClan.ClanId, clan.MyClan.MarkVersion) : null;
        context.Self.Plate()?.SetMark(PlateMark(mark));
        RefreshCardMark();
    }

    internal Texture2D? PlateMark(Texture2D? mark) => NameMark.HidesClan(context.Zone) ? null : mark;

    private void RefreshCardMark()
    {
        if (cardMark == null || !GodotObject.IsInstanceValid(cardMark)) return;
        var info = clan.MyClan;
        var mark = info.InClan ? Mark(info.ClanId, info.MarkVersion) : null;
        cardMark.Texture = mark;
        cardMark.Visible = mark != null;
        if (symbolBtn != null) symbolBtn.Visible = info.IsChief && info.Flag >= ClanTypes.Promoted;
    }

    internal Control BuildCardMark()
    {
        cardMark = new TextureRect
        {
            CustomMinimumSize = new Vector2(CardMarkSize, CardMarkSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            Visible = false,
        };
        return cardMark;
    }

    internal Button BuildSymbolButton()
    {
        symbolBtn = UiTheme.SmallButton("Symbol...", "Register a 32x32 Knights symbol");
        symbolBtn.Visible = false;
        symbolBtn.Pressed += PickSymbol;
        return symbolBtn;
    }

    private void PickSymbol()
    {
        if (symbolPicker == null || !GodotObject.IsInstanceValid(symbolPicker))
        {
            symbolPicker = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Filters = new[] { "*.png, *.jpg, *.jpeg, *.bmp ; Images" },
                Title = "Knights Symbol",
                UseNativeDialog = true,
            };
            symbolPicker.FileSelected += OnSymbolPicked;
            context.Root.AddChild(symbolPicker);
        }
        symbolPicker.PopupCentered(new Vector2I(720, 480));
    }

    private void OnSymbolPicked(string path)
    {
        var image = Image.LoadFromFile(path);
        if (image == null || image.IsEmpty())
        {
            context.Log.Notice(ItemData.Text(SymbolFailText, "Failed to register the symbol. Please try again."));
            return;
        }
        byte[] mark = ClanMarkCodec.Encode(image);
        if (self.Sheet.Gold < SymbolCost)
        {
            context.Log.Notice(SymbolCostNotice());
            return;
        }
        Notice.Confirm(context.Root,
            $"{ItemData.Text(SymbolAskText, "Will you register your Knights symbol?")}\n{SymbolCost:n0} coins",
            "Register", "Cancel", () => requests.MarkRegister(mark));
    }

    private static string SymbolCostNotice() => TextTemplate.Fill(
        ItemData.Text(SymbolCostText, "Sorry, but you'll need %d Coins in order to register your Knights symbol."),
        SymbolCost);

    private void OnMarkRegister(KnightsResult result, int version)
    {
        if (result == KnightsResult.Succeeded)
        {
            context.Log.Notice(ItemData.Text(SymbolOkText, "Successfully registered the symbol."));
            clanMarks.Remove(clan.MyClan.ClanId);
            RefreshSelfMark();
            return;
        }
        context.Log.Notice(result == (KnightsResult)SymbolNoCoins
            ? SymbolCostNotice()
            : ItemData.Text(SymbolFailText, "Failed to register the symbol. Please try again."));
    }
}
