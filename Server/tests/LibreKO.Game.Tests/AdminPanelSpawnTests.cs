using System.Text;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class AdminPanelSpawnTests
{
    private const byte ReqSpawnRow = 16;
    private const byte ReqSpawnSet = 17;
    private const byte ReqSpawnPersist = 18;
    private const byte AckResult = 0x11;
    private const byte AckSpawnRow = 0x16;
    private const byte Moradon = 21;
    private const int JuliaRow = 5;

    private sealed record Row(bool CanPersist, int Index, int NpcId, string Name, byte Zone, bool Monster, int X, int Z, int YTenths,
        int Direction, byte Count, short Respawn, short Range, int Alive);

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), $"libreko-seed-{Guid.NewGuid():N}");
        public readonly SessionManager Sessions = new();
        public readonly List<Packet> Sent = [];
        public readonly IClient Client = Substitute.For<IClient>();
        public readonly INpcSpawnRowStore Store = Substitute.For<INpcSpawnRowStore>();
        public readonly IHostEnvironment Environment = Substitute.For<IHostEnvironment>();
        public readonly NpcPosData Julia = new()
        {
            Index = JuliaRow, ZoneId = Moradon, NpcId = 31741, ActType = 100, LeftX = 769, TopZ = 369, NumNPC = 1, RegTime = 3600, Direction = 90,
        };
        public readonly NpcSpawnRowService SpawnRows;
        public readonly AdminPanelPacketCoordinator Coordinator;
        public readonly UserSession Gm;

        public string Shard => NpcPositionSeedFile.ShardPath(Root, Moradon);

        public Fixture(string environment = "Development")
        {
            Directory.CreateDirectory(Path.Combine(Root, "Seed", "Data"));
            File.WriteAllText(Shard,
                "[\r\n  {\r\n    \"Index\": 1,\r\n    \"ZoneId\": 21,\r\n    \"NpcId\": 256,\r\n    \"ActType\": 1,\r\n    \"DotCnt\": 0,\r\n    \"Path\": null,\r\n    \"LeftX\": 700,\r\n    \"TopZ\": 500,\r\n    \"NumNPC\": 5,\r\n    \"RegTime\": 25,\r\n    \"Direction\": 0,\r\n    \"SpawnRange\": 7,\r\n    \"RegenType\": 0,\r\n    \"DungeonFamily\": 0,\r\n    \"SpecialType\": 0,\r\n    \"TrapNumber\": 0,\r\n    \"Room\": 0\r\n  },\r\n  {\r\n    \"Index\": 5,\r\n    \"ZoneId\": 21,\r\n    \"NpcId\": 31741,\r\n    \"ActType\": 100,\r\n    \"DotCnt\": 0,\r\n    \"Path\": null,\r\n    \"LeftX\": 769,\r\n    \"TopZ\": 369,\r\n    \"NumNPC\": 1,\r\n    \"RegTime\": 3600,\r\n    \"Direction\": 90,\r\n    \"SpawnRange\": 0,\r\n    \"RegenType\": 0,\r\n    \"DungeonFamily\": 0,\r\n    \"SpecialType\": 0,\r\n    \"TrapNumber\": 0,\r\n    \"Room\": 0\r\n  }\r\n]\r\n",
                new UTF8Encoding(false));

            Environment.EnvironmentName.Returns(environment);
            Environment.ContentRootPath.Returns(Root);
            Client.Id.Returns(Guid.NewGuid());
            Client.SendPacket(Arg.Do<Packet>(packet => Sent.Add(packet)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Gm = Sessions.CreateSession(Client, 1, 1);
            Gm.Name = "Zeus";
            Gm.IsGM = true;
            Gm.ZoneId = Moradon;

            var gameData = Substitute.For<IGameDataService>();
            gameData.NpcPositions.Returns(new List<NpcPosData> { Julia });
            gameData.GetSpawnProto(Arg.Any<NpcPosData>()).Returns(new NpcData { Id = 31741, Name = "[Trader] Julia", Level = 80 });
            SpawnRows = new NpcSpawnRowService(Sessions, gameData, Substitute.For<IMonsterAggressionPolicy>());
            Store.UpdateAsync(Arg.Any<NpcPosData>()).Returns(true);
            Coordinator = new AdminPanelPacketCoordinator(
                Sessions,
                gameData,
                Substitute.For<IUserNotificationService>(),
                Substitute.For<ICombatNotificationService>(),
                Substitute.For<IZoneTransitionService>(),
                Substitute.For<IWorldMovementService>(),
                SpawnRows,
                Store,
                Environment,
                Substitute.For<ICollectionRaceService>(),
                Substitute.For<IPlayerProgressionService>(),
                Substitute.For<ILoyaltyService>(),
                Substitute.For<IItemGrantService>(),
                Substitute.For<IServiceScopeFactory>(),
                Options.Create(new GameServerSettings()),
                Substitute.For<ILogger<AdminPanelPacketCoordinator>>());
            SpawnRows.Spawn(Julia);
        }

        public IEnumerable<NpcInstance> Live => Sessions.Regions.GetAllNpcs().Where(npc => npc.SpawnRow == JuliaRow);

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static Packet RowRequest(int index)
    {
        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(ReqSpawnRow);
        packet.WriteInt(index);
        packet.ResetOffset();
        return packet;
    }

    private static Packet EditRequest(byte sub, int index, int x, int z, int direction, byte count, short respawn, short range)
    {
        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(sub);
        packet.WriteInt(index);
        packet.WriteInt(x);
        packet.WriteInt(z);
        packet.WriteInt(direction);
        packet.WriteByte(count);
        packet.WriteShort(respawn);
        packet.WriteShort(range);
        packet.ResetOffset();
        return packet;
    }

    private static bool IsSub(Packet packet, byte sub)
    {
        packet.ResetOffset();
        return packet.ReadByte() == sub;
    }

    private static Row ReadRow(List<Packet> sent)
    {
        var packet = sent.Last(p => IsSub(p, AckSpawnRow));
        packet.ResetOffset();
        packet.ReadByte();
        return new Row(packet.ReadByte() == AdminPanelPacketWriter.Granted, packet.ReadInt(), packet.ReadInt(), packet.ReadSByteString(),
            packet.ReadByte(), packet.ReadByte() == AdminPanelPacketWriter.Granted, packet.ReadInt(), packet.ReadInt(), packet.ReadInt(),
            packet.ReadInt(), packet.ReadByte(), packet.ReadShort(), packet.ReadShort(), packet.ReadInt());
    }

    private static (bool Ok, string Message) ReadResult(List<Packet> sent)
    {
        var packet = sent.Last(p => IsSub(p, AckResult));
        packet.ResetOffset();
        packet.ReadByte();
        return (packet.ReadByte() == AdminPanelPacketWriter.Granted, packet.ReadString());
    }

    [Fact]
    public async Task TheRowIsSentWithItsLiveState()
    {
        using var f = new Fixture();

        await f.Coordinator.HandleAsync(f.Client, RowRequest(JuliaRow));

        ReadRow(f.Sent).Should().Be(new Row(true, JuliaRow, 31741, "[Trader] Julia", Moradon, false, 769, 369, 0, 90, 1, 3600, 0, 1));
    }

    [Fact]
    public async Task SetMovesTheRowAndRespawnsItInMemoryOnly()
    {
        using var f = new Fixture();
        var shardBefore = File.ReadAllText(f.Shard);

        await f.Coordinator.HandleAsync(f.Client, EditRequest(ReqSpawnSet, JuliaRow, 800, 400, 180, 3, 60, 0));

        f.Julia.Should().BeEquivalentTo(new { LeftX = 800, TopZ = 400, Direction = 180, NumNPC = (byte)3, RegTime = (short)60, SpawnRange = (short)0 });
        f.Live.Should().HaveCount(3).And.OnlyContain(npc => npc.SpawnX == 800 && npc.SpawnZ == 400 && npc.ZoneId == Moradon && !npc.IsDead);
        ReadResult(f.Sent).Should().Be((true, "Spawn row 5 set (3 spawned); not persisted."));
        ReadRow(f.Sent).Should().Be(new Row(true, JuliaRow, 31741, "[Trader] Julia", Moradon, false, 800, 400, 0, 180, 3, 60, 0, 3));
        File.ReadAllText(f.Shard).Should().Be(shardBefore);
        await f.Store.DidNotReceive().UpdateAsync(Arg.Any<NpcPosData>());
    }

    [Fact]
    public async Task PersistWritesTheShardAndTheDatabaseRow()
    {
        using var f = new Fixture();

        await f.Coordinator.HandleAsync(f.Client, EditRequest(ReqSpawnPersist, JuliaRow, 800, 400, 180, 2, 60, 3));

        ReadResult(f.Sent).Should().Be((true, "Spawn row 5 persisted to NpcPositions.zone021.json and the database (2 spawned)."));
        var shard = File.ReadAllText(f.Shard);
        shard.Should().Contain("\"LeftX\": 800,\r\n    \"TopZ\": 400,\r\n    \"NumNPC\": 2,\r\n    \"RegTime\": 60,\r\n    \"Direction\": 180,\r\n    \"SpawnRange\": 3,");
        shard.Should().Contain("\"NpcId\": 256,").And.Contain("\"LeftX\": 700,");
        await f.Store.Received(1).UpdateAsync(f.Julia);
        f.Live.Should().HaveCount(2);
    }

    [Fact]
    public async Task PersistAfterSetsFindsTheSeedRowByItsLastPersistedValues()
    {
        using var f = new Fixture();

        await f.Coordinator.HandleAsync(f.Client, EditRequest(ReqSpawnSet, JuliaRow, 757, 773, 90, 1, 3600, 0));
        await f.Coordinator.HandleAsync(f.Client, EditRequest(ReqSpawnSet, JuliaRow, 760, 770, 90, 1, 3600, 0));
        await f.Coordinator.HandleAsync(f.Client, EditRequest(ReqSpawnPersist, JuliaRow, 761, 771, 90, 1, 3600, 0));

        ReadResult(f.Sent).Ok.Should().BeTrue();
        File.ReadAllText(f.Shard).Should().Contain("\"LeftX\": 761,\r\n    \"TopZ\": 771,");

        await f.Coordinator.HandleAsync(f.Client, EditRequest(ReqSpawnSet, JuliaRow, 762, 772, 90, 1, 3600, 0));
        await f.Coordinator.HandleAsync(f.Client, EditRequest(ReqSpawnPersist, JuliaRow, 763, 773, 90, 1, 3600, 0));

        ReadResult(f.Sent).Ok.Should().BeTrue();
        File.ReadAllText(f.Shard).Should().Contain("\"LeftX\": 763,\r\n    \"TopZ\": 773,").And.NotContain("\"LeftX\": 761,");
        await f.Store.Received(2).UpdateAsync(f.Julia);
    }

    [Fact]
    public async Task PersistIsRefusedOutsideALocalDevelopmentServer()
    {
        using var f = new Fixture("Production");
        var shardBefore = File.ReadAllText(f.Shard);

        await f.Coordinator.HandleAsync(f.Client, EditRequest(ReqSpawnPersist, JuliaRow, 800, 400, 180, 2, 60, 3));

        ReadResult(f.Sent).Ok.Should().BeFalse();
        File.ReadAllText(f.Shard).Should().Be(shardBefore);
        f.Julia.LeftX.Should().Be(769);
        await f.Store.DidNotReceive().UpdateAsync(Arg.Any<NpcPosData>());
    }

    [Theory]
    [InlineData(99, 800, 400, 90, 1)]
    [InlineData(JuliaRow, 800, 400, 400, 1)]
    [InlineData(JuliaRow, 800, 400, 90, 0)]
    [InlineData(JuliaRow, 800, 400, 90, 51)]
    public async Task AnUnknownRowOrValuesOutOfRangeAreRefused(int index, int x, int z, int direction, byte count)
    {
        using var f = new Fixture();

        await f.Coordinator.HandleAsync(f.Client, EditRequest(ReqSpawnSet, index, x, z, direction, count, 60, 0));

        ReadResult(f.Sent).Ok.Should().BeFalse();
        f.Live.Should().ContainSingle();
        f.Julia.LeftX.Should().Be(769);
    }
}
