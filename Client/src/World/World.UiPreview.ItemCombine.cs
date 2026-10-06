using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int CombinePreviewStone = 389227000;
    private const int CombinePreviewDredium = 389235000;
    private const int CombinePreviewFeather = 389239000;
    private const int CombinePreviewCrystal = 389242000;
    private const int CombinePreviewCategory = 1;
    private const int CombinePreviewRecipe = 1;

    internal Control BuildItemCombineUiPreview()
    {
        ItemData.EnsureLoaded();
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart] = PreviewItem(CombinePreviewStone, 12, 1);
        Inv[GridStart + 1] = PreviewItem(CombinePreviewDredium, 50, 1);
        Inv[GridStart + 2] = PreviewItem(CombinePreviewFeather, 80, 1);
        Inv[GridStart + 3] = PreviewItem(CombinePreviewCrystal, 10, 1);
        Inv[GridStart + 4] = PreviewItem(ItemCombine.ShadowPiece, 1, 1);
        BuildItemCombinePanel();
        BuildCombineRecipeBook();
        _itemCombineShown = true;
        _itemCombinePanel.Visible = true;
        StageCombine(0, GridStart, 10);
        StageCombine(1, GridStart + 1, 50);
        StageCombine(2, GridStart + 2, 80);
        StageCombine(3, GridStart + 3, 10);
        StageCombine(ItemCombineShadowSocket, GridStart + 4, 1);
        return DetachPreviewControl(_itemCombinePanel);
    }

    internal Control BuildCombineRecipeBookUiPreview(bool detail)
    {
        ItemData.EnsureLoaded();
        BuildItemCombinePanel();
        BuildCombineRecipeBook();
        OpenCombineRecipeBook();
        if (detail)
        {
            SelectCombineCategory(CombinePreviewCategory);
            SelectCombineRecipe(CombinePreviewRecipe);
        }
        return DetachPreviewControl(_combineBookPanel);
    }
}
