using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface IExpSealService
{
    Task HandleAsync(IClient client, Packet packet);
    Task<bool> TrySealAsync(UserSession session, long exp);
}

public sealed class ExpSealService : IExpSealService
{
    private readonly SessionManager sessions;
    private readonly IGameDataService gameData;
    private readonly IMagicItemUsageService itemUsage;
    private readonly IItemGrantService itemGrants;
    private readonly IPlayerProgressionService progression;

    public ExpSealService(
        SessionManager sessions,
        IGameDataService gameData,
        IMagicItemUsageService itemUsage,
        IItemGrantService itemGrants,
        IPlayerProgressionService progression,
        IZoneTransitionService? zones = null)
    {
        this.sessions = sessions;
        this.gameData = gameData;
        this.itemUsage = itemUsage;
        this.itemGrants = itemGrants;
        this.progression = progression;
        if (zones != null)
            zones.ZoneAbilitySent += SendStateAsync;
    }

    private async Task SendStateAsync(UserSession session)
    {
        if (session.ExpSealOn)
            await session.Client.SendPacket(ExpSealPacketWriter.Toggle(ExpSealRules.SubOn, ExpSealRules.Succeeded));
    }

    private const int StoreBytes = sizeof(int) + 1 + sizeof(long);
    private const int UseBytes = sizeof(int) + 1;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessions.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        switch (packet.ReadByte())
        {
            case ExpSealRules.SubOn:
                session.ExpSealOn = true;
                await client.SendPacket(ExpSealPacketWriter.Toggle(ExpSealRules.SubOn, ExpSealRules.Succeeded));
                break;
            case ExpSealRules.SubOff:
                session.ExpSealOn = false;
                await client.SendPacket(ExpSealPacketWriter.Toggle(ExpSealRules.SubOff, ExpSealRules.Succeeded));
                break;
            case ExpSealRules.SubStore when packet.RemainingBytes >= StoreBytes:
                var jar = packet.ReadInt();
                packet.ReadByte();
                await client.SendPacket(await StoreAsync(session, jar, packet.ReadLong()));
                break;
            case ExpSealRules.SubUseJar when packet.RemainingBytes >= UseBytes:
                var filled = packet.ReadInt();
                packet.ReadByte();
                await client.SendPacket(await UseJarAsync(session, filled));
                break;
        }
    }

    public async Task<bool> TrySealAsync(UserSession session, long exp)
    {
        if (!session.ExpSealOn || exp <= 0)
            return false;
        session.SealedExperience += exp;
        await session.Client.SendPacket(ExpSealPacketWriter.Total(session.SealedExperience));
        return true;
    }

    private async Task<Packet> StoreAsync(UserSession session, int jar, long amount)
    {
        if (jar != ExpSealRules.EmptyJar || !ExpSealRules.IsOfferedAmount(amount))
            return ExpSealPacketWriter.Code(ExpSealRules.SubStore, ExpSealRules.UseFailed);
        if (session.ExpSealOn)
            return ExpSealPacketWriter.Code(ExpSealRules.SubStore, ExpSealRules.StoreWhileOn);
        if (session.SealedExperience < amount)
            return ExpSealPacketWriter.Code(ExpSealRules.SubStore, ExpSealRules.StoreTooLittle);
        if (gameData.GetItem(ExpSealRules.LargeJar) is not { } filledJar || !await itemUsage.TryConsumeItemAsync(session, jar))
            return ExpSealPacketWriter.Code(ExpSealRules.SubStore, ExpSealRules.UseFailed);

        var placed = await itemGrants.GrantAsync(session, filledJar, ExpSealRules.FilledJarsFor(amount));
        session.SealedExperience -= placed * ExpSealRules.FillStep;
        return ExpSealPacketWriter.Stored(session.SealedExperience);
    }

    private async Task<Packet> UseJarAsync(UserSession session, int jar)
    {
        if (session.ExpSealOn)
            return ExpSealPacketWriter.Code(ExpSealRules.SubUseJar, ExpSealRules.UseWhileOn);
        var exp = ExpSealRules.ExpOfJar(jar);
        if (exp == 0 || !await itemUsage.TryConsumeItemAsync(session, jar))
            return ExpSealPacketWriter.Code(ExpSealRules.SubUseJar, ExpSealRules.UseFailed);
        await progression.ChangeExperienceAsync(session, exp);
        return ExpSealPacketWriter.Code(ExpSealRules.SubUseJar, ExpSealRules.Succeeded);
    }
}
