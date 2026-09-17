using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface ICombatNotificationService
{
    Task SendDeathNoticeAsync(UserSession killer, UserSession victim);
    Task SendHpChangeAsync(UserSession session, int attackerId = -1);
    Task SendMspChangeAsync(UserSession session);
    Task SendNpcTargetHpAsync(UserSession attacker, NpcInstance npc, int damage = 0);
    Task SendPlayerTargetHpAsync(UserSession attacker, UserSession victim, int damage = 0);
    Task SendPartyHpUpdateAsync(UserSession session);
    Task SendPartyLevelUpdateAsync(UserSession session);
    Task SendPartyClassUpdateAsync(UserSession session);
    Task SendPartyStatusUpdateAsync(UserSession session, byte statusType, bool applied);
    Task SendToPartyAsync(PartyGroup party, Packet packet);
}

public class CombatNotificationService(SessionManager sessionManager,
    ILogger<CombatNotificationService> logger) : ICombatNotificationService
{
    public async Task SendDeathNoticeAsync(UserSession killer, UserSession victim)
    {
        logger.LogInformation("PvP death: {Killer} killed {Victim} in zone {Zone}", killer.Name, victim.Name, victim.ZoneId);

        var packet = ChatPacketWriter.DeathNotice(
            (byte)victim.Nation, (byte)killer.Nation, (byte)DeathNoticeType.WithCoordinates,
            killer.CharacterId, killer.Name, victim.CharacterId, victim.Name,
            victim.GetPosX, victim.GetPosZ);

        foreach (var player in sessionManager.GetAll())
        {
            if (player.ZoneId == victim.ZoneId)
                await player.Client.SendPacket(packet);
        }
    }

    public async Task SendHpChangeAsync(UserSession session, int attackerId = -1)
    {
        var packet = VitalsPacketWriter.HpChange(session.MaxHp, session.Hp, attackerId);
        await session.Client.SendPacket(packet);

        await SendPartyHpUpdateAsync(session);
    }

    public async Task SendMspChangeAsync(UserSession session)
    {
        var packet = VitalsPacketWriter.MpChange(session.MaxMp, session.Mp);
        await session.Client.SendPacket(packet);

        await SendPartyHpUpdateAsync(session);
    }

    public async Task SendNpcTargetHpAsync(UserSession attacker, NpcInstance npc, int damage = 0)
    {
        var packet = TargetHpPacketWriter.For(npc.UniqueId, npc.MaxHp, npc.Hp, damage);
        await attacker.Client.SendPacket(packet);
    }

    public async Task SendPlayerTargetHpAsync(UserSession attacker, UserSession victim, int damage = 0)
    {
        if (attacker.CharacterId == victim.CharacterId)
            return;

        var packet = TargetHpPacketWriter
            .For(victim.CharacterId, victim.MaxHp, victim.Hp, damage);
        await attacker.Client.SendPacket(packet);
    }

    public async Task SendPartyHpUpdateAsync(UserSession session)
    {
        if (!session.IsInParty)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null)
            return;

        var packet = PartyPacketWriter.VitalsChange(
            session.CharacterId, session.MaxHp, session.Hp, session.MaxMp, session.Mp);
        await SendToPartyAsync(party, packet);
    }

    public async Task SendPartyLevelUpdateAsync(UserSession session)
    {
        if (!session.IsInParty)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null)
            return;

        var level = PartyPacketWriter.LevelChange(session.CharacterId, session.Level);
        await SendToPartyAsync(party, level);

        var vitals = PartyPacketWriter.VitalsChange(
            session.CharacterId, session.MaxHp, session.Hp, session.MaxMp, session.Mp);
        await SendToPartyAsync(party, vitals);
    }

    public async Task SendPartyClassUpdateAsync(UserSession session)
    {
        if (!session.IsInParty)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null)
            return;

        var packet = PartyPacketWriter.ClassChange(session.CharacterId, session.Class);
        await SendToPartyAsync(party, packet);
    }

    public async Task SendPartyStatusUpdateAsync(UserSession session, byte statusType, bool applied)
    {
        if (!session.IsInParty)
            return;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null)
            return;

        var packet = PartyPacketWriter.StatusEffect(session.CharacterId, statusType, applied);
        await SendToPartyAsync(party, packet);
    }

    public async Task SendToPartyAsync(PartyGroup party, Packet packet)
    {
        for (var index = 0; index < PartyGroup.MaxMembers; index++)
        {
            if (party.MemberIds[index] < 0)
                continue;

            var member = sessionManager.GetByCharacterId(party.MemberIds[index]);
            if (member != null)
                await member.Client.SendPacket(packet);
        }
    }

    private enum DeathNoticeType : byte
    {
        WithCoordinates = 0
    }
}
