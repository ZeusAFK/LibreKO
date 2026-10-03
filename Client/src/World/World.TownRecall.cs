using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int TextTownRecallLowHp = 1903;

    private CanvasLayer _townRecallLayer = null!;
    private ConfirmationDialog _townRecallDialog = null!;

    private void TownRecallInit()
    {
        _townRecallLayer = new CanvasLayer { Layer = 76 };
        AddChild(_townRecallLayer);

        _townRecallDialog = new ConfirmationDialog { Title = Localization.Loc.Tr("Town Recall") };
        _townRecallDialog.DialogText = Localization.Loc.Tr("Recall to town?\n(You must be above 50% HP.)");
        _townRecallDialog.GetOkButton().Text = Localization.Loc.Tr("Recall");
        _townRecallDialog.GetCancelButton().Text = Localization.Loc.Tr("Cancel");
        _townRecallDialog.Confirmed += TownRecallPress;
        _townRecallLayer.AddChild(_townRecallDialog);
    }

    public void TownRecallTryOpen()
    {
        if (!TownRecallReady()) return;
        _townRecallDialog.PopupCentered();
    }

    private void TownRecallPress()
    {
        if (!TownRecallReady()) return;
        TownRecallConfirm();
    }

    private bool TownRecallReady()
    {
        if (!_worldReady || _selfDead) return false;
        if (!Vitals.BelowHalfHp) return true;
        CombatNotice(ItemData.Text(TextTownRecallLowHp,
            "You cannot teleport back to town when you have half the HP or less"));
        return false;
    }

    private void TownRecallConfirm()
    {
        Net.I.SendTownRecall();
        CombatNotice(Localization.Loc.Tr("Recalling to town…"));
        Audio.Play(Sfx.WarpZone, _self.GlobalPosition);
    }
}
