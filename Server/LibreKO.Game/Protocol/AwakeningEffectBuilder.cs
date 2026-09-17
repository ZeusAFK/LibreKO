using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public static class AwakeningEffectBuilder
{
    public const byte EffectTypeVisual = 1;

    public static Packet BuildVisualEffect(float effectScale, int effectId)
        => BuildEffect(effectScale, EffectTypeVisual, effectId);

    public static Packet BuildEffect(float effectScale, byte effectType, int effectId)
    {
        var pkt = AwakenPacketWriter.Effect(effectScale, effectType, effectId);
        return pkt;
    }
}
