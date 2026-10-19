using System.Security.Cryptography;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public readonly record struct ChatRoomPruneResult(int Rows, int Files);

public interface IChatRoomHistory
{
    Task<ChatRoomMessage> AppendTextAsync(string roomKey, int senderId, string senderName, string text);
    Task<ChatRoomMessage> AppendNoteAsync(string roomKey, int senderId, string senderName, int durationMs, byte[] file, byte[] waveform);
    Task<IReadOnlyList<ChatRoomMessage>> PageAsync(string roomKey, long beforeId, int count);
    Task<ChatRoomMessage?> FindAsync(long id);
    Task<ChatRoomMessage?> FindNoteAsync(long id);
    Task<byte[]?> ReadNoteAsync(ChatRoomMessage note);
    Task<bool> ReactAsync(long messageId, int characterId, string characterName, string emoji);
    Task<IReadOnlyList<ChatRoomReaction>> ReactionsOfAsync(IReadOnlyCollection<long> messageIds);
    Task<int> CharacterIdOfAsync(string name);
    Task<IReadOnlyList<KeptChatRoom>> KeptRoomsAsync();
    Task SaveKeptRoomAsync(KeptChatRoom room);
    Task DeleteKeptRoomAsync(string roomKey);
    Task<ChatRoomPruneResult> PruneAsync();
}

public sealed class ChatRoomHistory(
    IServiceScopeFactory scopeFactory,
    IOptions<GameServerSettings> settings,
    ILogger<ChatRoomHistory> logger,
    TimeProvider? clock = null) : IChatRoomHistory
{
    private const string NoteExtension = ".opus";
    private const int PruneBatch = 500;

    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    private ChatRoomSettings Rooms => settings.Value.ChatRooms;

    public string NotesRoot => Path.GetFullPath(Rooms.NotesPath, AppContext.BaseDirectory);

    public async Task<ChatRoomMessage> AppendTextAsync(string roomKey, int senderId, string senderName, string text)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var line = new ChatRoomMessage
        {
            RoomKey = roomKey,
            Kind = ChatRoomMessageKind.Text,
            SenderCharacterId = senderId,
            SenderName = senderName,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            Text = text,
        };
        db.ChatRoomMessages.Add(line);
        await db.SaveChangesAsync();
        return line;
    }

    public async Task<ChatRoomMessage> AppendNoteAsync(string roomKey, int senderId, string senderName, int durationMs, byte[] file, byte[] waveform)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        string relative = Path.Combine(now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"), Guid.NewGuid().ToString("N") + NoteExtension);
        string full = Path.Combine(NotesRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string partial = full + ".part";
        await File.WriteAllBytesAsync(partial, file);
        File.Move(partial, full, overwrite: true);

        var note = new ChatRoomMessage
        {
            RoomKey = roomKey,
            Kind = ChatRoomMessageKind.VoiceNote,
            SenderCharacterId = senderId,
            SenderName = senderName,
            CreatedAt = now,
            NoteDurationMs = durationMs,
            NoteSizeBytes = file.Length,
            NoteSha256 = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(),
            NoteStoragePath = relative.Replace('\\', '/'),
            NoteWaveform = waveform.Length > 0 ? waveform : null,
        };
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChatRoomMessages.Add(note);
            await db.SaveChangesAsync();
        }
        catch
        {
            TryDelete(full);
            throw;
        }
        return note;
    }

    public async Task<IReadOnlyList<ChatRoomMessage>> PageAsync(string roomKey, long beforeId, int count)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-Rooms.HistoryDays);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = db.ChatRoomMessages.AsNoTracking().Where(m => m.RoomKey == roomKey && m.CreatedAt >= since);
        if (beforeId > 0) query = query.Where(m => m.Id < beforeId);
        var page = await query.OrderByDescending(m => m.Id).Take(count).ToListAsync();
        page.Reverse();
        return page;
    }

    public async Task<ChatRoomMessage?> FindAsync(long id)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ChatRoomMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id);
    }

    public async Task<ChatRoomMessage?> FindNoteAsync(long id)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ChatRoomMessages.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == id && m.Kind == ChatRoomMessageKind.VoiceNote);
    }

    public async Task<byte[]?> ReadNoteAsync(ChatRoomMessage note)
    {
        if (note.NoteStoragePath == null) return null;
        string full = Path.Combine(NotesRoot, note.NoteStoragePath);
        if (!File.Exists(full)) return null;
        return await File.ReadAllBytesAsync(full);
    }

    public async Task<bool> ReactAsync(long messageId, int characterId, string characterName, string emoji)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var current = await db.ChatRoomReactions.SingleOrDefaultAsync(r => r.MessageId == messageId && r.CharacterId == characterId);
        if (emoji.Length == 0)
        {
            if (current == null) return false;
            db.ChatRoomReactions.Remove(current);
        }
        else if (current == null)
        {
            db.ChatRoomReactions.Add(new ChatRoomReaction
            {
                MessageId = messageId,
                CharacterId = characterId,
                CharacterName = characterName,
                Emoji = emoji,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            });
        }
        else if (current.Emoji == emoji) return false;
        else
        {
            current.Emoji = emoji;
            current.CharacterName = characterName;
            current.CreatedAt = clock.GetUtcNow().UtcDateTime;
        }
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<ChatRoomReaction>> ReactionsOfAsync(IReadOnlyCollection<long> messageIds)
    {
        if (messageIds.Count == 0) return [];
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ChatRoomReactions.AsNoTracking()
            .Where(r => messageIds.Contains(r.MessageId))
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    public async Task<int> CharacterIdOfAsync(string name)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Characters.AsNoTracking().Where(c => c.Name == name).Select(c => c.Id).FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<KeptChatRoom>> KeptRoomsAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.KeptChatRooms.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync();
    }

    public async Task SaveKeptRoomAsync(KeptChatRoom room)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var current = await db.KeptChatRooms.SingleOrDefaultAsync(r => r.RoomKey == room.RoomKey);
        if (current == null) db.KeptChatRooms.Add(room);
        else
        {
            current.Name = room.Name;
            current.Password = room.Password;
            current.MaxMembers = room.MaxMembers;
            current.OwnerId = room.OwnerId;
            current.LastUsedAt = room.LastUsedAt;
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteKeptRoomAsync(string roomKey)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var current = await db.KeptChatRooms.SingleOrDefaultAsync(r => r.RoomKey == roomKey);
        if (current == null) return;
        db.KeptChatRooms.Remove(current);
        await db.SaveChangesAsync();
    }

    public async Task<ChatRoomPruneResult> PruneAsync()
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-Rooms.HistoryDays);
        int rows = 0, files = 0;
        while (true)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var batch = await db.ChatRoomMessages.Where(m => m.CreatedAt < cutoff).OrderBy(m => m.Id).Take(PruneBatch).ToListAsync();
            if (batch.Count == 0) break;
            foreach (var message in batch)
                if (message.NoteStoragePath != null && TryDelete(Path.Combine(NotesRoot, message.NoteStoragePath)))
                    files++;
            var ids = batch.Select(m => m.Id).ToList();
            db.ChatRoomReactions.RemoveRange(await db.ChatRoomReactions.Where(r => ids.Contains(r.MessageId)).ToListAsync());
            db.ChatRoomMessages.RemoveRange(batch);
            await db.SaveChangesAsync();
            rows += batch.Count;
            if (batch.Count < PruneBatch) break;
        }
        return new ChatRoomPruneResult(rows, files);
    }

    private bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Voice note file {Path} could not be deleted", path);
            return false;
        }
    }
}
