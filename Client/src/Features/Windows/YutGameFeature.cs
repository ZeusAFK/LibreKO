using Godot;

namespace LibreKO.Features.Windows;

internal sealed class YutGameFeature : IFeature
{
    private static readonly Color BetColour = new("ffffff");
    private static readonly Color WonColour = new("00ffff");
    private static readonly Color TiedColour = new("dddddd");
    private static readonly Color LostColour = new("00ffff");
    private static readonly Color RefusedColour = new("ff0000");

    private readonly IWorldContext context;
    private readonly MiningSession mining;
    private readonly SelfSession self;
    private readonly IMiningRequests miningRequests;
    private bool betting;

    public YutGameFeature(IWorldContext context, MiningSession mining, SelfSession self, IMiningRequests miningRequests)
    {
        this.context = context;
        this.mining = mining;
        this.self = self;
        this.miningRequests = miningRequests;
    }

    public void Build() => mining.YutRollEvent += OnRoll;

    public void Dispose() => mining.YutRollEvent -= OnRoll;

    internal void Bet()
    {
        if (betting) return;
        betting = true;
        miningRequests.YutBet();
        context.Log.Notice(GameText.System(YutGame.BetPlacedText, YutGame.BetPlacedFallback), BetColour);
    }

    private void OnRoll(YutRoll roll)
    {
        bool mine = roll.CharId == context.Self.CharId;
        if (YutGame.Refused(roll.Result))
        {
            if (!mine) return;
            context.Log.Notice(GameText.System(YutGame.NoCoinsText, YutGame.NoCoinsFallback), RefusedColour);
            betting = false;
            return;
        }
        Spawn(roll.CharId, YutGame.PlayerDieFx(roll.PlayerRoll));
        Spawn(roll.CharId, YutGame.NpcDieFx(roll.NpcRoll));
        Spawn(roll.CharId, YutGame.BoardFx);
        if (!mine)
        {
            Spawn(roll.CharId, YutGame.ResultFx(roll.Result));
            return;
        }
        context.Root.After(YutGame.ResultDelaySeconds, () => ShowResult(roll));
    }

    private void ShowResult(YutRoll roll)
    {
        if (!context.Alive) return;
        Spawn(roll.CharId, YutGame.ResultFx(roll.Result));
        if (YutGame.ResultText(roll.Result) is { } text)
            context.Log.Notice(GameText.System(text.Id, text.Fallback, self.LastEnter.Name), Colour(roll.Result));
        context.Root.After(YutGame.CooldownSeconds, () => betting = false);
    }

    private void Spawn(int charId, int? fxId)
    {
        if (fxId is { } id && Fx.NameForId(id) is { } name) context.Entities.SpawnFxOn(charId, name, 0f);
    }

    private static Color Colour(YutResult result) => result switch
    {
        YutResult.Won => WonColour,
        YutResult.Tied => TiedColour,
        _ => LostColour,
    };
}
