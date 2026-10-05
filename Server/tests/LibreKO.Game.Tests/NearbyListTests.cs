using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NearbyListTests : GameTestBase
{
    private const byte RefreshSub = 3;
    private const byte Zone = 21;
    private const ushort OwnRoom = 2;

    [Fact]
    public async Task PlayersAnywhereInTheZoneAreListed()
    {
        var names = await ListFor(viewer => { }, ("Far", AccountNation.ElMorad, 300f, InvisibilityType.None, false, Zone),
            ("Near", AccountNation.Karus, 12f, InvisibilityType.None, false, Zone),
            ("Elsewhere", AccountNation.Karus, 5f, InvisibilityType.None, false, (byte)1));
        names.Should().Equal("Near", "Far");
    }

    [Fact]
    public async Task HiddenPlayersAreListedOnlyForThoseWhoCouldSeeThem()
    {
        var others = new[]
        {
            ("Stealthed foe", AccountNation.ElMorad, 10f, InvisibilityType.DispelOnMove, false, Zone),
            ("Stealthed friend", AccountNation.Karus, 11f, InvisibilityType.DispelOnMove, false, Zone),
            ("Infiltrator", AccountNation.Karus, 12f, InvisibilityType.Infiltration, false, Zone),
            ("Operator", AccountNation.Karus, 13f, InvisibilityType.None, true, Zone),
        };
        (await ListFor(viewer => { }, others)).Should().Equal("Stealthed friend");
        (await ListFor(viewer => viewer.IsGM = true, others)).Should().HaveCount(4);
    }

    [Fact]
    public async Task PlayersInAnotherRoomOfTheZoneAreNotListed()
    {
        var names = await ListFor(viewer => viewer.Room = OwnRoom,
            ("Other room", AccountNation.Karus, 10f, InvisibilityType.None, false, Zone));
        names.Should().BeEmpty();
    }

    private async Task<List<string>> ListFor(Action<UserSession> setupViewer,
        params (string Name, AccountNation Nation, float Distance, InvisibilityType Hidden, bool Gm, byte ZoneId)[] others)
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var viewer = sessions.CreateSession(client, characterId: 1, accountId: 1);
        viewer.Name = "Viewer";
        viewer.Nation = AccountNation.Karus;
        viewer.ZoneId = Zone;
        viewer.X = 100f;
        viewer.Z = 100f;
        setupViewer(viewer);

        int id = 2;
        foreach (var other in others)
        {
            var otherClient = Substitute.For<IClient>();
            otherClient.Id.Returns(Guid.NewGuid());
            var session = sessions.CreateSession(otherClient, characterId: id, accountId: id);
            id++;
            session.Name = other.Name;
            session.Nation = other.Nation;
            session.ZoneId = other.ZoneId;
            session.X = 100f + other.Distance;
            session.Z = 100f;
            session.Invisibility = other.Hidden;
            session.IsGM = other.Gm;
        }

        var request = new Packet(GameOpcodes.GS_USER_INFO);
        request.WriteByte(RefreshSub);
        request.ResetOffset();
        await provider.GetRequiredService<IWorldVisibilityService>().HandleBottomUserListAsync(client, request);

        var list = sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_USER_INFO);
        list.ResetOffset();
        list.ReadByte();
        list.ReadByte();
        list.ReadShort();
        list.ReadByte();
        int count = list.ReadShort();
        var names = new List<string>();
        for (int i = 0; i < count; i++)
        {
            names.Add(list.ReadSByteString());
            list.ReadByte();
            list.ReadShort();
            list.ReadShort();
            list.ReadShort();
            list.ReadInt();
            list.ReadShort();
            list.ReadShort();
        }
        return names;
    }
}
