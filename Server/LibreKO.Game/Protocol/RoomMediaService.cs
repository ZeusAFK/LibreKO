using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Protocol;

public interface IRoomMediaService
{
    Task HandleAsync(IClient client, Packet packet);
    Task WhisperSentAsync(UserSession from, UserSession to, string text);
}

public sealed class RoomMediaService : IRoomMediaService
{
    public const int NoteLengthSlackMs = 500;
    public const int DecisecondsPerSecond = 10;
    public const int ReactionMinIntervalMs = 250;
    public const int PartnerNameMaxLength = 20;

    private sealed class Upload(uint id, string roomKey, int partnerId, int size, byte[] waveform)
    {
        public uint Id { get; } = id;
        public string RoomKey { get; } = roomKey;
        public int PartnerId { get; } = partnerId;
        public byte[] Waveform { get; } = waveform;
        public byte[] Data { get; } = new byte[size];
        public int Received { get; set; }
        public ushort NextIndex { get; set; }
    }

    private sealed class NoteQuota
    {
        public DateTime Last { get; set; } = DateTime.MinValue;
        public DateOnly Day { get; set; }
        public int Count { get; set; }
    }

    private readonly SessionManager sessions;
    private readonly INationChatService rooms;
    private readonly IChatRoomHistory history;
    private readonly VoiceRoster roster;
    private readonly IVoiceRelay relay;
    private readonly IOptions<GameServerSettings> settings;
    private readonly ILogger<RoomMediaService> logger;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly Dictionary<int, Upload> uploads = [];
    private readonly Dictionary<int, NoteQuota> quotas = [];
    private readonly HashSet<(string RoomKey, int CharacterId)> silenced = [];
    private readonly Dictionary<int, long> lastReaction = [];
    private uint nextUpload;

    public RoomMediaService(
        SessionManager sessions,
        INationChatService rooms,
        IChatRoomHistory history,
        VoiceRoster roster,
        IVoiceRelay relay,
        IOptions<GameServerSettings> settings,
        ILogger<RoomMediaService> logger,
        TimeProvider? clock = null)
    {
        this.sessions = sessions;
        this.rooms = rooms;
        this.history = history;
        this.roster = roster;
        this.relay = relay;
        this.settings = settings;
        this.logger = logger;
        this.clock = clock ?? TimeProvider.System;
        this.rooms.SeatReleased += OnSeatReleased;
    }

