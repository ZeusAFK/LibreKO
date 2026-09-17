using LibreKO.Common.Enums;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IItemUpgradeService
{
    Task HandleUpgradeAsync(IClient client, Packet packet);
}

public class ItemUpgradeService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IGlobalAnvilRateService globalAnvilRateService,
    IChaoticGeneratorService chaoticGeneratorService,
    ILogger<ItemUpgradeService> logger) : IItemUpgradeService
{


    private const byte UpgradeTypeNormal = 1;
    private const byte UpgradeTypePreview = 2;

    private const byte UpgradeFailed = 0;
    private const byte UpgradeSucceeded = 1;
    private const byte UpgradeTrading = 2;
    private const byte UpgradeNeedCoins = 3;
    private const byte UpgradeNoMatch = 4;
    private const byte UpgradeRental = 5;

    private const byte ObjectAnvil = 8;

    private const int UpgradeSlotCount = 10;
    private const int UpgradeMaterialMax = 8;
    private const int UpgradeRequestBytes = 1 + 4 + UpgradeSlotCount * 5;
    private const float MaxAnvilRangeSq = 100.0f;

    private const int ItemTrina = 700002000;
    private const int ItemKarivdis = 379258000;
    private const int ItemLowClassTrina = 353000000;
    private const int ItemMiddleClassTrina = 352900000;
    private const int ItemAccessoryTrina = 354000000;
    private const int ItemBlessingLogos = 890092000;


    private const short ItemClassUnclassified = 0;
    private const short ItemClassLow = 1;
    private const short ItemClassMiddle = 2;
    private const short ItemClassHigh = 3;
    private const short ItemClassReverse = 4;
    private const short ItemClassRebirth = 5;
    private const short ItemClassUnique = 7;
    private const short ItemClassKrowaz = 33;
    private const short ItemClassKrowazRebirth = 35;

    private const byte AccessoryKindEarring = 91;
    private const byte AccessoryKindNecklace = 92;
    private const byte AccessoryKindRing = 93;
    private const byte AccessoryKindBelt = 94;

    private const int AccessoryCompoundCount = 3;
    private const int MaxGenRate = 10000;
    private const short LogosGenRate = 3300;
    private const int LogosDowngradeBound = 6701;
    private const short LogosMaxGrade = 10;

    private const int SealPrice = 1_000_000;
    private const int SealStoneItem = 810890000;
    private const int SealCodeLength = 8;

    private sealed record SealRule(
        Func<ItemFlag, bool> IsApplicable,
        ItemFlag AppliedFlag,
        int Price = 0,
        bool NeedsCode = false,
        bool ChargesStones = false);

    private static readonly Dictionary<ItemSealType, SealRule> SealRules = new()
    {
        [ItemSealType.Seal] = new SealRule(
            flag => flag != ItemFlag.Sealed, ItemFlag.Sealed, SealPrice, NeedsCode: true),
        [ItemSealType.Unseal] = new SealRule(
            flag => flag == ItemFlag.Sealed, ItemFlag.Unsealed, NeedsCode: true),
        [ItemSealType.Bind] = new SealRule(
            flag => flag != ItemFlag.Bound, ItemFlag.Bound),
        [ItemSealType.Unbind] = new SealRule(
            flag => flag == ItemFlag.Bound, ItemFlag.NotBound, ChargesStones: true),
    };

    private enum ScrollClass
    {
        None,
        LowClass,
        MiddleClass,
        HighClass,
        ClassUpgrade,
        ReverseConvert,
        Rebirth,
        Accessories
    }

    public async Task HandleUpgradeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var subOpcode = (ItemUpgradeSubOpcode)packet.ReadByte();
        switch (subOpcode)
        {
            case ItemUpgradeSubOpcode.Upgrade:
            case ItemUpgradeSubOpcode.UpgradeAccessories:
            case ItemUpgradeSubOpcode.UpgradeRebirth:
                await HandleStandardUpgradeAsync(session, packet, subOpcode);
                break;
            case ItemUpgradeSubOpcode.ItemSeal:
                await HandleItemSealAsync(session, packet);
                break;
            case ItemUpgradeSubOpcode.BifrostExchange:
                await chaoticGeneratorService.HandlePieceExchangeAsync(session, packet);
                break;
            default:
                logger.LogDebug("Unhandled item upgrade sub-opcode {SubOpcode} from {Name}", subOpcode, session.Name);
                break;
        }
    }

    private async Task HandleStandardUpgradeAsync(UserSession session, Packet packet, ItemUpgradeSubOpcode responseSubOpcode)
    {
        var requestBytes = packet.RemainingBytes;

        if (session.Trade.IsTrading || session.Trade.IsMerchanting || session.Hp <= 0)
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: trading={IsTrading} merchanting={IsMerchanting} hp={Hp}",
                session.Name, session.Trade.IsTrading, session.Trade.IsMerchanting, session.Hp);
            await SendUpgradeResultAsync(session, responseSubOpcode, UpgradeTypeNormal, UpgradeTrading, [], []);
            return;
        }

        if (!TryParseUpgradeRequest(packet, out var upgradeType, out var npcUniqueId, out var itemIds, out var positions))
        {
            logger.LogWarning("Upgrade parse failed for {Name}: bytes={Bytes}", session.Name, requestBytes);
            await SendUpgradeResultAsync(session, responseSubOpcode, UpgradeTypeNormal, UpgradeNoMatch, [], []);
            return;
        }

        logger.LogInformation(
            "Upgrade request from {Name}: sub={Sub} type={UpgradeType} anvil={AnvilId} money={Money} zone={Zone} request={Request}",
            session.Name, responseSubOpcode, DescribeUpgradeType(upgradeType), npcUniqueId, session.Money, session.ZoneId,
            FormatUpgradeRequest(itemIds, positions));

        if (!IsAtAnvil(session, npcUniqueId))
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: not standing at anvil {AnvilId} in zone {Zone}",
                session.Name, npcUniqueId, session.ZoneId);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        for (var index = 0; index < UpgradeSlotCount; index++)
        {
            if (positions[index] < 0 || itemIds[index] == 0)
                continue;

            if (positions[index] >= InventoryConstants.HaveMax)
            {
                logger.LogWarning(
                    "Upgrade rejected for {Name}: slot {SlotIndex} position {Position} out of range",
                    session.Name, index, positions[index]);
                await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
                return;
            }

            var slot = session.Inventory[InventoryConstants.InventoryStart + positions[index]];
            if (slot.IsEmpty || slot.ItemId != itemIds[index])
            {
                logger.LogWarning(
                    "Upgrade rejected for {Name}: slot {SlotIndex} position={Position} requestItem={RequestItemId} inventoryItem={InventoryItemId}",
                    session.Name, index, positions[index], itemIds[index], slot.ItemId);
                await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
                return;
            }

            if (!slot.IsTradable)
            {
                logger.LogWarning(
                    "Upgrade rejected for {Name}: slot {SlotIndex} item {ItemId} carries flag {Flag}",
                    session.Name, index, itemIds[index], slot.Flag);
                await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeRental, itemIds, positions);
                return;
            }
        }

        var originPosition = positions[0];
        var originItemId = itemIds[0];
        if (originPosition < 0 || originItemId == 0)
        {
            logger.LogWarning("Upgrade rejected for {Name}: no origin item in the request", session.Name);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        var originItem = session.Inventory[InventoryConstants.InventoryStart + originPosition];
        var originItemData = gameDataService.GetItem(originItemId);
        if (originItemData == null)
        {
            logger.LogWarning("Upgrade rejected for {Name}: unknown origin item {OriginItemId}", session.Name, originItemId);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        var scrollId = 0;
        var scrollClass = ScrollClass.None;
        for (var index = 1; index < UpgradeSlotCount; index++)
        {
            if (positions[index] < 0)
                continue;

            var candidate = ClassifyScroll(itemIds[index]);
            if (candidate == ScrollClass.None)
                continue;

            scrollId = itemIds[index];
            scrollClass = candidate;
            break;
        }

        if (scrollClass == ScrollClass.None)
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: no upgrade scroll among the materials request={Request}",
                session.Name, FormatUpgradeRequest(itemIds, positions));
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        var protectionId = 0;
        var protectionCount = 0;
        var logosCount = CountMaterial(itemIds, positions, ItemBlessingLogos);
        foreach (var candidate in (int[])[ItemTrina, ItemKarivdis, ItemLowClassTrina, ItemMiddleClassTrina, ItemAccessoryTrina])
        {
            var count = CountMaterial(itemIds, positions, candidate);
            if (count == 0)
                continue;

            protectionCount += count;
            protectionId = candidate;
        }

        if (protectionCount > 1 || (logosCount > 0 && (protectionCount > 0 || logosCount > 1)))
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: conflicting protection materials protection={ProtectionCount} logos={LogosCount}",
                session.Name, protectionCount, logosCount);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        var hasLogos = logosCount > 0;
        var itemType = (short)originItemData.ItemType;
        var grade = ResolveGrade(originItemData, originItemId);

        if (hasLogos && !IsUpgradeableItemType(itemType))
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: blessing logos on item type {ItemType}", session.Name, itemType);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        if (hasLogos && (grade >= LogosMaxGrade || scrollClass is ScrollClass.ReverseConvert or ScrollClass.Accessories))
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: blessing logos not allowed at grade {Grade} with scroll {ScrollId}",
                session.Name, grade, scrollId);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        if (!IsScrollAllowed(originItemData, scrollClass))
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: scroll {ScrollId} ({ScrollClass}) does not fit item {OriginItemId} class {ItemClass}",
                session.Name, scrollId, scrollClass, originItemId, originItemData.ItemClass);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        if (scrollClass == ScrollClass.Accessories && !HasAccessoryCompound(itemIds, positions, originItemId))
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: accessory upgrade needs {Count} copies of {OriginItemId}",
                session.Name, AccessoryCompoundCount, originItemId);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        var recipe = gameDataService.GetUpgradeRecipe(originItemId, scrollId);
        if (recipe == null || recipe.NewNumber == 0)
        {
            logger.LogWarning(
                "Upgrade recipe not found for {Name}: originItem={OriginItemId} scroll={ScrollId} type={ItemType} class={ItemClass} grade={Grade}",
                session.Name, originItemId, scrollId, itemType, originItemData.ItemClass, grade);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        var setting = hasLogos ? null : gameDataService.GetUpgradeSetting(itemType, grade, scrollId, protectionId);
        var genRate = hasLogos ? LogosGenRate : (short)Math.Clamp(setting?.SuccessRate ?? 0, 0, MaxGenRate);
        var requiredNoah = hasLogos ? 0 : setting?.ReqNoah ?? 0;

        if (genRate <= 0)
        {
            logger.LogWarning(
                "Upgrade rate not found for {Name}: originItem={OriginItemId} type={ItemType} grade={Grade} scroll={ScrollId} protection={ProtectionId} setting={SettingIndex}",
                session.Name, originItemId, itemType, grade, scrollId, protectionId, setting?.Index ?? 0);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        var upgradedItemId = recipe.NewNumber;
        var upgradedItemData = gameDataService.GetItem(upgradedItemId);
        if (upgradedItemData == null)
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: recipe {RecipeIndex} points at missing item {UpgradedItemId}",
                session.Name, recipe.Index, upgradedItemId);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNoMatch, itemIds, positions);
            return;
        }

        logger.LogInformation(
            "Upgrade recipe matched for {Name}: recipe={RecipeIndex} originItem={OriginItemId} targetItem={UpgradedItemId} scroll={ScrollId} protection={ProtectionId} type={ItemType} class={ItemClass} grade={Grade} genRate={GenRate} reqNoah={ReqNoah}",
            session.Name, recipe.Index, originItemId, upgradedItemId, scrollId, protectionId, itemType,
            originItemData.ItemClass, grade, genRate, requiredNoah);

        if (session.Money < requiredNoah)
        {
            logger.LogWarning(
                "Upgrade rejected for {Name}: insufficient coins money={Money} required={RequiredNoah}",
                session.Name, session.Money, requiredNoah);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeNeedCoins, itemIds, positions);
            return;
        }

        if (upgradeType == UpgradeTypePreview)
        {
            itemIds[0] = upgradedItemId;
            logger.LogInformation(
                "Upgrade preview for {Name}: originItem={OriginItemId} targetItem={UpgradedItemId} genRate={GenRate}",
                session.Name, originItemId, upgradedItemId, genRate);
            await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, UpgradeSucceeded, itemIds, positions);
            return;
        }

        var roll = Random.Shared.Next(0, MaxGenRate);
        var rollOutcome = globalAnvilRateService.ResolveRoll(genRate, roll);
        var upgradeResult = rollOutcome.Succeeded ? UpgradeSucceeded : UpgradeFailed;

        logger.LogInformation(
            "Upgrade roll {Outcome} for {Name}: recipe={RecipeIndex} originItem={OriginItemId} targetItem={UpgradedItemId} baseGenRate={BaseGenRate} effectiveGenRate={EffectiveGenRate} modifierBeforePercent={ModifierBeforePercent} modifierAfterPercent={ModifierAfterPercent} roll={Roll}",
            rollOutcome.Succeeded ? "succeeded" : "failed", session.Name, recipe.Index, originItemId, upgradedItemId,
            rollOutcome.BaseGenRate, rollOutcome.EffectiveGenRate, rollOutcome.ModifierBeforePercent,
            rollOutcome.ModifierAfterPercent, roll);

        if (rollOutcome.Succeeded)
        {
            originItem.ItemId = upgradedItemId;
            originItem.Durability = upgradedItemData.Duration;
            itemIds[0] = upgradedItemId;
        }
        else if (hasLogos)
        {
            var downgradedItemId = originItemId - 1;
            if (grade > 1 && Random.Shared.Next(0, LogosDowngradeBound) < roll && gameDataService.GetItem(downgradedItemId) != null)
            {
                originItem.ItemId = downgradedItemId;
                itemIds[0] = downgradedItemId;
            }
        }
        else
        {
            originItem.Clear();
            itemIds[0] = 0;
        }

        if (requiredNoah > 0)
        {
            session.Money -= requiredNoah;
            await userNotificationService.SendGoldLossAsync(session, requiredNoah);
        }

        ConsumeMaterials(session, itemIds, positions);

        logger.LogInformation(
            "Upgrade completed for {Name}: sub={Sub} result={Result} anvil={AnvilId} originItem={OriginItemId} finalItem={FinalItemId} moneyAfter={MoneyAfter}",
            session.Name, responseSubOpcode, DescribeUpgradeResult(upgradeResult), npcUniqueId, originItemId, itemIds[0],
            session.Money);

        await SendUpgradeResultAsync(session, responseSubOpcode, upgradeType, upgradeResult, itemIds, positions);

        var objectEvent = MiscPacketWriter.ObjectEvent(ObjectAnvil, upgradeResult, npcUniqueId);
        await sessionManager.Regions.SendToRegion(session, objectEvent, excludeSender: false);

        if (session.IsGM)
            return;

        var noticeItemData = upgradeResult == UpgradeSucceeded ? upgradedItemData : originItemData;
        var noticeItemId = upgradeResult == UpgradeSucceeded ? upgradedItemId : originItemId;
        if (noticeItemData.UpgradeNotice == 0)
            return;

        var notice = LogosShoutPacketWriter.UpgradeAnnouncement(
            upgradeResult, session.Name, noticeItemId, (byte)session.Nation);
        await sessionManager.BroadcastToAll(notice);
    }

    private static void ConsumeMaterials(UserSession session, int[] itemIds, sbyte[] positions)
    {
        var consumed = 0;
        for (var index = 1; index < UpgradeSlotCount && consumed < UpgradeMaterialMax; index++)
        {
            if (positions[index] < 0 || itemIds[index] == 0)
                continue;

            var material = session.Inventory[InventoryConstants.InventoryStart + positions[index]];
            if (material.IsEmpty || material.ItemId != itemIds[index])
                continue;

            if (material.Count > 1)
                material.Count--;
            else
                material.Clear();

            consumed++;
        }
    }

    private static int CountMaterial(int[] itemIds, sbyte[] positions, int itemId)
    {
        var count = 0;
        for (var index = 1; index < UpgradeSlotCount; index++)
            if (positions[index] >= 0 && itemIds[index] == itemId)
                count++;

        return count;
    }

    private static bool HasAccessoryCompound(int[] itemIds, sbyte[] positions, int originItemId)
    {
        var copies = 1;
        for (var index = 1; index < UpgradeSlotCount; index++)
            if (positions[index] >= 0 && itemIds[index] == originItemId)
                copies++;

        return copies >= AccessoryCompoundCount;
    }

    private static bool IsUpgradeableItemType(short itemType)
        => itemType is ItemData.TypeKrowaz or ItemData.TypeStandard or ItemData.TypeRebirth or ItemData.TypeRebirthKrowaz;

    private static short ResolveGrade(ItemData item, int itemId)
        => item.Grade != 0 ? item.Grade : (short)(itemId % 10);

    private static ScrollClass ClassifyScroll(int itemId) => itemId switch
    {
        >= 379016000 and <= 379035000 => ScrollClass.HighClass,
        >= 379138000 and <= 379141000 => ScrollClass.HighClass,
        379152000 => ScrollClass.ClassUpgrade,
        >= 379159000 and <= 379164000 => ScrollClass.Accessories,
        >= 379205000 and <= 379220000 => ScrollClass.MiddleClass,
        >= 379221000 and <= 379235000 => ScrollClass.LowClass,
        379255000 => ScrollClass.LowClass,
        379256000 => ScrollClass.ReverseConvert,
        379257000 => ScrollClass.Rebirth,
        _ => ScrollClass.None
    };

    private static ScrollClass ItemScrollClass(short itemClass) => itemClass switch
    {
        ItemClassLow => ScrollClass.LowClass,
        ItemClassUnclassified => ScrollClass.LowClass,
        ItemClassMiddle => ScrollClass.MiddleClass,
        ItemClassHigh => ScrollClass.HighClass,
        ItemClassUnique => ScrollClass.HighClass,
        ItemClassKrowaz => ScrollClass.HighClass,
        ItemClassReverse => ScrollClass.Rebirth,
        ItemClassRebirth => ScrollClass.Rebirth,
        ItemClassKrowazRebirth => ScrollClass.Rebirth,
        _ => ScrollClass.None
    };

    private static bool IsScrollAllowed(ItemData item, ScrollClass scroll)
    {
        if (scroll == ScrollClass.Accessories)
            return item.Kind is AccessoryKindEarring or AccessoryKindNecklace or AccessoryKindRing or AccessoryKindBelt;

        return ItemScrollClass(item.ItemClass) switch
        {
            ScrollClass.LowClass => scroll is ScrollClass.LowClass or ScrollClass.MiddleClass or ScrollClass.HighClass
                or ScrollClass.ClassUpgrade,
            ScrollClass.MiddleClass => scroll is ScrollClass.MiddleClass or ScrollClass.HighClass or ScrollClass.ClassUpgrade,
            ScrollClass.HighClass => scroll is ScrollClass.HighClass or ScrollClass.ReverseConvert or ScrollClass.ClassUpgrade,
            ScrollClass.Rebirth => scroll is ScrollClass.Rebirth or ScrollClass.ReverseConvert or ScrollClass.HighClass,
            _ => false
        };
    }

    private bool IsAtAnvil(UserSession session, int anvilId)
    {
        var anvil = sessionManager.Maps?.GetObjectEvent(session.ZoneId, (short)anvilId);
        if (anvil == null || anvil.Type != ObjectAnvil)
            return false;

        var dx = session.X - anvil.PosX;
        var dz = session.Z - anvil.PosZ;
        return dx * dx + dz * dz <= MaxAnvilRangeSq;
    }

    private static bool TryParseUpgradeRequest(
        Packet packet,
        out byte upgradeType,
        out int npcId,
        out int[] itemIds,
        out sbyte[] positions)
    {
        upgradeType = UpgradeTypeNormal;
        npcId = 0;
        itemIds = new int[UpgradeSlotCount];
        positions = new sbyte[UpgradeSlotCount];

        if (packet.RemainingBytes < UpgradeRequestBytes)
            return false;

        upgradeType = packet.ReadByte();
        npcId = packet.ReadInt();
        for (var index = 0; index < UpgradeSlotCount; index++)
        {
            itemIds[index] = packet.ReadInt();
            positions[index] = unchecked((sbyte)packet.ReadByte());
        }

        return upgradeType is UpgradeTypeNormal or UpgradeTypePreview;
    }

    private static async Task SendUpgradeResultAsync(
        UserSession session,
        ItemUpgradeSubOpcode responseSubOpcode,
        byte upgradeType,
        byte resultCode,
        int[] itemIds,
        sbyte[] positions)
    {
        await session.Client.SendPacket(ItemUpgradePacketWriter.UpgradeResult(
            responseSubOpcode, upgradeType, resultCode, itemIds, positions));
    }

    private async Task HandleItemSealAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 10)
            return;

        var sealType = (ItemSealType)packet.ReadByte();
        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        var srcPos = packet.ReadByte();
        var code = packet.RemainingBytes >= 2 ? packet.ReadString() : string.Empty;

        var result = await ApplySealAsync(session, sealType, itemId, srcPos, code);

        await session.Client.SendPacket(
            ItemUpgradePacketWriter.ItemSealResult(sealType, result, itemId, srcPos));
    }

    private async Task<ItemSealResult> ApplySealAsync(
        UserSession session, ItemSealType sealType, int itemId, byte srcPos, string code)
    {
        var rule = SealRules.GetValueOrDefault(sealType);
        if (rule == null
            || session.Trade.IsTrading
            || session.Trade.IsMerchanting
            || srcPos >= InventoryConstants.HaveMax)
            return ItemSealResult.Failed;

        var slot = session.Inventory[InventoryConstants.InventoryStart + srcPos];
        if (slot.IsEmpty || slot.ItemId != itemId || !rule.IsApplicable(slot.State))
            return ItemSealResult.Failed;

        if (rule.NeedsCode)
        {
            if (session.SealCode.Length != SealCodeLength)
                return ItemSealResult.NoCodeSet;

            if (code != session.SealCode)
                return ItemSealResult.WrongCode;
        }

        if (session.Money < rule.Price)
            return ItemSealResult.NeedCoins;

        var stones = rule.ChargesStones ? gameDataService.GetItem(itemId)?.Bound ?? 0 : 0;
        if (stones > 0 && CountSealStones(session) < stones)
            return ItemSealResult.MissingMaterial;

        if (rule.Price > 0)
        {
            session.Money -= rule.Price;
            await userNotificationService.SendGoldLossAsync(session, rule.Price);
        }

        if (stones > 0)
            await SpendSealStonesAsync(session, stones);

        slot.Flag = (byte)rule.AppliedFlag;
        return ItemSealResult.Succeeded;
    }

    private static int CountSealStones(UserSession session)
    {
        var count = 0;
        for (var index = 0; index < InventoryConstants.HaveMax; index++)
        {
            var slot = session.Inventory[InventoryConstants.InventoryStart + index];
            if (slot.ItemId == SealStoneItem)
                count += slot.Count;
        }

        return count;
    }

    private async Task SpendSealStonesAsync(UserSession session, int count)
    {
        var remaining = count;
        for (var index = 0; index < InventoryConstants.HaveMax && remaining > 0; index++)
        {
            var absolute = InventoryConstants.InventoryStart + index;
            var slot = session.Inventory[absolute];
            if (slot.ItemId != SealStoneItem)
                continue;

            var taken = Math.Min((int)slot.Count, remaining);
            slot.Count -= (ushort)taken;
            remaining -= taken;

            if (slot.Count == 0)
                slot.Clear();

            await userNotificationService.SendStackChangeAsync(
                session, (byte)absolute, slot.ItemId, slot.Count, slot.Durability);
        }
    }

    private static string DescribeUpgradeType(byte upgradeType) => upgradeType switch
    {
        UpgradeTypeNormal => "normal",
        UpgradeTypePreview => "preview",
        _ => $"unknown({upgradeType})"
    };

    private static string DescribeUpgradeResult(byte resultCode) => resultCode switch
    {
        UpgradeFailed => "failed",
        UpgradeSucceeded => "succeeded",
        UpgradeTrading => "trading",
        UpgradeNeedCoins => "need_coins",
        UpgradeNoMatch => "no_match",
        UpgradeRental => "rental_or_sealed",
        _ => $"unknown({resultCode})"
    };

    private static string FormatUpgradeRequest(int[] itemIds, sbyte[] positions)
    {
        var count = Math.Min(itemIds.Length, positions.Length);
        if (count == 0)
            return string.Empty;

        var parts = new string[count];
        for (var index = 0; index < count; index++)
            parts[index] = $"{index}:{positions[index]}={itemIds[index]}";

        return string.Join(", ", parts);
    }
}
