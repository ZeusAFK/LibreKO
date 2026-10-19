namespace LibreKO.Features.Windows;

internal sealed class RentalFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly RentalSession rental;

    public RentalFeature(IWorldContext context, RentalSession rental)
    {
        this.context = context;
        this.rental = rental;
    }

    private const int UnavailableText = 10747;

    public void Build() => rental.RentalUnavailableEvent += OnUnavailable;

    public void Dispose() => rental.RentalUnavailableEvent -= OnUnavailable;

    private void OnUnavailable() =>
        Notice.Show(context.Root, ItemData.Text(UnavailableText, "Currently unavailable."), "Rental");
}