    private ChatRoomSettings Settings => settings.Value.ChatRooms;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessions.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;
        byte sub = packet.ReadByte();
        switch (sub)
        {
            case RoomMediaPacketWriter.History: await SendHistoryAsync(session, packet); break;
            case RoomMediaPacketWriter.React: await ReactAsync(session, packet); break;
            case RoomMediaPacketWriter.Kept: await KeepAsync(session, packet); break;
            case RoomMediaPacketWriter.DirectHistory: await SendDirectHistoryAsync(session, packet); break;
            case RoomMediaPacketWriter.NoteBegin: await BeginNoteAsync(session, packet); break;
            case RoomMediaPacketWriter.NoteChunk: await TakeChunkAsync(session, packet); break;
            case RoomMediaPacketWriter.NoteEnd: await EndNoteAsync(session, packet); break;
            case RoomMediaPacketWriter.NoteFetch: await FetchNoteAsync(session, packet); break;
            case RoomMediaPacketWriter.VoiceJoin: await JoinVoiceAsync(session); break;
            case RoomMediaPacketWriter.VoiceLeave: await LeaveVoiceAsync(session); break;
            case RoomMediaPacketWriter.VoiceState: await ChangeVoiceStateAsync(session, packet); break;
            case RoomMediaPacketWriter.VoiceFrame: await RelayTcpFrameAsync(session, packet); break;
            case RoomMediaPacketWriter.VoiceTransport: UseTransport(session, packet); break;
            case RoomMediaPacketWriter.VoiceRoster: await SendRosterAsync(session); break;
            case RoomMediaPacketWriter.VoiceSilence: await SilenceAsync(session, packet); break;
            default: logger.LogDebug("Unhandled room media sub {Sub} from {Name}", sub, session.Name); break;
        }
    }

    private static bool Silenced(UserSession session) =>
        !session.IsGM && (session.IsMuted || session.ZoneId == (byte)ZoneId.Prison);

    private async Task SendHistoryAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 8) return;
        long before = packet.ReadLong();
        if (!rooms.TryGetSeat(session.CharacterId, out var seat))
        {
            await session.Client.SendPacket(RoomMediaPacketWriter.HistoryPage([], false));
            return;
        }
        var (page, more) = await PageOfAsync(seat.Key, before);
        await session.Client.SendPacket(RoomMediaPacketWriter.HistoryPage(page, more));
        await SendReactionsAsync(session, page);
    }

    private async Task SendDirectHistoryAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2) return;
        string partnerName = packet.ReadString();
        if (packet.RemainingBytes < 8 || partnerName.Length is 0 or > PartnerNameMaxLength) return;
        long before = packet.ReadLong();
        int partnerId = sessions.GetByName(partnerName)?.CharacterId ?? await history.CharacterIdOfAsync(partnerName);
        if (partnerId == 0 || partnerId == session.CharacterId)
        {
            await session.Client.SendPacket(RoomMediaPacketWriter.DirectPage(partnerName, [], false));
            return;
        }
        var (page, more) = await PageOfAsync(ChatRoomKeys.Direct(session.CharacterId, partnerId), before);
        await session.Client.SendPacket(RoomMediaPacketWriter.DirectPage(partnerName, page, more));
        await SendReactionsAsync(session, page);
    }

    private async Task<(IReadOnlyList<ChatRoomMessage> Page, bool More)> PageOfAsync(string key, long before)
    {
        var rows = await history.PageAsync(key, before, RoomMediaPacketWriter.HistoryPageMax + 1);
        bool more = rows.Count > RoomMediaPacketWriter.HistoryPageMax;
        return (more ? rows.Skip(rows.Count - RoomMediaPacketWriter.HistoryPageMax).ToList() : rows, more);
    }

    private async Task SendReactionsAsync(UserSession session, IReadOnlyList<ChatRoomMessage> page)
    {
        var reactions = await history.ReactionsOfAsync(page.Select(row => row.Id).ToList());
        if (reactions.Count > 0)
            await session.Client.SendPacket(RoomMediaPacketWriter.ReactionList(reactions));
    }

    public async Task WhisperSentAsync(UserSession from, UserSession to, string text)
    {
        if (from.IsBot || to.IsBot) return;
        ChatRoomMessage line;
        try
        {
            line = await history.AppendTextAsync(ChatRoomKeys.Direct(from.CharacterId, to.CharacterId), from.CharacterId, from.Name, text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Whisper from {Name} was not stored", from.Name);
            return;
        }
        await SendDirectAsync(from, to, line);
    }

    private async Task SendDirectAsync(UserSession from, UserSession to, ChatRoomMessage row)
    {
        await SendToAsync([from.CharacterId], RoomMediaPacketWriter.DirectRow(to.Name, row));
        await SendToAsync([to.CharacterId], RoomMediaPacketWriter.DirectRow(from.Name, row));
    }

    private async Task ReactAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 10) return;
        long messageId = packet.ReadLong();
        string emoji = packet.ReadString().Trim();
        if (emoji.Length > ChatRoomReaction.EmojiMaxLength || emoji.Any(char.IsControl)) return;
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        lock (gate)
        {
            if (lastReaction.TryGetValue(session.CharacterId, out long last) && now - last < ReactionMinIntervalMs) return;
            lastReaction[session.CharacterId] = now;
        }
        var message = await history.FindAsync(messageId);
        if (message == null) return;
        IEnumerable<int> audience;
        int partner = ChatRoomKeys.PartnerOf(message.RoomKey, session.CharacterId);
        if (partner != 0) audience = [session.CharacterId, partner];
        else if (rooms.TryGetSeat(session.CharacterId, out var seat) && seat.Key == message.RoomKey) audience = seat.Members;
        else return;
        if (!await history.ReactAsync(messageId, session.CharacterId, session.Name, emoji)) return;
        await SendToAsync(audience, RoomMediaPacketWriter.Reaction(messageId, session.CharacterId, session.Name, emoji));
    }

    private async Task KeepAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1) return;
        await rooms.SetKeptAsync(session, packet.ReadByte() != 0);
    }

    private byte DirectRefusal(UserSession session, string partnerName, out UserSession? partner)
    {
        partner = partnerName.Length is 0 or > PartnerNameMaxLength ? null : sessions.GetByName(partnerName);
        if (session.BlockPrivateChat) return RoomMediaResults.Blocked;
        if (partner == null || partner == session) return RoomMediaResults.PartnerAway;
        if (partner.BlockPrivateChat) return RoomMediaResults.Blocked;
        return RoomMediaResults.Ok;
    }

    private (byte Result, string Key, int PartnerId) NoteTarget(UserSession session, string partnerName)
    {
        if (partnerName.Length > 0)
        {
            byte refusal = DirectRefusal(session, partnerName, out var partner);
            return refusal == RoomMediaResults.Ok
                ? (refusal, ChatRoomKeys.Direct(session.CharacterId, partner!.CharacterId), partner.CharacterId)
                : (refusal, "", 0);
        }
        return rooms.TryGetSeat(session.CharacterId, out var seat)
            ? (RoomMediaResults.Ok, seat.Key, 0)
            : (RoomMediaResults.NotInRoom, "", 0);
    }

    private async Task BeginNoteAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 6) return;
        int durationDs = packet.ReadUShort();
        int size = (int)packet.ReadUInt();
        byte[] waveform = [];
        if (packet.RemainingBytes >= 1)
        {
            int samples = packet.ReadByte();
            if (samples > ChatRoomMessage.WaveformMaxSamples || samples > packet.RemainingBytes) return;
            waveform = packet.ReadBytes(samples);
        }
        string partnerName = packet.RemainingBytes >= 2 ? packet.ReadString() : "";
        uint uploadId = 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var (result, key, partnerId) = NoteTarget(session, partnerName);
        if (result == RoomMediaResults.Ok)
        {
            if (Silenced(session)) result = RoomMediaResults.Silenced;
            else if (durationDs > Settings.NoteMaxSeconds * DecisecondsPerSecond) result = RoomMediaResults.TooLong;
            else if (size <= 0 || size > Settings.NoteMaxBytes) result = RoomMediaResults.TooBig;
            else
            {
                lock (gate)
                {
                    var quota = QuotaOf(session.CharacterId, now);
                    if ((now - quota.Last).TotalSeconds < Settings.NoteMinIntervalSeconds) result = RoomMediaResults.TooSoon;
                    else if (quota.Count >= Settings.NotesPerDay) result = RoomMediaResults.DailyLimit;
                    else
                    {
                        uploadId = ++nextUpload;
                        uploads[session.CharacterId] = new Upload(uploadId, key, partnerId, size, waveform);
                    }
                }
            }
        }
        await session.Client.SendPacket(result == RoomMediaResults.Ok
            ? RoomMediaPacketWriter.UploadStarted(uploadId)
            : RoomMediaPacketWriter.Result(RoomMediaPacketWriter.NoteBegin, result));
    }

    private async Task TakeChunkAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 8) return;
        uint id = packet.ReadUInt();
        ushort index = packet.ReadUShort();
        int length = packet.ReadUShort();
        bool bad;
        lock (gate)
        {
            bad = !uploads.TryGetValue(session.CharacterId, out var upload) || upload.Id != id
                || index != upload.NextIndex || length > RoomMediaPacketWriter.ChunkBytes
                || length > packet.RemainingBytes || upload.Received + length > upload.Data.Length;
            if (!bad)
            {
                packet.ReadBytes(length).CopyTo(upload!.Data, upload.Received);
                upload.Received += length;
                upload.NextIndex++;
            }
            else uploads.Remove(session.CharacterId);
        }
        if (bad)
            await session.Client.SendPacket(RoomMediaPacketWriter.Result(RoomMediaPacketWriter.NoteEnd, RoomMediaResults.BadUpload));
    }

    private async Task EndNoteAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4) return;
        uint id = packet.ReadUInt();
        Upload? upload;
        lock (gate)
        {
            if (!uploads.Remove(session.CharacterId, out upload) || upload.Id != id) upload = null;
        }
        OggOpusInfo info = default;
        UserSession? partner = null;
        byte result = upload == null || upload.Received != upload.Data.Length
            ? RoomMediaResults.BadUpload
            : UploadTargetRefusal(session, upload, out partner);
        if (result == RoomMediaResults.Ok)
        {
            if (!OggOpusInfo.TryRead(upload!.Data, out info) || info.Channels != 1) result = RoomMediaResults.BadUpload;
            else if (info.DurationMs > Settings.NoteMaxSeconds * 1000 + NoteLengthSlackMs) result = RoomMediaResults.TooLong;
        }
        if (result != RoomMediaResults.Ok)
        {
            await session.Client.SendPacket(RoomMediaPacketWriter.Result(RoomMediaPacketWriter.NoteEnd, result));
            return;
        }

        ChatRoomMessage note;
        try
        {
            note = await history.AppendNoteAsync(upload!.RoomKey, session.CharacterId, session.Name, info.DurationMs, upload.Data, upload.Waveform);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Voice note from {Name} could not be stored", session.Name);
            await session.Client.SendPacket(RoomMediaPacketWriter.Result(RoomMediaPacketWriter.NoteEnd, RoomMediaResults.Failed));
            return;
        }
        lock (gate)
        {
            var quota = QuotaOf(session.CharacterId, note.CreatedAt);
            quota.Last = note.CreatedAt;
            quota.Count++;
        }
        await session.Client.SendPacket(RoomMediaPacketWriter.Result(RoomMediaPacketWriter.NoteEnd, RoomMediaResults.Ok));
        if (partner != null)
            await SendDirectAsync(session, partner, note);
        else if (rooms.TryGetSeat(session.CharacterId, out var room))
            await SendToAsync(room.Members, RoomMediaPacketWriter.PostedRow(note));
    }

    private byte UploadTargetRefusal(UserSession session, Upload upload, out UserSession? partner)
    {
        partner = null;
        if (upload.PartnerId == 0)
            return rooms.TryGetSeat(session.CharacterId, out var seat) && seat.Key == upload.RoomKey
                ? RoomMediaResults.Ok
                : RoomMediaResults.NotInRoom;
        var online = sessions.GetByCharacterId(upload.PartnerId);
        return online == null ? RoomMediaResults.PartnerAway : DirectRefusal(session, online.Name, out partner);
    }

    private bool CanRead(UserSession session, string roomKey) =>
        ChatRoomKeys.PartnerOf(roomKey, session.CharacterId) != 0
        || rooms.TryGetSeat(session.CharacterId, out var seat) && seat.Key == roomKey;

    private async Task FetchNoteAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 8) return;
        long noteId = packet.ReadLong();
        var note = await history.FindNoteAsync(noteId);
        byte[]? file = null;
        if (note != null && CanRead(session, note.RoomKey))
            file = await history.ReadNoteAsync(note);
        if (file == null)
        {
            await session.Client.SendPacket(RoomMediaPacketWriter.NoteFetchRefused(noteId, RoomMediaResults.NotFound));
            return;
        }
        await session.Client.SendPacket(RoomMediaPacketWriter.NoteHeader(noteId, file.Length));
        ushort index = 0;
        for (int offset = 0; offset < file.Length; offset += RoomMediaPacketWriter.ChunkBytes, index++)
        {
            int length = Math.Min(RoomMediaPacketWriter.ChunkBytes, file.Length - offset);
            await session.Client.SendPacket(RoomMediaPacketWriter.NoteChunkOf(noteId, index, file, offset, length));
        }
    }

    private async Task JoinVoiceAsync(UserSession session)
    {
        byte result;
        VoiceMember? member = null;
        List<VoiceMember> others = [];
        if (!relay.Enabled) result = RoomMediaResults.VoiceOff;
        else if (!rooms.TryGetSeat(session.CharacterId, out var seat)) result = RoomMediaResults.NotInRoom;
        else if (seat.Public) result = RoomMediaResults.PublicChannel;
        else
        {
            await LeaveOtherCallAsync(session.CharacterId, seat.Key);
            member = roster.Join(session.CharacterId, seat.Key, !Silenced(session), Settings.Voice.MaxVoicePerRoom, out others);
            result = member == null ? RoomMediaResults.VoiceFull : RoomMediaResults.Ok;
            if (member != null)
                lock (gate) member.Silenced = silenced.Contains((seat.Key, session.CharacterId));
        }
        if (member == null)
        {
            await session.Client.SendPacket(RoomMediaPacketWriter.Result(RoomMediaPacketWriter.VoiceJoin, result));
            return;
        }
        var peers = others.Select(o => new RoomMediaPacketWriter.VoicePeer(o.CharacterId, o.WireFlags)).ToList();
        await session.Client.SendPacket(RoomMediaPacketWriter.Joined(relay.UdpPort, member.SessionId, member.Key, member.WireFlags, peers));
        await SendToAsync(RoomAudience(member.RoomKey, session.CharacterId),
            RoomMediaPacketWriter.MemberEvent(session.CharacterId, RoomMediaPacketWriter.MemberJoined, member.WireFlags));
    }

    private async Task LeaveOtherCallAsync(int characterId, string roomKey)
    {
        if (roster.Get(characterId) is not { } current || current.RoomKey == roomKey) return;
        if (roster.Leave(characterId, out _) is not { } left) return;
        await SendToAsync(RoomAudience(left.RoomKey, characterId),
            RoomMediaPacketWriter.MemberEvent(characterId, RoomMediaPacketWriter.MemberLeft, 0));
    }

    private async Task SendRosterAsync(UserSession session)
    {
        var peers = rooms.TryGetSeat(session.CharacterId, out var seat) && !seat.Public
            ? roster.MembersOf(seat.Key).Select(m => new RoomMediaPacketWriter.VoicePeer(m.CharacterId, m.WireFlags)).ToList()
            : [];
        await session.Client.SendPacket(RoomMediaPacketWriter.Roster(peers));
    }

    private async Task SilenceAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 5) return;
        int targetId = packet.ReadInt();
        bool silence = packet.ReadByte() != 0;
        if (!rooms.TryGetSeat(session.CharacterId, out var seat) || seat.Public || seat.OwnerId != session.CharacterId)
        {
            await session.Client.SendPacket(RoomMediaPacketWriter.Result(RoomMediaPacketWriter.VoiceSilence, RoomMediaResults.NotLeader));
            return;
        }
        if (targetId == session.CharacterId || !seat.Members.Contains(targetId)) return;
        bool changed;
        lock (gate)
            changed = silence ? silenced.Add((seat.Key, targetId)) : silenced.Remove((seat.Key, targetId));
        if (!changed) return;
        string targetName = sessions.GetByCharacterId(targetId)?.Name ?? "";
        await SendToAsync(rooms.MembersOf(seat.Key), RoomMediaPacketWriter.SilenceNotice(targetId, silence, session.Name, targetName));
        var target = roster.Get(targetId);
        if (target == null || target.RoomKey != seat.Key) return;
        target.Silenced = silence;
        await SendToAsync(rooms.MembersOf(seat.Key),
            RoomMediaPacketWriter.MemberEvent(targetId, RoomMediaPacketWriter.MemberFlags, target.WireFlags));
    }

    private IEnumerable<int> RoomAudience(string roomKey, int except) =>
        rooms.MembersOf(roomKey).Where(id => id != except);

    private async Task LeaveVoiceAsync(UserSession session)
    {
        var member = roster.Leave(session.CharacterId, out _);
        await session.Client.SendPacket(RoomMediaPacketWriter.Result(RoomMediaPacketWriter.VoiceLeave,
            member == null ? RoomMediaResults.NotInRoom : RoomMediaResults.Ok));
        if (member != null)
            await SendToAsync(RoomAudience(member.RoomKey, session.CharacterId),
                RoomMediaPacketWriter.MemberEvent(session.CharacterId, RoomMediaPacketWriter.MemberLeft, 0));
    }

    private async Task ChangeVoiceStateAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1) return;
        byte flags = (byte)(packet.ReadByte() & VoiceFlags.ClientSettable);
        var member = roster.Get(session.CharacterId);
        if (member == null || member.Flags == flags) return;
        member.Flags = flags;
        await SendToAsync(RoomAudience(member.RoomKey, session.CharacterId),
            RoomMediaPacketWriter.MemberEvent(session.CharacterId, RoomMediaPacketWriter.MemberFlags, member.WireFlags));
    }

    private async Task RelayTcpFrameAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4) return;
        ushort seq = packet.ReadUShort();
        int length = packet.ReadUShort();
        if (length > packet.RemainingBytes) return;
        var member = roster.Get(session.CharacterId);
        if (member == null) return;
        var opus = packet.ReadBytes(length);
        if (roster.AllowFrame(member, opus.Length, clock.GetUtcNow().ToUnixTimeMilliseconds()))
            await relay.ForwardAsync(member, seq, opus);
    }

    private void UseTransport(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1) return;
        bool tcp = packet.ReadByte() != 0;
        if (roster.Get(session.CharacterId) is { } member)
            member.Tcp = tcp;
    }

    private void OnSeatReleased(int characterId, string roomKey)
    {
        lock (gate)
        {
            if (uploads.TryGetValue(characterId, out var upload) && upload.PartnerId == 0) uploads.Remove(characterId);
            silenced.Remove((roomKey, characterId));
        }
        var member = roster.Leave(characterId, out _);
        if (member == null) return;
        _ = SendToAsync(RoomAudience(roomKey, characterId), RoomMediaPacketWriter.MemberEvent(characterId, RoomMediaPacketWriter.MemberLeft, 0));
    }

    private NoteQuota QuotaOf(int characterId, DateTime now)
    {
        if (!quotas.TryGetValue(characterId, out var quota))
            quotas[characterId] = quota = new NoteQuota();
        var today = DateOnly.FromDateTime(now);
        if (quota.Day != today)
        {
            quota.Day = today;
            quota.Count = 0;
        }
        return quota;
    }

    private async Task SendToAsync(IEnumerable<int> characterIds, Packet packet)
    {
        foreach (var id in characterIds)
        {
            var member = sessions.GetByCharacterId(id);
            if (member == null) continue;
            try { await member.Client.SendPacket(packet); }
            catch (Exception ex) { logger.LogDebug(ex, "Room media send failed for {Id}", id); }
        }
    }
}
