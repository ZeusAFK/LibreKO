using System.Text;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IBeautyShopPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class BeautyShopPacketCoordinator(
    IServiceScopeFactory scopeFactory,
    SessionManager sessionManager,
    IWorldPacketCoordinator worldPacketCoordinator,
    ILogger<BeautyShopPacketCoordinator> logger) : IBeautyShopPacketCoordinator
{
    private const int HeaderLength = sizeof(byte) + sizeof(byte);
    private const int AppearanceLength = sizeof(byte) + sizeof(int);

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        if (packet.RemainingBytes < HeaderLength)
        {
            await RefuseAsync(client);
            return;
        }

        var subOpcode = packet.ReadByte();
        var nameLength = packet.ReadByte();
        if (packet.RemainingBytes != nameLength + AppearanceLength)
        {
            await RefuseAsync(client);
            return;
        }

        var characterName = Encoding.ASCII.GetString(packet.ReadBytes(nameLength));
        var face = packet.ReadByte();
        var hair = packet.ReadInt();

        if (!string.Equals(characterName, session.Name, StringComparison.OrdinalIgnoreCase)
            || session.Hp <= 0 || session.Trade.IsTrading || session.Trade.IsMerchanting || session.IsGathering)
        {
            await RefuseAsync(client);
            return;
        }

        Packet result;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var preGameService = scope.ServiceProvider.GetRequiredService<IPreGameService>();
            result = await preGameService.ChangeHairAsync(session.AccountId, subOpcode, session.Name, face, hair);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not save the beauty shop appearance of {Name}", session.Name);
            await RefuseAsync(client);
            return;
        }

        await client.SendPacket(result);
        if (result.GetData() is not [PreGamePacketWriter.ChangeHairSucceeded] || sessionManager.GetByClientId(client.Id) != session)
            return;

        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.Out);
        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.In);
    }

    private static Task RefuseAsync(IClient client) =>
        client.SendPacket(PreGamePacketWriter.ChangeHairResult(PreGamePacketWriter.ChangeHairFailed));
}
