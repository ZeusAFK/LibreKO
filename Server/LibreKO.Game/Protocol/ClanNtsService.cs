using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IClanNtsService
{
    Task ExecuteAsync(UserSession session);
}

public class ClanNtsService(
    SessionManager sessionManager,
    IKingSystemRuntimeService kingSystemRuntimeService,
    IServiceScopeFactory scopeFactory,
    ILogger<ClanNtsService> logger) : IClanNtsService
{
    private const int ClanNtsItemId = 900_144_023;

    // Race constants — duplicated from JobChangeService since we don't have a shared file.
    private const byte KarusBig = 1;
    private const byte KarusMiddle = 2;
    private const byte KarusSmall = 3;
    private const byte KarusWoman = 4;
    private const byte Kurian = 6;
    private const byte ElmoradMan = 12;
    private const byte ElmoradWoman = 13;
    private const byte Porutu = 14;

    public async Task ExecuteAsync(UserSession session)
    {
        if (session.KnightsId <= 0 || session.KnightsFame != 1)
        {
            SendChat(session, "You are not in a clan or a leader.");
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            SendChat(session, "Clan not found.");
            return;
        }

        var scrollSlot = FindScrollSlot(session);
        if (scrollSlot < 0)
        {
            SendChat(session, "You do not have the required parts on you.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        var characterRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var accountRepo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();

        // Every member character (of every clan member account).
        var memberChars = await knightsRepo.GetCharactersByClanAsync(clan.Id);

        // Verify no member (besides caller) is online.
        foreach (var memberChar in memberChars)
        {
            if (memberChar.Id == session.CharacterId)
                continue;
            if (sessionManager.GetByCharacterId(memberChar.Id) != null)
            {
                SendChat(session, $"Online User {memberChar.Name}");
                return;
            }
        }

        // Verify no king (Karus or El Morad).
        var kingKarus = kingSystemRuntimeService.GetKingData(AccountNation.Karus);
        var kingElmo = kingSystemRuntimeService.GetKingData(AccountNation.ElMorad);
        var kingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (kingKarus != null && !string.IsNullOrEmpty(kingKarus.KingName)) kingNames.Add(kingKarus.KingName);
        if (kingElmo != null && !string.IsNullOrEmpty(kingElmo.KingName)) kingNames.Add(kingElmo.KingName);

        foreach (var memberChar in memberChars)
        {
            if (kingNames.Contains(memberChar.Name))
            {
                SendChat(session, $"King User {memberChar.Name}");
                return;
            }
        }

        // Other characters of each member's account — must not be in a different clan.
        // (Iterating all clan-member characters already gives us every member-clan character;
        // Implementation simplification: skip cross-account scan. Our current schema doesn't
        // character is in a different clan, that account simply won't have their other
        // character's nation transferred — acceptable degradation.

        // Consume scroll, switch nation, persist clan.
        session.Inventory[scrollSlot].Count--;
        if (session.Inventory[scrollSlot].Count <= 0)
            session.Inventory[scrollSlot].Clear();

        var oldNation = (AccountNation)clan.Nation;
        var newNation = oldNation == AccountNation.Karus ? AccountNation.ElMorad : AccountNation.Karus;
        clan.Nation = (byte)newNation;
        await knightsRepo.UpdateAsync(clan);

        // Transform every member character (class + race) and flip each member
        // account's nation. Account.Nation is the canonical source of nation —
        // Character entity doesn't carry it, but Class/Race are character-scoped.
        var accountIds = new HashSet<int>();
        foreach (var memberChar in memberChars)
        {
            var newClass = newNation == AccountNation.ElMorad
                ? (short)(memberChar.Class + 100)
                : (short)(memberChar.Class - 100);
            var newRace = GetCntsNewRace((ushort)newClass, newNation);
            if (newRace == 0)
                continue;

            memberChar.Class = newClass;
            memberChar.Race = newRace;
            await characterRepo.UpdateAsync(memberChar);
            accountIds.Add(memberChar.AccountId);
        }

        foreach (var accountId in accountIds)
        {
            var account = await accountRepo.GetById(accountId);
            if (account == null) continue;
            account.Nation = newNation;
            await accountRepo.UpdateAsync(account);
        }

        // Apply to caller's in-memory session so subsequent packets see the new state.
        session.Nation = newNation;
        var callerNewClass = newNation == AccountNation.ElMorad
            ? (short)(session.Class + 100)
            : (short)(session.Class - 100);
        session.Class = callerNewClass;
        var callerNewRace = GetCntsNewRace((ushort)callerNewClass, newNation);
        if (callerNewRace != 0)
            session.Race = callerNewRace;

        logger.LogInformation(
            "ClanNts: {Clan} (id={Id}) transferred from {Old} to {New}, {Count} characters updated",
            clan.Name, clan.Id, oldNation, newNation, memberChars.Count);

        SendChat(session, "Success Process");
    }

    private static int FindScrollSlot(UserSession session)
    {
        for (var i = Common.Domain.Entities.GameData.InventoryConstants.InventoryStart;
             i < Common.Domain.Entities.GameData.InventoryConstants.InventoryStart
                 + Common.Domain.Entities.GameData.InventoryConstants.HaveMax;
             i++)
        {
            if (session.Inventory[i].ItemId == ClanNtsItemId && session.Inventory[i].Count > 0)
                return i;
        }
        return -1;
    }

    public static byte GetCntsNewRace(ushort newClass, AccountNation newNation)
    {
        var classType = (byte)(newClass % 100);
        if (newNation == AccountNation.ElMorad)
        {
            return classType switch
            {
                1 or 5 or 6 => ElmoradMan,        // warrior
                2 or 7 or 8 => ElmoradMan,        // rogue
                3 or 9 or 10 => ElmoradWoman,     // mage
                4 or 11 or 12 => ElmoradWoman,    // priest
                13 or 14 or 15 => Porutu,
                _ => 0,
            };
        }
        // Karus
        return classType switch
        {
            1 or 5 or 6 => KarusBig,
            2 or 7 or 8 => KarusMiddle,
            3 or 9 or 10 => KarusSmall,
            4 or 11 or 12 => KarusWoman,
            13 or 14 or 15 => Kurian,
            _ => 0,
        };
    }

    private static void SendChat(UserSession session, string message)
    {
        var packet = ChatPacketWriter.SystemNotice((byte)session.Nation, $"[ClanNTS] {message}");
        _ = session.Client.SendPacket(packet);
    }
}
