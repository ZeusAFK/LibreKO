using LibreKO.Game.Configuration;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Protocol;

public interface IGlobalAnvilRateService
{
    GlobalAnvilRateSnapshot GetSnapshot(short baseGenRate);
    GlobalAnvilRollOutcome ResolveRoll(short baseGenRate, int roll);
}

public readonly record struct GlobalAnvilRateSnapshot(
    short BaseGenRate,
    short EffectiveGenRate,
    double ModifierPercent);

public readonly record struct GlobalAnvilRollOutcome(
    short BaseGenRate,
    short EffectiveGenRate,
    int Roll,
    bool Succeeded,
    double ModifierBeforePercent,
    double ModifierAfterPercent);

public class GlobalAnvilRateService(IOptions<GameServerSettings> settings) : IGlobalAnvilRateService
{
    private const short MaxUpgradeRate = 10000;

    private readonly Lock _sync = new();
    private readonly bool _enabled = settings.Value.Global.Anvil.Enabled;
    private readonly double _maxModifier = NormalizePercent(settings.Value.Global.Anvil.MaxRateSwingPercent);
    private readonly double _successStep = NormalizePercent(settings.Value.Global.Anvil.SuccessStepPercent);
    private readonly double _failureStep = NormalizePercent(settings.Value.Global.Anvil.FailureStepPercent);

    private double _modifier;

    public GlobalAnvilRateSnapshot GetSnapshot(short baseGenRate)
    {
        lock (_sync)
        {
            return CreateSnapshot(baseGenRate, _modifier);
        }
    }

    public GlobalAnvilRollOutcome ResolveRoll(short baseGenRate, int roll)
    {
        lock (_sync)
        {
            var modifierBefore = _enabled ? _modifier : 0d;
            var effectiveGenRate = CalculateEffectiveGenRate(baseGenRate, modifierBefore);
            var succeeded = effectiveGenRate > roll;

            if (_enabled)
            {
                _modifier = Math.Clamp(
                    _modifier + (succeeded ? -_successStep : _failureStep),
                    -_maxModifier,
                    _maxModifier);
            }

            return new GlobalAnvilRollOutcome(
                baseGenRate,
                effectiveGenRate,
                roll,
                succeeded,
                modifierBefore * 100d,
                (_enabled ? _modifier : 0d) * 100d);
        }
    }

    private GlobalAnvilRateSnapshot CreateSnapshot(short baseGenRate, double modifier)
    {
        var normalizedModifier = _enabled ? modifier : 0d;
        return new GlobalAnvilRateSnapshot(
            baseGenRate,
            CalculateEffectiveGenRate(baseGenRate, normalizedModifier),
            normalizedModifier * 100d);
    }

    private static short CalculateEffectiveGenRate(short baseGenRate, double modifier)
    {
        var adjusted = (int)Math.Round(baseGenRate * (1d + modifier), MidpointRounding.AwayFromZero);
        return (short)Math.Clamp(adjusted, 0, MaxUpgradeRate);
    }

    private static double NormalizePercent(double percent) => percent <= 0 ? 0d : percent / 100d;
}
