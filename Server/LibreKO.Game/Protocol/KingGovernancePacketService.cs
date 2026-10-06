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
    Task HandleNationIntroAsync(UserSession session, Packet packet);
}

public class KingGovernancePacketService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IItemGrantService itemGrantService,
    IKingEventState kingEventState,
    IKingSystemRuntimeService kingSystemRuntimeService,
    TimeWeatherBroadcastService timeWeather,
    ILogger<KingGovernancePacketService> logger) : IKingGovernancePacketService
{
    private const byte MaxTerritoryTariff = 5;
    private const int KingsSceptor = 910_074_000;
    private const int MaxIntroLength = 200;

    public async Task HandleTaxAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var taxOpcode = packet.ReadByte();
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var isKing = kingSystemRuntimeService.IsKing(session, kingData);

        switch (taxOpcode)
        {
            case KingPacketConstants.TaxCollect:
                await HandleKingsFundCollectionAsync(session, kingData, isKing);
                break;

            case KingPacketConstants.TaxTariffView:
                await SendTariffAsync(session, kingData, isKing);
                break;

            case KingPacketConstants.TaxTariffChange:
                await HandleTariffUpdateAsync(session, packet, kingData, isKing);
                break;

            case KingPacketConstants.TaxSceptor:
                await HandleSceptorRequestAsync(session, isKing);
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
                await session.Client.SendPacket(KingPacketWriter.Flag(KingPacketConstants.Event, eventOpcode, 0));
                break;
        }
    }

    public async Task HandleNationIntroAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var introOpcode = packet.ReadByte();
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);

        switch (introOpcode)
        {
            case KingPacketConstants.NationIntroRead:
                await session.Client.SendPacket(KingPacketWriter.NationIntro(kingData?.IntroMessage ?? string.Empty));
                break;

            case KingPacketConstants.NationIntroWrite:
                await HandleNationIntroWriteAsync(session, packet, kingData);
                break;
        }
    }

    private async Task HandleNationIntroWriteAsync(UserSession session, Packet packet, KingSystemData? kingData)
    {
        var intro = packet.RemainingBytes >= sizeof(ushort) ? packet.ReadString() : null;
        if (intro == null
            || kingData == null
            || !kingSystemRuntimeService.IsKing(session, kingData)
            || intro.Length > MaxIntroLength)
        {
            await session.Client.SendPacket(KingPacketWriter.NationIntroWritten(KingPacketConstants.NationIntroRefused));
            return;
        }

        kingData.IntroMessage = intro;
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.IntroMessage);
        logger.LogInformation("King {Name} rewrote the introduction of nation {Nation}", session.Name, session.Nation);

        await session.Client.SendPacket(KingPacketWriter.NationIntroWritten(KingPacketConstants.NationIntroSaved));
    }

    private async Task HandleKingsFundCollectionAsync(UserSession session, KingSystemData? kingData, bool isKing)
    {
        if (!isKing || kingData == null)
        {
            await SendTaxRefusalAsync(session, KingPacketConstants.TaxCollect);
            return;
        }

        var room = Math.Max(0L, (long)ExchangePacketConstants.CoinMax - session.Money);
        var fromTax = (int)Math.Min(Math.Max(kingData.TerritoryTax, 0), room);
        var fromTribute = (int)Math.Min(Math.Max(kingData.Tribute, 0), room - fromTax);
        var collected = fromTax + fromTribute;

        session.Money += collected;
        kingData.TerritoryTax -= fromTax;
        kingData.Tribute -= fromTribute;
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.TerritoryTax);
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.Tribute);
        logger.LogInformation("King {Name} collected {Amount} coins of King's Fund in nation {Nation}", session.Name, collected, session.Nation);

        await session.Client.SendPacket(KingPacketWriter.KingsFundCollected((uint)session.Money, (uint)collected));
    }

    private static async Task SendTariffAsync(UserSession session, KingSystemData? kingData, bool isKing)
    {
        if (!isKing || kingData == null)
        {
            await SendTaxRefusalAsync(session, KingPacketConstants.TaxTariffView);
            return;
        }

        await session.Client.SendPacket(KingPacketWriter.Tariff(KingPacketConstants.TaxTariffView, kingData.TerritoryTariff));
    }

    private async Task HandleTariffUpdateAsync(UserSession session, Packet packet, KingSystemData? kingData, bool isKing)
    {
        if (!isKing || kingData == null || packet.RemainingBytes < 1)
        {
            await SendTaxRefusalAsync(session, KingPacketConstants.TaxTariffChange);
            return;
        }

        var newTariff = packet.ReadByte();
        if (newTariff > MaxTerritoryTariff)
        {
            await SendTaxRefusalAsync(session, KingPacketConstants.TaxTariffChange);
            return;
        }

        kingData.TerritoryTariff = newTariff;
        logger.LogInformation("King {Name} set tariff to {Tariff}% for nation {Nation}", session.Name, newTariff, session.Nation);
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.TerritoryTariff);

        await session.Client.SendPacket(KingPacketWriter.Tariff(KingPacketConstants.TaxTariffChange, newTariff));
    }

    private async Task HandleSceptorRequestAsync(UserSession session, bool isKing)
    {
        if (!isKing)
        {
            logger.LogWarning("{Name} asked for the King's Sceptor without being king", session.Name);
            return;
        }

        var result = await GrantSceptorAsync(session);
        await session.Client.SendPacket(
            KingPacketWriter.Result(KingPacketConstants.Tax, KingPacketConstants.TaxSceptor, result));
    }

    private async Task<short> GrantSceptorAsync(UserSession session)
    {
        if (CarriesInBag(session, KingsSceptor))
            return KingPacketConstants.SceptorAlreadyOwned;

        var sceptor = gameDataService.GetItem(KingsSceptor);
        if (sceptor == null)
        {
            logger.LogError("King's Sceptor {ItemId} is missing from the item table", KingsSceptor);
            return KingPacketConstants.SceptorNoFreeSlot;
        }

        return await itemGrantService.GrantAsync(session, sceptor, 1) > 0
            ? KingPacketWriter.Accepted
            : KingPacketConstants.SceptorNoFreeSlot;
    }

    private static bool CarriesInBag(UserSession session, int itemId)
    {
        for (var slot = InventoryConstants.InventoryStart; slot < InventoryConstants.InventoryStart + InventoryConstants.HaveMax; slot++)
        {
            if (session.Inventory[slot].ItemId == itemId)
                return true;
        }

        return false;
    }

    private static Task SendTaxRefusalAsync(UserSession session, byte taxOpcode) =>
        session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Tax, taxOpcode, KingPacketConstants.NotKing));

    private async Task HandleNoahEventAsync(UserSession session, Packet packet, KingSystemData? kingData, bool isKing)
    {
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

        var notice = KingPacketWriter.Notice(1, $"The King has activated a {amount}% gold drop bonus for 30 minutes!");
        await kingSystemRuntimeService.BroadcastToNationAsync(session.Nation, notice);
    }

    private async Task HandleExpEventAsync(UserSession session, Packet packet, KingSystemData? kingData, bool isKing)
    {
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
}
