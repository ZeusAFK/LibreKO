using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using static LibreKO.Game.Protocol.Writers.ClanBoardPacketWriter;

namespace LibreKO.Game.World;

public interface IClanBoardService
{
    Task HandleBoardAsync(UserSession session, Packet packet);
    Task HandleApplyAsync(UserSession session, Packet packet);
    Task HandleAdvertAsync(UserSession session, Packet packet);
    Task HandlePromoteAsync(UserSession session);
    Task HandleWishAsync(UserSession session, Packet packet);
}

public sealed class ClanBoardService(
    SessionManager sessionManager,
    IKnightsMembershipPacketService membership,
    TimeProvider? clock = null) : IClanBoardService
{
    public const int RowsPerPage = 8;
    public const int MaxMessageBytes = 139;
    public const int MaxRequests = 9;
    public const int MinAge = 1;
    public const int MaxAge = 99;
    public static readonly TimeSpan ClanPromoteCooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan WishPromoteCooldown = TimeSpan.FromHours(1);
    public static readonly TimeSpan WishRefreshCooldown = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan WishPostLifetime = TimeSpan.FromDays(7);

    private const byte RefreshFlag = 1;
    private const byte CodeOk = 0;
    private const byte ApplyTooLong = 1;
    private const byte ApplyAlready = 2;
    private const byte ApplyElsewhere = 3;
    private const byte ApplyInClan = 4;
    private const byte ApplyNone = 5;
    private const byte ApplyError = 6;
    private const byte AdvertTooLong = 1;
    private const byte AdvertAlready = 2;
    private const byte AdvertNoAuthority = 3;
    private const byte AdvertNone = 4;
    private const byte PromoteError = 0;
    private const byte PromoteNoClan = 2;
    private const byte PromoteNotCommander = 3;
    private const byte PromoteTooSoon = 4;
    private const byte PromoteNoAdvert = 5;
    private const byte PromoteNotRegistered = 6;
    private const byte PromoteWar = 7;
    private const byte WishFailed = 0;
    private const byte WishOk = 1;
    private const byte WishAlready = 2;
    private const byte WishClanMember = 3;
    private const byte WishExceeded = 4;
    private const byte WishInClan = 3;
    private const byte WishOpen = 0;
    private const byte WishRequestOpen = 1;
    private const byte WishIntro = 2;
    private const byte WishOffline = 3;
    private const byte WishOnline = 4;
    private const byte WishAccept = 5;
    private const byte WishPromoteSelf = 6;
    private const string DateFormat = "yyyy-MM-dd";

    private sealed record Advert(string Text);
    private sealed record Application(short ClanId, string Name, byte Level, byte Class, string Message);
    private sealed record WishPost(string Name, string Introduction, byte Level, byte Class, byte Age, byte Nation, DateTimeOffset Registered);
    private sealed record WishRequestEntry(short ClanId, string Leader, string Message);

    private readonly object gate = new();
    private readonly Dictionary<short, Advert> adverts = [];
    private readonly Dictionary<short, DateTimeOffset> clanPromotedAt = [];
    private readonly Dictionary<string, Application> applications = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WishPost> posts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<WishRequestEntry>> requests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<short, string> requestedBy = [];
    private readonly Dictionary<string, DateTimeOffset> wishPromotedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> wishRefreshedAt = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();

    private static bool IsStaff(UserSession session) => session.KnightsId > 0 && ClanRules.CanInvite(session.KnightsFame);

    private KnightsEntity? ClanOf(UserSession session) =>
        session.KnightsId > 0 ? sessionManager.Knights.GetClan(session.KnightsId) : null;

    public async Task HandleBoardAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;
        packet.ReadByte();
        int page = Math.Max(1, (int)packet.ReadByte());
        await session.Client.SendPacket(BoardStatusFor(session));
        await session.Client.SendPacket(AdvertRows(AdvertPage(session, page)));
    }

    private Packet BoardStatusFor(UserSession session)
    {
        lock (gate)
        {
            if (IsStaff(session))
                return StaffStatus(applications.Values.Where(a => a.ClanId == session.KnightsId)
                    .Select(a => new Applicant(a.Level, a.Class, a.Name, a.Message)).ToList());

            byte state = session.KnightsId > 0 ? StateMember : StateClanless;
            AppliedClan? applied = null;
            if (applications.TryGetValue(session.Name, out var application)
                && sessionManager.Knights.GetClan(application.ClanId) is { } clan)
                applied = new AppliedClan(clan.Id, clan.MarkVersion, clan.Name);
            return ApplicantStatus(state, applied);
        }
    }

    private List<AdvertRow> AdvertPage(UserSession session, int page)
    {
        lock (gate)
        {
            return adverts
                .Select(pair => (Clan: sessionManager.Knights.GetClan(pair.Key), Advert: pair.Value))
                .Where(entry => entry.Clan != null && entry.Clan.Nation == (byte)session.Nation)
                .OrderBy(entry => entry.Clan!.Grade)
                .ThenBy(entry => entry.Clan!.Name, StringComparer.OrdinalIgnoreCase)
                .Skip((page - 1) * RowsPerPage)
                .Take(RowsPerPage)
                .Select(entry => new AdvertRow(entry.Clan!.Id, entry.Clan.Name, entry.Clan.Grade, entry.Clan.Flag,
                    entry.Clan.Chief, entry.Clan.MarkVersion, entry.Advert.Text))
                .ToList();
        }
    }

    public async Task HandleApplyAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;
        byte kind = packet.ReadByte();
        byte code = kind == KindRegister ? Apply(session, packet) : CancelApplication(session, packet);
        await session.Client.SendPacket(KindResult(KnightsSubOpcode.ClanAdvanceApply, kind, code));
    }

    private byte Apply(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4)
            return ApplyError;
        short clanId = packet.ReadShort();
        string message = packet.ReadString();
        if (session.KnightsId > 0)
            return ApplyInClan;
        if (message.Length > MaxMessageBytes)
            return ApplyTooLong;
        var clan = sessionManager.Knights.GetClan(clanId);
        if (clan == null || clan.Nation != (byte)session.Nation)
            return ApplyError;
        lock (gate)
        {
            if (!adverts.ContainsKey(clanId))
                return ApplyNone;
            if (applications.TryGetValue(session.Name, out var existing))
                return existing.ClanId == clanId ? ApplyAlready : ApplyElsewhere;
            applications[session.Name] = new Application(clanId, session.Name, session.Level, (byte)session.Class, message);
        }
        return CodeOk;
    }

    private byte CancelApplication(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return ApplyError;
        string name = packet.ReadString();
        lock (gate)
        {
            if (!applications.TryGetValue(name, out var application))
                return ApplyNone;
            bool own = string.Equals(name, session.Name, StringComparison.OrdinalIgnoreCase);
            bool staffOfClan = IsStaff(session) && session.KnightsId == application.ClanId;
            if (!own && !staffOfClan)
                return ApplyError;
            applications.Remove(name);
        }
        return CodeOk;
    }

    public async Task HandleAdvertAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;
        byte kind = packet.ReadByte();
        byte code;
        if (!IsStaff(session) || ClanOf(session) == null)
            code = AdvertNoAuthority;
        else if (kind == KindRegister)
            code = RegisterAdvert(session, packet.RemainingBytes >= 2 ? packet.ReadString() : "");
        else
            code = DeleteAdvert(session);
        await session.Client.SendPacket(KindResult(KnightsSubOpcode.ClanAdvanceRegister, kind, code));
    }

    private byte RegisterAdvert(UserSession session, string text)
    {
        if (text.Length == 0 || text.Length > MaxMessageBytes)
            return AdvertTooLong;
        lock (gate)
        {
            if (adverts.ContainsKey(session.KnightsId))
                return AdvertAlready;
            adverts[session.KnightsId] = new Advert(text);
        }
        return CodeOk;
    }

    private byte DeleteAdvert(UserSession session)
    {
        lock (gate)
        {
            if (!adverts.Remove(session.KnightsId))
                return AdvertNone;
            foreach (var name in applications.Where(pair => pair.Value.ClanId == session.KnightsId).Select(pair => pair.Key).ToList())
                applications.Remove(name);
        }
        return CodeOk;
    }

    public async Task HandlePromoteAsync(UserSession session)
    {
        var clan = ClanOf(session);
        byte? refusal = null;
        lock (gate)
        {
            if (clan == null)
                refusal = PromoteNoClan;
            else if (!IsStaff(session))
                refusal = PromoteNotCommander;
            else if (!adverts.ContainsKey(clan.Id))
                refusal = PromoteNoAdvert;
            else if (sessionManager.Battle.IsBattleActive)
                refusal = PromoteWar;
            else if (clanPromotedAt.TryGetValue(clan.Id, out var last) && Now - last < ClanPromoteCooldown)
                refusal = PromoteTooSoon;
            else
                clanPromotedAt[clan.Id] = Now;
        }

        if (refusal is { } code || clan == null)
        {
            await session.Client.SendPacket(PromoteRefused(refusal ?? PromoteError));
            return;
        }

        var line = Promoted(clan.Name, session.Name);
        foreach (var player in sessionManager.GetAll().Where(p => p.Nation == session.Nation).ToList())
            await player.Client.SendPacket(line);
    }

    public async Task HandleWishAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;
        switch (packet.ReadByte())
        {
            case WishOpen when packet.RemainingBytes >= 2:
                await OpenWishAsync(session, packet.ReadByte(), packet.ReadByte());
                break;
            case WishRequestOpen:
                await session.Client.SendPacket(WishRequestList(RequestsFor(session.Name)));
                break;
            case WishIntro when packet.RemainingBytes >= 1:
                await IntroAsync(session, packet);
                break;
            case WishOffline when packet.RemainingBytes >= 1:
                await RequestAsync(session, packet);
                break;
            case WishOnline when packet.RemainingBytes >= 5:
                await RequestOnlineAsync(session, packet.ReadInt(), packet.ReadSByteString());
                break;
            case WishAccept when packet.RemainingBytes >= 2:
                await AcceptAsync(session, packet.ReadShort());
                break;
            case WishPromoteSelf:
                await PromoteSelfAsync(session);
                break;
        }
    }

    private async Task OpenWishAsync(UserSession session, byte pageByte, byte refresh)
    {
        bool tooSoon = false;
        lock (gate)
        {
            if (refresh == RefreshFlag && wishRefreshedAt.TryGetValue(session.Name, out var last) && Now - last < WishRefreshCooldown)
                tooSoon = true;
            else if (refresh == RefreshFlag)
                wishRefreshedAt[session.Name] = Now;
        }
        if (tooSoon)
        {
            await session.Client.SendPacket(WishTooSoon());
            return;
        }

        await session.Client.SendPacket(WishStatusFor(session));
        await session.Client.SendPacket(WishRows(WishPageFor(session, Math.Max(1, (int)pageByte))));
    }

    private Packet WishStatusFor(UserSession session)
    {
        lock (gate)
        {
            if (IsStaff(session))
                return WishRecruiterStatus(requestedBy.TryGetValue(session.KnightsId, out var name) ? name : "");
            if (session.KnightsId > 0)
                return WishMemberStatus();
            return WishSeekerStatus(requests.TryGetValue(session.Name, out var list) ? list.Count : 0);
        }
    }

    private List<WishRow> WishPageFor(UserSession session, int page)
    {
        lock (gate)
        {
            DropExpiredPosts();
            return posts.Values
                .Where(post => post.Nation == (byte)session.Nation)
                .OrderByDescending(post => post.Registered)
                .Skip((page - 1) * RowsPerPage)
                .Take(RowsPerPage)
                .Select(post =>
                {
                    var online = sessionManager.GetByName(post.Name);
                    return new WishRow(post.Name, post.Introduction, online?.Level ?? post.Level, post.Class, post.Age,
                        online != null, online?.CharacterId ?? NoTarget, post.Registered.ToString(DateFormat));
                })
                .ToList();
        }
    }

    private void DropExpiredPosts()
    {
        foreach (var name in posts.Where(pair => Now - pair.Value.Registered > WishPostLifetime).Select(pair => pair.Key).ToList())
            posts.Remove(name);
    }

    private List<WishRequest> RequestsFor(string poster)
    {
        lock (gate)
        {
            if (!requests.TryGetValue(poster, out var list))
                return [];
            return list.Select(request => sessionManager.Knights.GetClan(request.ClanId) is { } clan
                    ? new WishRequest(clan.Id, request.Leader, clan.Name, clan.Grade, clan.Flag, clan.MarkVersion, request.Message)
                    : (WishRequest?)null)
                .Where(request => request != null)
                .Select(request => request!.Value)
                .ToList();
        }
    }

    private async Task IntroAsync(UserSession session, Packet packet)
    {
        byte action = packet.ReadByte();
        byte code;
        if (session.KnightsId > 0)
            code = WishClanMember;
        else if (action == KindRegister)
            code = RegisterIntro(session, packet);
        else
            lock (gate)
                code = posts.Remove(session.Name) ? WishOk : WishFailed;
        await session.Client.SendPacket(WishResult(WishIntroResult, action, code));
    }

    private byte RegisterIntro(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return WishFailed;
        byte age = packet.ReadByte();
        string text = packet.ReadSByteString();
        if (age is < MinAge or > MaxAge || text.Length == 0 || text.Length > MaxMessageBytes)
            return WishFailed;
        lock (gate)
        {
            DropExpiredPosts();
            if (posts.ContainsKey(session.Name))
                return WishAlready;
            posts[session.Name] = new WishPost(session.Name, text, session.Level, (byte)session.Class, age, (byte)session.Nation, Now);
        }
        return WishOk;
    }

    private async Task RequestAsync(UserSession session, Packet packet)
    {
        byte action = packet.ReadByte();
        byte code = action == KindRegister ? SendOfflineRequest(session, packet) : CancelRequest(session, packet);
        await session.Client.SendPacket(WishResult(WishRequestResult, action, code));
    }

    private byte SendOfflineRequest(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4 || !IsStaff(session))
            return WishFailed;
        short clanId = packet.ReadShort();
        string poster = packet.ReadSByteString();
        string message = packet.RemainingBytes >= 1 ? packet.ReadSByteString() : "";
        if (clanId != session.KnightsId || message.Length > MaxMessageBytes)
            return WishFailed;
        lock (gate)
        {
            if (!posts.ContainsKey(poster))
                return WishFailed;
            if (!requests.TryGetValue(poster, out var list))
                requests[poster] = list = [];
            if (list.Any(request => request.ClanId == clanId))
                return WishAlready;
            if (list.Count >= MaxRequests)
                return WishExceeded;
            if (requestedBy.TryGetValue(clanId, out var previous))
                requests.GetValueOrDefault(previous)?.RemoveAll(request => request.ClanId == clanId);
            list.Add(new WishRequestEntry(clanId, session.Name, message));
            requestedBy[clanId] = poster;
        }
        return WishOk;
    }

    private byte CancelRequest(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return WishFailed;
        short clanId = packet.ReadShort();
        lock (gate)
        {
            if (IsStaff(session) && session.KnightsId == clanId)
            {
                if (!requestedBy.Remove(clanId, out var poster))
                    return WishFailed;
                requests.GetValueOrDefault(poster)?.RemoveAll(request => request.ClanId == clanId);
                return WishOk;
            }
            if (!requests.TryGetValue(session.Name, out var list) || list.RemoveAll(request => request.ClanId == clanId) == 0)
                return WishFailed;
            requestedBy.Remove(clanId);
            return WishOk;
        }
    }

    private async Task RequestOnlineAsync(UserSession session, int targetId, string name)
    {
        var target = sessionManager.GetByCharacterId(targetId);
        if (!IsStaff(session) || target == null || !string.Equals(target.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            await session.Client.SendPacket(WishResult(WishRequestResult, KindRegister, WishFailed));
            return;
        }
        await membership.InviteAsync(session, targetId);
        await session.Client.SendPacket(WishResult(WishRequestResult, KindRegister, WishOk));
    }

    private async Task AcceptAsync(UserSession session, short clanId)
    {
        var clan = sessionManager.Knights.GetClan(clanId);
        lock (gate)
        {
            if (clan == null || session.KnightsId > 0 || !requests.TryGetValue(session.Name, out var list)
                || list.All(request => request.ClanId != clanId))
                clan = null;
        }
        if (clan == null || !await membership.JoinFromBoardAsync(session, clan))
        {
            await session.Client.SendPacket(WishResult(WishRequestResult, KindRegister, WishFailed));
            return;
        }
        lock (gate)
        {
            posts.Remove(session.Name);
            applications.Remove(session.Name);
            if (requests.Remove(session.Name, out var accepted))
                foreach (var request in accepted)
                    requestedBy.Remove(request.ClanId);
        }
    }

    private async Task PromoteSelfAsync(UserSession session)
    {
        byte? refusal = null;
        lock (gate)
        {
            if (session.KnightsId > 0)
                refusal = WishInClan;
            else if (!posts.ContainsKey(session.Name))
                refusal = PromoteNotRegistered;
            else if (sessionManager.Battle.IsBattleActive)
                refusal = PromoteWar;
            else if (wishPromotedAt.TryGetValue(session.Name, out var last) && Now - last < WishPromoteCooldown)
                refusal = PromoteTooSoon;
            else
                wishPromotedAt[session.Name] = Now;
        }

        if (refusal is { } code)
        {
            await session.Client.SendPacket(WishPromoteRefused(code));
            return;
        }

        var line = WishPromoted(session.Name);
        foreach (var player in sessionManager.GetAll().Where(p => p.Nation == session.Nation).ToList())
            await player.Client.SendPacket(line);
    }
}
