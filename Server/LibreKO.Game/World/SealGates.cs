namespace LibreKO.Game.World;

// Sealing a character and sending it mail or a letter take turns.
public sealed class SealGates
{
    private const int Stripes = 64;
    private readonly SemaphoreSlim[] stripes = [.. Enumerable.Range(0, Stripes).Select(_ => new SemaphoreSlim(1, 1))];

    public async Task<IDisposable> EnterAsync(int characterId)
    {
        var stripe = stripes[(characterId & int.MaxValue) % Stripes];
        await stripe.WaitAsync();
        return new Turn(stripe);
    }

    private sealed class Turn(SemaphoreSlim stripe) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                stripe.Release();
        }
    }
}
