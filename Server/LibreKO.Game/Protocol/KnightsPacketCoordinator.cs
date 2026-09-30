using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IKnightsPacketCoordinator
{
    Task HandleProcessAsync(IClient client, Packet packet);
}

public class KnightsPacketCoordinator(
    SessionManager sessionManager,
    IKnightsMembershipPacketService knightsMembershipPacketService,
    IKnightsManagementPacketService knightsManagementPacketService,
    ILogger<KnightsPacketCoordinator> logger) : IKnightsPacketCoordinator
{
    public async Task HandleProcessAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var subOpcode = (KnightsSubOpcode)packet.ReadByte();
        switch (subOpcode)
        {
            case KnightsSubOpcode.Create:
                await knightsMembershipPacketService.HandleCreateAsync(session, packet);
                break;

            case KnightsSubOpcode.Join:
                await knightsMembershipPacketService.HandleJoinAsync(session, packet);
                break;

            case KnightsSubOpcode.Invite:
                await knightsMembershipPacketService.HandleInviteAnswerAsync(session, packet);
                break;

            case KnightsSubOpcode.Withdraw:
                await knightsMembershipPacketService.HandleWithdrawAsync(session);
                break;

            case KnightsSubOpcode.Remove:
                await knightsMembershipPacketService.HandleRemoveAsync(session, packet);
                break;

            case KnightsSubOpcode.Destroy:
                await knightsMembershipPacketService.HandleDestroyAsync(session);
                break;

            case KnightsSubOpcode.Admit:
                await knightsMembershipPacketService.HandleAdmitAsync(session, packet);
                break;

            case KnightsSubOpcode.Reject:
                await knightsMembershipPacketService.HandleRejectAsync(session, packet);
                break;

            case KnightsSubOpcode.Chief:
            case KnightsSubOpcode.Vicechief:
            case KnightsSubOpcode.Officer:
                await knightsManagementPacketService.HandlePromoteAsync(session, packet, subOpcode);
                break;

            case KnightsSubOpcode.AllListRequest:
                await knightsManagementPacketService.HandleAllListRequestAsync(session, packet);
                break;

            case KnightsSubOpcode.MemberRequest:
                await knightsManagementPacketService.HandleMemberRequestAsync(session);
                break;

            case KnightsSubOpcode.PointRequest:
                await knightsManagementPacketService.HandlePointRequestAsync(session);
                break;

            case KnightsSubOpcode.PointMethod:
                await knightsManagementPacketService.HandlePointMethodAsync(session, packet);
                break;

            case KnightsSubOpcode.DonatePoints:
                await knightsManagementPacketService.HandleDonateAsync(session, packet);
                break;

            case KnightsSubOpcode.DonationList:
                await knightsManagementPacketService.HandleDonationListAsync(session);
                break;

            case KnightsSubOpcode.LeaderPoints:
                await knightsManagementPacketService.HandleLeaderPointsAsync(session);
                break;

            case KnightsSubOpcode.Top10:
                await SendTop10Async(session);
                break;

            case KnightsSubOpcode.UpdateNotice:
                await knightsManagementPacketService.HandleUpdateNoticeAsync(session, packet);
                break;

            case KnightsSubOpcode.UpdateMemo:
                await knightsManagementPacketService.HandleUpdateMemoAsync(session, packet);
                break;

            case KnightsSubOpcode.HandoverList:
                await knightsManagementPacketService.HandleHandoverListAsync(session);
                break;

            case KnightsSubOpcode.HandoverReq:
                await knightsManagementPacketService.HandleHandoverRequestAsync(session, packet);
                break;

            case KnightsSubOpcode.MarkVersionReq:
                await knightsManagementPacketService.HandleMarkVersionReqAsync(session);
                break;

            case KnightsSubOpcode.MarkRegister:
                await knightsManagementPacketService.HandleMarkRegisterAsync(session, packet);
                break;

            case KnightsSubOpcode.MarkReq:
                await knightsManagementPacketService.HandleMarkReqAsync(session, packet);
                break;

            case KnightsSubOpcode.MarkRegionReq:
                break;

            case KnightsSubOpcode.AllyCreate:
                await knightsManagementPacketService.HandleAllyCreateAsync(session, packet);
                break;

            case KnightsSubOpcode.AllyReq:
                await knightsManagementPacketService.HandleAllyReqAsync(session, packet);
                break;

            case KnightsSubOpcode.AllyInsert:
                await knightsManagementPacketService.HandleAllyInsertAsync(session, packet);
                break;

            case KnightsSubOpcode.AllyPunish:
                await knightsManagementPacketService.HandleAllyPunishAsync(session, packet);
                break;

            case KnightsSubOpcode.AllyRemove:
                await knightsManagementPacketService.HandleAllyRemoveAsync(session);
                break;

            case KnightsSubOpcode.AllyList:
                await knightsManagementPacketService.HandleAllyListAsync(session);
                break;

            default:
                logger.LogDebug("Unhandled knights sub-opcode 0x{SubOp:X2} from {Name}", (byte)subOpcode, session.Name);
                break;
        }
    }

    private async Task SendTop10Async(UserSession session)
    {
        var entries = new List<KnightsPacketWriter.TopEntry>(
            KnightsPacketWriter.TopBoardPerNation * 2);
        entries.AddRange(TopNationClans(AccountNation.Karus));
        entries.AddRange(TopNationClans(AccountNation.ElMorad));

        await session.Client.SendPacket(KnightsPacketWriter.TopBoard(entries));
    }

    private List<KnightsPacketWriter.TopEntry> TopNationClans(AccountNation nation)
    {
        var topClans = sessionManager.Knights.GetAll()
            .Where(clan => clan.Nation == (byte)nation)
            .OrderByDescending(clan => clan.ClanPointFund)
            .ThenByDescending(clan => clan.Points)
            .ThenBy(clan => clan.Id)
            .Take(KnightsPacketWriter.TopBoardPerNation)
            .ToList();

        var entries = new List<KnightsPacketWriter.TopEntry>(KnightsPacketWriter.TopBoardPerNation);
        short rank = 0;
        foreach (var clan in topClans)
            entries.Add(new KnightsPacketWriter.TopEntry(clan.Id, clan.Name, rank++));

        for (; rank < KnightsPacketWriter.TopBoardPerNation; rank++)
        {
            entries.Add(new KnightsPacketWriter.TopEntry(
                KnightsPacketWriter.NoValue, string.Empty, rank));
        }

        return entries;
    }
}
