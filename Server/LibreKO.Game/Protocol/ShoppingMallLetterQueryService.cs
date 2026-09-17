using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IShoppingMallLetterQueryService
{
    Task SendUnreadAsync(UserSession session);
    Task HandleListAsync(UserSession session, bool history);
    Task HandleReadAsync(UserSession session, Packet packet);
}

public class ShoppingMallLetterQueryService(IServiceScopeFactory scopeFactory,
    ILogger<ShoppingMallLetterQueryService> logger) : IShoppingMallLetterQueryService
{
    private const int LetterLifetimeDays = 30;
    private const byte LetterTypePlain = 1;
    private const byte LetterStatusRead = 2;

    public async Task SendUnreadAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var count = await db.MailBoxes
            .CountAsync(mail => mail.RecipientId == session.Name && mail.Status == 1 && !mail.Deleted);
        logger.LogDebug("{Name} has {Count} unread mail", session.Name, count);

        await session.Client.SendPacket(ShoppingMallPacketWriter.UnreadCount(
            ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterUnread,
            (byte)Math.Min(count, byte.MaxValue)));
    }

    public async Task HandleListAsync(UserSession session, bool history)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var maxResults = history ? 20 : 12;
        var status = history ? (byte)2 : (byte)1;

        var letters = await db.MailBoxes
            .Where(mail => mail.RecipientId == session.Name && mail.Status == status && !mail.Deleted)
            .OrderByDescending(mail => mail.SendDate)
            .Take(maxResults)
            .ToListAsync();

        var rows = letters.Select(letter =>
        {
            var date = history && letter.ReadDate.HasValue ? letter.ReadDate.Value : letter.SendDate;
            var daysRemaining = Math.Max(0, LetterLifetimeDays - (int)(DateTime.UtcNow - letter.SendDate).TotalDays);
            return new ShoppingMallPacketWriter.Letter(
                letter.LetterId, letter.Status, letter.Subject, letter.SenderId, letter.Type,
                letter.ItemId, (ushort)letter.Count, letter.Coins,
                date.Year * 10000 + date.Month * 100 + date.Day,
                (ushort)daysRemaining);
        }).ToList();

        await session.Client.SendPacket(ShoppingMallPacketWriter.LetterList(
            ShoppingMallLetterProtocol.StoreLetter,
            history ? ShoppingMallLetterProtocol.LetterHistory : ShoppingMallLetterProtocol.LetterList,
            rows));
    }

    public async Task HandleReadAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4)
            return;

        var letterId = packet.ReadInt();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var letter = await db.MailBoxes
            .OrderBy(mail => mail.LetterId)
            .FirstOrDefaultAsync(mail => mail.LetterId == letterId && mail.RecipientId == session.Name && !mail.Deleted);

        if (letter == null)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterRead,
                ShoppingMallPacketWriter.Failed));
            return;
        }

        letter.ReadDate ??= DateTime.UtcNow;
        if (letter.Type == LetterTypePlain)
            letter.Status = LetterStatusRead;

        await db.SaveChangesAsync();

        await session.Client.SendPacket(ShoppingMallPacketWriter.LetterBody(
            ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterRead,
            letter.LetterId, letter.Message));
    }
}
