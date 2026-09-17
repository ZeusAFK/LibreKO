using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IChatPacketCoordinator
{
    Task HandleAsync(UserSession session, byte chatType, string message);
}

public class ChatPacketCoordinator(
    SessionManager sessionManager,
    IUserNotificationService userNotificationService,
    ICombatNotificationService combatNotificationService,
    ILogger<ChatPacketCoordinator> logger) : IChatPacketCoordinator
{
    private const int MessageMaxLength = 128;
    private const int FloodIntervalMs = 300;
    private const byte CommandCaptainFame = 100;
    private const byte ClanOfficerFame = 3;
    private const int ShoutMinLevel = 35;
    private const int ShoutFee = 3000;

    public async Task HandleAsync(UserSession session, byte chatType, string message)
    {
        if (string.IsNullOrEmpty(message) || message.Length > MessageMaxLength)
            return;

        if (session.IsMuted && !session.IsGM)
            return;

        if (session.ZoneId == (byte)ZoneId.Prison && !session.IsGM)
            return;

        var now = Environment.TickCount64;
        if (now - session.LastChatTicks < FloodIntervalMs)
            return;

        session.LastChatTicks = now;

        var type = (ChatType)chatType;
        var outType = type == ChatType.General && session.IsGM ? ChatType.GameMaster : type;

        var result = ChatPacketWriter
            .Say((byte)outType, (byte)session.Nation, session.CharacterId, session.Name, message, session.IsGM);

        switch (type)
        {
            case ChatType.General:
                await sessionManager.Regions.SendToRegion(session, result, excludeSender: false);
                break;

            case ChatType.Private:
            {
                var target = sessionManager.GetByCharacterId(session.PrivateChatUser);
                if (target != null)
                    await target.Client.SendPacket(result);
                break;
            }

            case ChatType.Party:
                if (session.IsInParty)
                {
                    var party = sessionManager.Parties.GetParty(session.PartyIndex);
                    if (party != null)
                        await combatNotificationService.SendToPartyAsync(party, result);
                }
                break;

            case ChatType.Shout:
                if (session.Mp < session.MaxMp / 5)
                    break;

                if (!session.IsGM && session.Level < ShoutMinLevel)
                {
                    if (session.Money < ShoutFee)
                        break;

                    session.Money -= ShoutFee;
                    await userNotificationService.SendGoldLossAsync(session, ShoutFee);
                }

                session.Mp -= (short)(session.MaxMp / 5);
                await combatNotificationService.SendMspChangeAsync(session);
                await sessionManager.Regions.SendToRegion(session, result, excludeSender: false);
                break;

            case ChatType.Clan:
                if (session.KnightsId > 0)
                {
                    foreach (var member in sessionManager.GetAll())
                    {
                        if (member.KnightsId == session.KnightsId)
                        {
                            try { await member.Client.SendPacket(result); } catch (Exception ex) { logger.LogDebug(ex, "Chat broadcast failed for {Id}", member.CharacterId); }
                        }
                    }
                }
                break;

            case ChatType.Public:
            case ChatType.WarSystem:
                if (session.IsGM)
                    await sessionManager.BroadcastToAll(result);
                break;

            case ChatType.Command:
                if (session.KnightsFame == CommandCaptainFame)
                {
                    foreach (var player in sessionManager.GetAll())
                    {
                        if (player.Nation == session.Nation)
                        {
                            try { await player.Client.SendPacket(result); } catch (Exception ex) { logger.LogDebug(ex, "Chat broadcast failed for {Id}", player.CharacterId); }
                        }
                    }
                }
                break;

            case ChatType.Merchant:
                if (session.Trade.IsMerchanting)
                    await sessionManager.Regions.SendToRegion(session, result, excludeSender: false);
                break;

            case ChatType.Alliance:
                if (session.KnightsId > 0)
                {
                    var allianceClanIds = sessionManager.Knights.GetAllianceClanIds(session.KnightsId).ToHashSet();
                    if (allianceClanIds.Count == 0)
                        allianceClanIds.Add((short)session.KnightsId);

                    foreach (var member in sessionManager.GetAll())
                    {
                        if (allianceClanIds.Contains((short)member.KnightsId))
                        {
                            try { await member.Client.SendPacket(result); } catch (Exception ex) { logger.LogDebug(ex, "Chat broadcast failed for {Id}", member.CharacterId); }
                        }
                    }
                }
                break;

            case ChatType.SeekingParty:
                foreach (var player in sessionManager.GetAll())
                {
                    if (player.ZoneId == session.ZoneId && player.Nation == session.Nation)
                    {
                        try { await player.Client.SendPacket(result); } catch (Exception ex) { logger.LogDebug(ex, "Chat broadcast failed for {Id}", player.CharacterId); }
                    }
                }
                break;

            case ChatType.ClanOfficer:
                if (session.KnightsId > 0 && session.KnightsFame <= ClanOfficerFame)
                {
                    foreach (var member in sessionManager.GetAll())
                    {
                        if (member.KnightsId != session.KnightsId || member.KnightsFame > ClanOfficerFame)
                            continue;

                        try { await member.Client.SendPacket(result); } catch (Exception ex) { logger.LogDebug(ex, "Chat broadcast failed for {Id}", member.CharacterId); }
                    }
                }
                break;
        }
    }

}
