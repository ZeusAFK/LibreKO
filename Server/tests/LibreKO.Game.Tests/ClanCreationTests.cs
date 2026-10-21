using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ClanCreationTests : GameTestBase
{
    private const int FounderId = 650;
    private const int RivalId = 651;
    private const short OtherClan = 77;
    private const string ClanName = "Ravens";
    private const string Renamed = "Crows";
    private const short HeldMembers = 2;
    private static readonly TimeSpan RivalWait = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task APlayerWithTheCoinsInATownFoundsAClan()
    {
        using var provider = Provider(new Hooks());
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        founder.KnightsId.Should().BeGreaterThan(0);
        founder.Money.Should().Be(0);
        (await ClansNamed(provider, ClanName)).Should().Be(1);
    }

    [Fact]
    public async Task TwoClansFoundedAtOnceUnderOneNameMakeOne()
    {
        var hooks = new Hooks();
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);
        var rival = Founder(provider, RivalId);
        Task? rivalFounding = null;
        var creates = 0;
        hooks.BeforeCreate = async () =>
        {
            if (Interlocked.Increment(ref creates) > 1)
                return;

            rivalFounding = Task.Run(() => Create(provider, rival, ClanName));
            await Task.WhenAny(rivalFounding, Task.Delay(RivalWait));
        };

        await Create(provider, founder, ClanName);
        await rivalFounding!;

        (await ClansNamed(provider, ClanName)).Should().Be(1, "a name is checked and taken one founding at a time");
        new[] { founder, rival }.Count(player => player.KnightsId > 0).Should().Be(1);
        new[] { founder, rival }.Sum(player => (long)player.Money).Should().Be(ClanRules.CreationCoins, "only the founder pays");
    }

    [Fact]
    public async Task CoinsSpentWhileTheClanIsFoundedAreNotTakenAgain()
    {
        var hooks = new Hooks();
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);
        hooks.BeforeNameCheck = () =>
        {
            founder.Money = 0;
            return Task.CompletedTask;
        };

        await Create(provider, founder, ClanName);

        founder.Money.Should().Be(0, "the purse never goes below empty");
        founder.KnightsId.Should().Be(0);
        (await ClansNamed(provider, ClanName)).Should().Be(0);
    }

    [Fact]
    public async Task AFounderWhoJoinedAnotherClanMeanwhileKeepsItAndTheCoins()
    {
        var hooks = new Hooks();
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);
        hooks.AfterCreate = () => founder.KnightsId = OtherClan;

        await Create(provider, founder, ClanName);

        founder.KnightsId.Should().Be(OtherClan);
        founder.Money.Should().Be(ClanRules.CreationCoins);
        (await ClansNamed(provider, ClanName)).Should().Be(0, "the clan stored for the founding comes down again");
    }

    [Fact]
    public async Task AClanThatCannotBeStoredCostsNothing()
    {
        var hooks = new Hooks();
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);
        hooks.FailCreate = true;

        var founding = () => Create(provider, founder, ClanName);

        await founding.Should().ThrowAsync<DbUpdateException>();
        founder.Money.Should().Be(ClanRules.CreationCoins);
        founder.KnightsId.Should().Be(0);
    }

    [Fact]
    public async Task ASaveOfAClanHeldInMemoryKeepsTheNameARenameStored()
    {
        using var provider = Provider(new Hooks());
        var held = new KnightsEntity { Name = ClanName, Chief = "Founder", Members = 1 };
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().CreateAsync(held);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Knights.FindAsync(held.Id))!.Name = Renamed;
            await db.SaveChangesAsync();
        }

        held.Members = HeldMembers;
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(held);

        await using var check = provider.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<AppDbContext>().Knights.AsNoTracking().SingleAsync(clan => clan.Id == held.Id);
        stored.Name.Should().Be(Renamed);
        stored.Members.Should().Be(HeldMembers);
    }

    private sealed class Hooks
    {
        public Func<Task> BeforeNameCheck { get; set; } = () => Task.CompletedTask;
        public Func<Task> BeforeCreate { get; set; } = () => Task.CompletedTask;
        public Action AfterCreate { get; set; } = () => { };
        public bool FailCreate { get; set; }
    }

    private sealed class HookedKnights(IKnightsRepository inner, Hooks hooks) : IKnightsRepository
    {
        public async Task<bool> IsNameTakenAsync(string name)
        {
            await hooks.BeforeNameCheck();
            return await inner.IsNameTakenAsync(name);
        }

        public async Task CreateAsync(KnightsEntity clan)
        {
            if (hooks.FailCreate)
                throw new DbUpdateException("the database went away");
            await hooks.BeforeCreate();
            await inner.CreateAsync(clan);
            hooks.AfterCreate();
        }

        public Task<KnightsEntity?> FindAsync(short id) => inner.FindAsync(id);
        public Task RemoveAsync(KnightsEntity entity) => inner.RemoveAsync(entity);
        public Task UpdateAsync(KnightsEntity clan) => inner.UpdateAsync(clan);
        public Task<List<ClanMemberProjection>> GetMembersAsync(short knightsId) => inner.GetMembersAsync(knightsId);
        public Task<List<Character>> GetCharactersByClanAsync(short knightsId) => inner.GetCharactersByClanAsync(knightsId);
        public Task<List<ClanMemberLoyalty>> GetMemberLoyaltyAsync() => inner.GetMemberLoyaltyAsync();
        public Task SyncCharacterClanStateAsync(int characterId, short knightsId, byte fame, int? money = null, int? loyalty = null) =>
            inner.SyncCharacterClanStateAsync(characterId, knightsId, fame, money, loyalty);
        public Task SetClanMemoAsync(int characterId, string memo) => inner.SetClanMemoAsync(characterId, memo);
    }

    private static ServiceProvider Provider(Hooks hooks) => CreateProvider(
        db =>
        {
            db.Characters.AddRange(
                new Character { Id = FounderId, AccountId = FounderId, Name = $"Founder{FounderId}", Level = ClanRules.CreationLevel },
                new Character { Id = RivalId, AccountId = RivalId, Name = $"Founder{RivalId}", Level = ClanRules.CreationLevel });
            db.SaveChanges();
        },
        configureServices: services => services.AddScoped<IKnightsRepository>(scope =>
            new HookedKnights(new KnightsRepository(scope.GetRequiredService<AppDbContext>()), hooks)));

    private static UserSession Founder(ServiceProvider provider, int characterId)
    {
        var sessions = provider.GetRequiredService<SessionManager>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, characterId, characterId);
        session.Name = $"Founder{characterId}";
        session.Nation = AccountNation.Karus;
        session.Level = ClanRules.CreationLevel;
        session.Money = ClanRules.CreationCoins;
        session.ZoneId = (byte)ZoneId.Moradon;
        sessions.Regions.AddToRegion(session);
        return session;
    }

    private static Task Create(ServiceProvider provider, UserSession founder, string name)
    {
        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteString(name);
        packet.ResetOffset();
        return provider.GetRequiredService<IKnightsMembershipPacketService>().HandleCreateAsync(founder, packet);
    }

    private static async Task<int> ClansNamed(ServiceProvider provider, string name)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Knights.CountAsync(clan => clan.Name == name);
    }
}
