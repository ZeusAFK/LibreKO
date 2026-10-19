using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ChangeHairFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ChangeHairSession changeHair;
    private readonly SelfSession self;
    private readonly IChangeHairRequests requests;
    private readonly GenderChangeFeature genderWindow;

    public ChangeHairFeature(IWorldContext context, ChangeHairSession changeHair, SelfSession self, IChangeHairRequests requests, GenderChangeFeature genderChangeWindow)
    {
        this.context = context;
        this.changeHair = changeHair;
        this.self = self;
        this.requests = requests;
        genderWindow = genderChangeWindow;
    }

    private const int BeautyPreviewWidth = 214;
    private const int BeautyPreviewHeight = 275;
    private const int FaceCoupon = 810109000;
    private const int MakeOverCoupon = 810340000;
    private const int NoCouponText = 18901;
    private const int LookChangedText = 18902;
    private const int LookFailedText = 18903;
    private const int HairColourText = 200025;
    private const int HairColourPromptText = 18905;
    private const string BeautyConfirmText = "Use a Make Over Coupon to change your look?";
    private const float PaletteGap = 8f;

    private static readonly int[] BeautyCoupons = { FaceCoupon, MakeOverCoupon };

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    internal LookPreview BeautyPreview { get; private set; } = null!;
    internal LookEditor BeautyEditor { get; private set; } = null!;

    private Label status = null!;

    private Button beautyConfirm = null!;

    private bool shown;

    public bool IsOpen => shown;

    internal bool BeautyInFlight { get; private set; }
    private HudWindow? hairPalette;
    private ColorPicker hairPalettePicker = null!;
    private LookEditor? hairPaletteTarget;

    public void Build()
    {
        BuildPanel();
        changeHair.ChangeHairResultEvent += OnResult;
        changeHair.BeautyShopEvent += Open;
    }

    public void Dispose()
    {
        changeHair.ChangeHairResultEvent -= OnResult;
        changeHair.BeautyShopEvent -= Open;
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("changehair", "Beauty Shop") { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var column = Panel.Body;
        column.AddThemeConstantOverride("separation", 8);
        BeautyPreview = new LookPreview(BeautyPreviewWidth, BeautyPreviewHeight);
        column.AddChild(GenderChangeFeature.LookPreviewColumn(BeautyPreview, zoom: true));

        BeautyEditor = new LookEditor(withRace: false, withColour: false);
        BeautyEditor.Changed += ShowBeautyLook;
        column.AddChild(BeautyEditor);

        var colour = GenderChangeFeature.LookActionButton(ItemData.Text(HairColourText, "Change hair color"), () => OpenHairPalette(BeautyEditor));
        column.AddChild(colour);

        status = HudStyle.Label(12);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.CustomMinimumSize = new Vector2(BeautyPreviewWidth, 0);
        column.AddChild(status);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        actions.AddThemeConstantOverride("separation", 8);
        beautyConfirm = GenderChangeFeature.LookActionButton("OK", Submit);
        actions.AddChild(beautyConfirm);
        actions.AddChild(GenderChangeFeature.LookActionButton("Cancel", Close));
        column.AddChild(actions);
    }

    internal void Open()
    {
        if (shown) return;
        var me = self.LastEnter;
        BeautyInFlight = false;
        BeautyEditor.Load(new[] { me.Race }, me.Race, self.Face, self.Hair);
        ShowBeautyLook();
        SetStatus("", false);
        Panel.Visible = true;
        shown = true;
        BeautyPreview.AlwaysRender = Panel.PluginReplaced;
        PluginHost.Game.RaiseBeautyShop();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        BeautyPreview.AlwaysRender = false;
        BeautyPreview.Clear();
        CloseHairPalette();
        PluginHost.Game.RaiseBeautyShop();
    }

    private void ShowBeautyLook()
    {
        BeautyEditor.Refresh();
        BeautyPreview.Show(BeautyEditor.Race, BeautyEditor.Face, BeautyEditor.HairStyle, BeautyEditor.HairColour);
        beautyConfirm.Disabled = BeautyInFlight;
        PluginHost.Game.RaiseBeautyShop();
    }

    private int BeautyCouponPosition()
    {
        if (self.Inventory == null) return -1;
        foreach (int coupon in BeautyCoupons)
            for (int i = 0; i < InventoryConstants.HaveMax; i++)
            {
                int slot = InventoryConstants.InventoryStart + i;
                if (slot < self.Inventory.Length && self.Inventory[slot].ItemId == coupon && self.Inventory[slot].Count > 0) return i;
            }
        return -1;
    }

    internal void Submit()
    {
        if (BeautyInFlight) return;
        int position = BeautyCouponPosition();
        if (position < 0)
        {
            Notice.Show(context.Root, ItemData.Text(NoCouponText, "You do not have a Makeover Coupon."));
            Close();
            return;
        }
        Notice.Confirm(context.Root, BeautyConfirmText, "OK", "Cancel", () =>
        {
            BeautyInFlight = true;
            ShowBeautyLook();
            requests.Change((byte)position, BeautyEditor.Face, BeautyEditor.Hair);
        });
    }

    private void OnResult(bool ok, int face, int hair)
    {
        BeautyInFlight = false;
        if (!ok)
        {
            Notice.Show(context.Root, ItemData.Text(LookFailedText, "Feature modification failed."));
            if (shown) ShowBeautyLook();
            return;
        }
        context.Self.Redress();
        Notice.Show(context.Root, ItemData.Text(LookChangedText, "Feature modification succeeded."));
        Close();
    }

    private void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.Visible = text.Length > 0;
        status.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }

    internal void OpenHairPalette(LookEditor target)
    {
        if (hairPalette == null || !GodotObject.IsInstanceValid(hairPalette)) BuildHairPalette();
        hairPaletteTarget = target;
        hairPalettePicker.Color = target.HairColour;
        hairPalette!.Visible = true;
        var beside = target == genderWindow.Editor ? genderWindow.Panel : Panel;
        hairPalette.Position = beside.Position + new Vector2(beside.Size.X + PaletteGap, 0f);
    }

    private void CloseHairPalette()
    {
        if (hairPalette != null && GodotObject.IsInstanceValid(hairPalette)) hairPalette.Visible = false;
    }

    private void BuildHairPalette()
    {
        hairPalette = new HudWindow("hairpalette", "Hair Colour", persistLayout: false) { Visible = false };
        hairPalette.Closed += CloseHairPalette;
        layer.AddChild(hairPalette);
        var body = hairPalette.Body;
        body.AddThemeConstantOverride("separation", 8);
        var prompt = HudStyle.Label(12);
        prompt.Text = ItemData.Text(HairColourPromptText, "Please select your hair color.");
        body.AddChild(prompt);
        hairPalettePicker = new ColorPicker
        {
            EditAlpha = false,
            PickerShape = ColorPicker.PickerShapeType.HsvRectangle,
            ColorMode = ColorPicker.ColorModeType.Rgb,
            PresetsVisible = false,
            SamplerVisible = false,
            ColorModesVisible = false,
            HexVisible = false,
        };
        body.AddChild(hairPalettePicker);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        actions.AddThemeConstantOverride("separation", 8);
        actions.AddChild(GenderChangeFeature.LookActionButton("Apply", () =>
        {
            (hairPaletteTarget ?? BeautyEditor).SetHairColour(hairPalettePicker.Color);
            CloseHairPalette();
        }));
        actions.AddChild(GenderChangeFeature.LookActionButton("Cancel", CloseHairPalette));
        body.AddChild(actions);
    }
}
