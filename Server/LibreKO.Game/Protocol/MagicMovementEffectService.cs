using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IMagicMovementEffectService
{
    Task ExecuteAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data);
}

public class MagicMovementEffectService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IWorldMovementService worldMovementService,
    ILogger<MagicMovementEffectService> logger) : IMagicMovementEffectService
{
    public async Task ExecuteAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType8Table, magic, skillId, out var type8Data))
        {
            await caster.Client.SendPacket(MagicProcessPacketWriter.CreateFail(skillId, caster.CharacterId));
            return;
        }

        var warpType = (MagicWarpType)type8Data.WarpType;
        var moved = warpType switch
        {
            MagicWarpType.BindPoint => await WarpToBindPointAsync(caster, magic),
            MagicWarpType.SummonInZone => await SummonToCasterAsync(caster, targetId),
            MagicWarpType.MoveToTarget => await MoveToTargetAsync(caster, magic, targetId),
            MagicWarpType.Blink => await BlinkAsync(caster, type8Data, data),
            _ => Unhandled(warpType, skillId, caster),
        };

        if (!moved)
        {
            await caster.Client.SendPacket(MagicProcessPacketWriter.CreateFail(skillId, caster.CharacterId));
            return;
        }

        await sessionManager.Regions.SendToRegion(
            caster,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                (short)caster.CharacterId,
                targetId,
                data),
            excludeSender: false);
    }

    private bool Unhandled(MagicWarpType warpType, int skillId, UserSession caster)
    {
        logger.LogDebug(
            "Warp type {WarpType} has no destination rule: skill={SkillId} caster={Name}",
            warpType, skillId, caster.Name);
        return true;
    }

    private async Task<bool> WarpToBindPointAsync(UserSession caster, MagicData magic)
    {
        if ((SkillMoral)magic.Moral != SkillMoral.PartyAll)
            return await SendHomeAsync(caster);

        var party = caster.IsInParty ? sessionManager.Parties.GetParty(caster.PartyIndex) : null;
        if (party == null)
            return await SendHomeAsync(caster);

        var warped = false;
        foreach (var memberId in party.MemberIds)
        {
            if (memberId < 0)
                continue;

            var member = sessionManager.GetByCharacterId(memberId);
            if (member == null || member.Hp <= 0)
                continue;

            warped |= await SendHomeAsync(member);
        }

        return warped;
    }

    private async Task<bool> SendHomeAsync(UserSession caster)
    {
        var bindEvent = caster.Quest.BindPoint > 0
            ? sessionManager.Maps?.GetObjectEvent(caster.ZoneId, caster.Quest.BindPoint)
            : null;

        if (bindEvent is { Life: ObjectEventAlive })
        {
            await worldMovementService.WarpAsync(caster, ToTenths(bindEvent.PosX), ToTenths(bindEvent.PosZ));
            return true;
        }

        var startPosition = gameDataService.GetStartPosition(caster.ZoneId);
        if (startPosition == null)
            return false;

        var (x, z) = startPosition.RandomSpawn(caster.Nation);
        await worldMovementService.WarpAsync(caster, ToTenths(x), ToTenths(z));
        return true;
    }

    private async Task<bool> SummonToCasterAsync(UserSession caster, int targetId)
    {
        var target = sessionManager.GetByCharacterId(targetId);
        if (target == null
            || target.CharacterId == caster.CharacterId
            || target.Hp <= 0
            || target.ZoneId != caster.ZoneId)
            return false;

        await worldMovementService.WarpAsync(target, (ushort)caster.GetPosX, (ushort)caster.GetPosZ);
        return true;
    }

    private async Task<bool> MoveToTargetAsync(UserSession caster, MagicData magic, int targetId)
    {
        var target = sessionManager.GetByCharacterId(targetId);
        if (target == null
            || target.CharacterId == caster.CharacterId
            || target.Hp <= 0
            || target.ZoneId != caster.ZoneId)
            return false;

        if ((SkillMoral)magic.Moral < SkillMoral.Enemy && PvpRules.IsEnemy(caster, target))
            return false;

        await worldMovementService.WarpAsync(caster, (ushort)target.GetPosX, (ushort)target.GetPosZ);
        return true;
    }

    private async Task<bool> BlinkAsync(UserSession caster, MagicType8Data type8Data, int[] data)
    {
        if (data.Length <= BlinkZSlot)
            return false;

        var tenthsX = unchecked((ushort)data[BlinkXSlot]);
        var tenthsZ = unchecked((ushort)data[BlinkZSlot]);
        float x = tenthsX / TenthsPerMetre, z = tenthsZ / TenthsPerMetre;
        float dx = x - caster.X, dz = z - caster.Z;
        float reach = type8Data.Radius + BlinkReachSlack;
        if (dx * dx + dz * dz > reach * reach)
            return false;

        if (sessionManager.Maps?.IsValidPosition(caster.ZoneId, x, z) == false)
            return false;

        await worldMovementService.WarpAsync(caster, tenthsX, tenthsZ);
        return true;
    }

    private const int BlinkXSlot = 0;
    private const int BlinkZSlot = 2;
    private const float BlinkReachSlack = 2f;
    private const float TenthsPerMetre = 10f;

    private const byte ObjectEventAlive = 1;

    private static ushort ToTenths(float value) => (ushort)(value * 10);

    private static ushort ToTenths(short value) => (ushort)(value * 10);
}
