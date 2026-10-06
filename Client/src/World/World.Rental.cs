using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int RentalUnavailableText = 10747;

    private void RentalInit() => Net.I.RentalUnavailableEvent += OnRentalUnavailable;

    private void RentalDispose() => Net.I.RentalUnavailableEvent -= OnRentalUnavailable;

    private void OnRentalUnavailable() =>
        Notice.Show(this, ItemData.Text(RentalUnavailableText, "Currently unavailable."), "Rental");
}
