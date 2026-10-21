using System;

namespace LibreKO.Domain;

public static class ChatSender
{
    public const int PathoistText = 43161;
    public const int ElMoradDeityText = 1452;
    public const int KarusDeityText = 1453;
    private const char LineBreak = '\n';
    private const char FlattenedBreak = '_';

    private static readonly int[] HeroZones = { 31, 46 };

    public static int MaskText(int zone, bool selfIsGm, int selfNation, int selfId, ChatType type, int senderNation, int senderId)
    {
        if (senderId < 0) return 0;
        if (ZoneLooks.IsPathosZone(zone)) return PathoistText;
        if (selfIsGm || type == ChatType.GameMaster) return 0;
        if (Array.IndexOf(HeroZones, zone) >= 0)
            return senderNation == selfNation ? 0 : senderNation switch
            {
                Nations.Karus => PlateRules.KarusHeroText,
                Nations.ElMorad => PlateRules.ElMoradHeroText,
                _ => 0,
            };
        if (ZoneLooks.IsDeityZone(zone))
            return (selfNation == Nations.ElMorad) == (senderId == selfId) ? ElMoradDeityText : KarusDeityText;
        return 0;
    }

    public static string Flatten(ChatType type, string message) =>
        type == ChatType.Private ? message : message.Replace(LineBreak, FlattenedBreak);
}
