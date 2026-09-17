using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IKingGovernancePacketService
{
    Task HandleTaxAsync(UserSession session, Packet packet);
    Task HandleKingEventAsync(UserSession session, Packet packet);
    Task HandleNationIntroAsync(UserSession session);
    Task HandleKingNpcAsync(UserSession session);
}

public class KingGovernancePacketService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IKingEventState kingEventState,
    IKingSystemRuntimeService kingSystemRuntimeService,
    TimeWeatherBroadcastService timeWeather,
    ILogger<KingGovernancePacketService> logger) : IKingGovernancePacketService
{
    public async Task HandleTaxAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var taxOpcode = packet.ReadByte();
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var isKing = kingSystemRuntimeService.IsKing(session, kingData);

        switch (taxOpcode)
        {
            case 2:
                await HandleTaxCollectionAsync(session, kingData, isKing);
                break;

            case 3:
                await SendTariffAsync(session, kingData);
                break;

            case 4:
                await HandleTariffUpdateAsync(session, packet, kingData, isKing);
                break;

            case 7:
                await HandleScepterRequestAsync(session, isKing);
                break;
        }
    }

    public async Task HandleKingEventAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var eventOpcode = packet.ReadByte();
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var isKing = kingSystemRuntimeService.IsKing(session, kingData);

        switch (eventOpcode)
        {
            case KingPacketConstants.EventNoah:
                await HandleNoahEventAsync(session, packet, kingData, isKing);
                break;

            case KingPacketConstants.EventExp:
                await HandleExpEventAsync(session, packet, kingData, isKing);
                break;

            case KingPacketConstants.EventPrize:
                await HandlePrizeEventAsync(session, packet, kingData, isKing);
                break;

            case KingPacketConstants.EventWeather:
                await HandleWeatherEventAsync(session, packet, kingData, isKing);
                break;

            case KingPacketConstants.EventNotice:
                await HandleNoticeEventAsync(session, packet, isKing);
                break;

            default:
                var failure = CreateEventResponse(eventOpcode);
                await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, eventOpcode, 0));
                break;
        }
    }

    public async Task HandleNationIntroAsync(UserSession session)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);

        await session.Client.SendPacket(KingPacketWriter.NationIntro(
            KingPacketConstants.NationIntro, 1,
            kingData?.KingName?.Trim() ?? string.Empty,
            kingData?.NationalTreasury ?? 0,
            kingData?.TerritoryTariff ?? 0));
    }

    public async Task HandleKingNpcAsync(UserSession session)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);

        await session.Client.SendPacket(KingPacketWriter.KingNpc(
            KingPacketConstants.Npc, kingData?.KingName?.Trim() ?? string.Empty));
    }

    private async Task HandleTaxCollectionAsync(UserSession session, KingSystemData? kingData, bool isKing)
    {
        var response = CreateTaxResponse(TaxCollect);
        if (!isKing || kingData == null || kingData.TerritoryTax <= 0)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Tax, 2, 0));
            return;
        }

        var taxAmount = kingData.TerritoryTax;
        session.Money += taxAmount;
        kingData.TerritoryTax = 0;
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.TerritoryTax);
        await userNotificationService.SendGoldGainAsync(session, taxAmount);

        await session.Client.SendPacket(KingPacketWriter.FlagWithAmount(
            KingPacketConstants.Tax, TaxCollect, 1, taxAmount));
    }

    private static async Task SendTariffAsync(UserSession session, KingSystemData? kingData)
    {
        await session.Client.SendPacket(KingPacketWriter.FlagWithValue(
            KingPacketConstants.Tax, TaxTariffView, 1, kingData?.TerritoryTariff ?? 0));
    }

    private async Task HandleTariffUpdateAsync(UserSession session, Packet packet, KingSystemData? kingData, bool isKing)
    {
        var response = CreateTaxResponse(4);
        if (!isKing || kingData == null || packet.RemainingBytes < 1)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Tax, 4, 0));
            return;
        }

        var newTariff = packet.ReadByte();
        if (newTariff > 10)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Tax, 4, 0));
            return;
        }

        kingData.TerritoryTariff = newTariff;
        logger.LogInformation("King {Name} set tariff to {Tariff}% for nation {Nation}", session.Name, newTariff, session.Nation);
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.TerritoryTariff);

        await session.Client.SendPacket(KingPacketWriter.FlagWithValue(KingPacketConstants.Tax, 4, 1, newTariff));
    }

    private async Task HandleScepterRequestAsync(UserSession session, bool isKing)
    {
        var response = CreateTaxResponse(7);
        if (!isKing)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Tax, 7, 0));
            return;
        }

        const int scepterItemId = 910074311;
        var slot = session.FindSlotForItem(scepterItemId, gameDataService);
        if (slot < 0)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Tax, 7, 0));
            return;
        }

        session.Inventory[slot].ItemId = scepterItemId;
        session.Inventory[slot].Durability = 1;
        session.Inventory[slot].Count = 1;
        await userNotificationService.SendStackChangeAsync(session, (byte)slot, scepterItemId, 1, 1, isNewItem: true);

        await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Tax, 7, 1));
    }

    private async Task HandleNoahEventAsync(UserSession session, Packet packet, KingSystemData? kingData, bool isKing)
    {
        var response = CreateEventResponse(KingPacketConstants.EventNoah);
        if (!isKing || kingData == null || packet.RemainingBytes < 1)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventNoah, 0));
            return;
        }

        var amount = packet.ReadByte();
        if (amount < 1 || amount > 3)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventNoah, 0));
            return;
        }

        var cost = 50_000_000 * amount;
        if (kingData.NationalTreasury < cost)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventNoah, 0));
            return;
        }

        kingData.NationalTreasury -= cost;
        kingEventState.ActivateNoahBonus(session.Nation, amount, TimeSpan.FromMinutes(30));
        logger.LogInformation("King {Name} activated {Amount}% Noah bonus for nation {Nation}", session.Name, amount, session.Nation);
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.NationalTreasury);

        await session.Client.SendPacket(KingPacketWriter.FlagWithValue(KingPacketConstants.Event, KingPacketConstants.EventNoah, 1, amount));

        var notice = KingPacketWriter.Notice(1, $"The King has activated a {amount}% Noah drop bonus for 30 minutes!");
        await kingSystemRuntimeService.BroadcastToNationAsync(session.Nation, notice);
    }

    private async Task HandleExpEventAsync(UserSession session, Packet packet, KingSystemData? kingData, bool isKing)
    {
        var response = CreateEventResponse(KingPacketConstants.EventExp);
        if (!isKing || kingData == null || packet.RemainingBytes < 1)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventExp, 0));
            return;
        }

        var amount = packet.ReadByte();
        if (amount != 10 && amount != 30 && amount != 50)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventExp, 0));
            return;
        }

        var cost = 30_000_000 * amount;
        if (kingData.NationalTreasury < cost)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventExp, 0));
            return;
        }

        kingData.NationalTreasury -= cost;
        kingEventState.ActivateExpBonus(session.Nation, amount, TimeSpan.FromMinutes(30));
        logger.LogInformation("King {Name} activated {Amount}% EXP bonus for nation {Nation}", session.Name, amount, session.Nation);
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.NationalTreasury);

        await session.Client.SendPacket(KingPacketWriter.FlagWithValue(KingPacketConstants.Event, KingPacketConstants.EventExp, 1, amount));

        var notice = KingPacketWriter.Notice(1, $"The King has activated a {amount}% EXP bonus for 30 minutes!");
        await kingSystemRuntimeService.BroadcastToNationAsync(session.Nation, notice);
    }

    private async Task HandlePrizeEventAsync(UserSession session, Packet packet, KingSystemData? kingData, bool isKing)
    {
        var response = CreateEventResponse(KingPacketConstants.EventPrize);
        if (!isKing || kingData == null || packet.RemainingBytes < 6)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventPrize, 0));
            return;
        }

        var targetName = packet.ReadSByteString();
        if (packet.RemainingBytes < 4)
            return;

        var amount = packet.ReadInt();
        var target = sessionManager.GetByName(targetName);
        if (amount <= 0 || target == null || target.Nation != session.Nation || kingData.NationalTreasury < amount)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventPrize, 0));
            return;
        }

        kingData.NationalTreasury -= amount;
        target.Money += amount;
        await userNotificationService.SendGoldGainAsync(target, amount);
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.NationalTreasury);

        await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventPrize, 1));
    }

    private async Task HandleWeatherEventAsync(UserSession session, Packet packet, KingSystemData? kingData, bool isKing)
    {
        var response = CreateEventResponse(KingPacketConstants.EventWeather);
        if (!isKing || kingData == null || packet.RemainingBytes < 2)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventWeather, 0));
            return;
        }

        var weatherType = packet.ReadByte();
        var weatherAmount = packet.ReadByte();
        if (weatherType < 1 || weatherType > 3
            || kingData.NationalTreasury < 100_000)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventWeather, 0));
            return;
        }

        kingData.NationalTreasury -= 100_000;
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.NationalTreasury);

        await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventWeather, 1));

        timeWeather.TrySetWeather(weatherType, weatherAmount);
        await timeWeather.BroadcastWeatherAsync();
    }

    private async Task HandleNoticeEventAsync(UserSession session, Packet packet, bool isKing)
    {
        var response = CreateEventResponse(KingPacketConstants.EventNotice);
        if (!isKing || packet.RemainingBytes < 2)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventNotice, 0));
            return;
        }

        var message = packet.ReadString();
        if (string.IsNullOrEmpty(message) || message.Length > 256)
        {
            await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventNotice, 0));
            return;
        }

        await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, KingPacketConstants.EventNotice, 1));

        var notice = ChatPacketWriter
            .NationNotice((byte)session.Nation, session.CharacterId, session.Name, message)
            ;
        await kingSystemRuntimeService.BroadcastToNationAsync(session.Nation, notice);
    }

    private const byte TaxCollect = 2;
    private const byte TaxTariffView = 3;

    private static Packet CreateTaxResponse(byte taxOpcode) =>
        KingPacketWriter.Election(taxOpcode, KingPacketConstants.Tax);

    private static Packet CreateEventResponse(byte eventOpcode) =>
        KingPacketWriter.Election(eventOpcode, KingPacketConstants.Event);
}
