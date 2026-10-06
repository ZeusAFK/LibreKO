using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const string SiegePreviewClan = "Valhalla";
    private const uint SiegePreviewFee = 1_000_000;
    private const uint SiegePreviewCollectable = 4_250_000;
    private const byte SiegePreviewTuesday = 3;
    private const byte SiegePreviewSaturday = 7;

    internal Control BuildSiegeUiPreview(string view)
    {
        ItemData.EnsureLoaded();
        Net.I.SeedPreviewClan(new MyClanInfo { InClan = true, ClanId = 1, Name = SiegePreviewClan });
        BuildSiegeWindows();

        switch (view)
        {
            case "siege-schedule":
                OnSiegeSchedule(new SiegeSchedule(SiegeWarfare.Success,
                [
                    new SiegeScheduleRow(SiegeWarfare.CastleWarType, SiegePreviewSaturday, 20, 0),
                    new SiegeScheduleRow(1, SiegePreviewTuesday, 21, 30),
                    new SiegeScheduleRow(1, 5, 21, 30),
                ]));
                return DetachPreviewControl(_siegeSchedulePanel);
            case "siege-challengers":
            case "siege-challengers-applied":
                OnSiegeChallengers(new SiegeChallengers(SiegeWarfare.Success,
                [
                    new SiegeClanRow("Olympus", Nations.Karus, 48),
                    new SiegeClanRow(view == "siege-challengers-applied" ? SiegePreviewClan : "Avalon", Nations.ElMorad, 36),
                    new SiegeClanRow("Midgard", Nations.ElMorad, 29),
                ], 3, 6, SiegePreviewFee, SiegePreviewTuesday, 9, 0, SiegePreviewTuesday));
                return DetachPreviewControl(_siegeChallengersPanel);
            case "siege-defenders":
                OnSiegeDefenders(new SiegeDefenders(SiegeWarfare.Success,
                [
                    new SiegeClanRow("Olympus", Nations.Karus, 48),
                    new SiegeClanRow("Asgard", Nations.Karus, 41),
                ]));
                return DetachPreviewControl(_siegeDefendersPanel);
            case "siege-office":
                OpenSiegeOffice(new SiegeOffice(SiegePreviewCollectable, 0));
                return DetachPreviewControl(_siegeOfficePanel);
            case "siege-taxlist":
                OnSiegeTaxRates(new SiegeTaxRates(SiegeWarfare.Success, 3, 4, SiegeWarfare.FeeLimit));
                return DetachPreviewControl(_siegeTaxListPanel);
            case "siege-taxrate":
            case "siege-fee":
                OnSiegeTaxRates(new SiegeTaxRates(SiegeWarfare.Success, 3, 4, SiegeWarfare.FeeLimit));
                OpenSiegeTaxRate(view == "siege-fee" ? SiegeRateKind.DungeonFee : SiegeRateKind.Moradon);
                return DetachPreviewControl(_siegeTaxRatePanel);
            default:
                OpenSiegeGuard();
                return DetachPreviewControl(_siegeGuardPanel);
        }
    }
}
