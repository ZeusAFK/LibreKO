using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int MerchantPriceMax = 2_000_000_000;

    private void BuildAmountPrompt()
    {
        _amountLayer = new CanvasLayer { Layer = 77, Visible = false };
        AddChild(_amountLayer);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        _amountLayer.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        _amountLayer.AddChild(centre);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        centre.AddChild(box);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 14, 12, 14, 12);
        box.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        root.AddChild(head);
        _amountIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(38, 38),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        head.AddChild(_amountIcon);
        var headText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        headText.AddThemeConstantOverride("separation", -2);
        head.AddChild(headText);
        _amountName = UiTheme.Text("", 14, UiTheme.TextHi);
        headText.AddChild(_amountName);
        _amountHint = UiTheme.Text("", 11, UiTheme.Gold);
        headText.AddChild(_amountHint);

        _amountCountRow = new HBoxContainer();
        _amountCountRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(_amountCountRow);
        var countLabel = UiTheme.Text("Quantity", 13, UiTheme.TextLo);
        countLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _amountCountRow.AddChild(countLabel);
        _amountCount = UiTheme.NumberBox(1, 9_999, 1, 120);
        _amountCount.ValueChanged += _ => RefreshAmountTotal();
        _amountCountRow.AddChild(_amountCount);

        _amountPriceRow = new HBoxContainer();
        _amountPriceRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(_amountPriceRow);
        _amountPriceLabel = UiTheme.Text("Price each", 13, UiTheme.TextLo);
        _amountPriceLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _amountPriceRow.AddChild(_amountPriceLabel);
        _amountPrice = new MoneyEdit(MerchantPriceMax, 160);
        _amountPrice.ValueChanged += _ => RefreshAmountTotal();
        _amountPriceRow.AddChild(_amountPrice);
        _amountPriceFixed = UiTheme.Text("", 13, UiTheme.GoldBright);
        _amountPriceFixed.HorizontalAlignment = HorizontalAlignment.Right;
        _amountPriceFixed.CustomMinimumSize = new Vector2(160, 0);
        _amountPriceRow.AddChild(_amountPriceFixed);

        _amountTotalRow = UiTheme.Section();
        root.AddChild(_amountTotalRow);
        _amountTotalRow.AddChild(MoneyRow("Total", out _amountTotal, UiTheme.GoldBright));

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        footer.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(footer);
        _amountConfirmBtn = new Button { Text = "Yes", FocusMode = Control.FocusModeEnum.None };
        _amountConfirmBtn.CustomMinimumSize = new Vector2(104, 28);
        _amountConfirmBtn.Pressed += AcceptAmount;
        footer.AddChild(_amountConfirmBtn);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.CustomMinimumSize = new Vector2(104, 28);
        cancel.Pressed += CloseAmountPrompt;
        footer.AddChild(cancel);
    }

    private void AskStallPrice(ItemSlot slot, int absSlot, int stallSlot)
    {
        int suggested = System.Math.Max(1, ItemData.SellPrice(slot.ItemId));
        AskAmount(slot, "Price this item", suggested, slot.Count, slot.Count > 1,
            (count, price) => Net.I.SendMerchantAddItem(
                slot.ItemId, count, price, (byte)(absSlot - GridStart), (byte)stallSlot),
            defaultCount: slot.Count);
    }

    private void AskTrade(
        ItemSlot slot, string hint, int price, int maxCount, bool countable,
        System.Action<int, int> accept)
    {
        AskAmount(slot, hint, price, maxCount, countable, accept, priceEditable: false, defaultCount: 1);
    }

    private void AskAmount(
        ItemSlot slot, string hint, int price, int maxCount, bool countable,
        System.Action<int, int> accept, bool priceEditable = true, int defaultCount = 0)
    {
        _amountAccept = accept;
        _amountIcon.Texture = ItemData.Icon(slot.ItemId);
        _amountName.Text = ItemData.DisplayName(slot.ItemId);
        _amountHint.Text = hint;

        bool pickQuantity = countable && maxCount > 1;
        _amountCountRow.Visible = pickQuantity;
        _amountTotalRow.Visible = pickQuantity;
        _amountPriceLabel.Text = pickQuantity ? "Price each" : "Price";
        _amountCount.MaxValue = System.Math.Max(1, maxCount);
        _amountCount.Value = pickQuantity
            ? System.Math.Clamp(defaultCount > 0 ? defaultCount : maxCount, 1, maxCount)
            : 1;

        _amountPriceRow.Visible = price > 0;
        _amountPrice.Value = price;
        _amountPrice.Visible = priceEditable;
        _amountPriceFixed.Visible = !priceEditable;
        _amountPriceFixed.Text = Money(price);

        RefreshAmountTotal();
        _amountConfirmBtn.Text = priceEditable ? "Confirm" : "Yes";
        _amountLayer.Visible = true;
    }

    private void RefreshAmountTotal()
    {
        long count = (long)_amountCount.Value;
        long price = _amountPriceRow.Visible ? _amountPrice.Value : 0;
        _amountTotal.Text = Money(count * price);
    }

    private void AcceptAmount()
    {
        var accept = _amountAccept;
        int count = (int)_amountCount.Value;
        int price = (int)System.Math.Clamp(_amountPrice.Value, 1, MerchantPriceMax);
        CloseAmountPrompt();
        accept?.Invoke(count, price);
    }

    private void CloseAmountPrompt()
    {
        _amountAccept = null;
        _amountLayer.Visible = false;
    }
}
