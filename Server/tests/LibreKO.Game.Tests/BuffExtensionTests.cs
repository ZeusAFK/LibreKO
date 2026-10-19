using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class BuffExtensionTests
{
    private const int BuffSkill = 111654;
    private const int ContinuanceSkill = 500039;
    private const int ContinuanceItem = 800022000;
    private const short BuffSeconds = 600;

    private readonly IGameDataService data = Substitute.For<IGameDataService>();
    private readonly IMagicItemUsageService items = Substitute.For<IMagicItemUsageService>();
    private readonly BuffExtensionService service;
    private readonly SessionManager sessions = new();
    private readonly List<Packet> sent = new();
    private readonly UserSession session;

    public BuffExtensionTests()
    {
        data.GetMagic(BuffSkill).Returns(new MagicData { Id = BuffSkill, Moral = 1 });
        data.GetMagic(ContinuanceSkill).Returns(new MagicData { Id = ContinuanceSkill, Moral = BuffExtensionService.ExtendDurationMoral });
        data.MagicType4Table.Returns(new Dictionary<int, MagicType4Data> { [BuffSkill] = new() { BuffType = (byte)BuffType.None, Duration = BuffSeconds } });
        data.GetItem(ContinuanceItem).Returns(new ItemData { Num = ContinuanceItem, Kind = BuffExtensionService.DurationItemKind, Effect1 = ContinuanceSkill });
        items.TryConsumeItemAsync(Arg.Any<UserSession>(), ContinuanceItem, Arg.Any<int>()).Returns(true);
        service = new BuffExtensionService(data, items, NullLogger<BuffExtensionService>.Instance);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(p => sent.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        session = sessions.CreateSession(client, 1, 1);
        session.Name = "Rin";
    }

    private ActiveBuff Buff()
    {
        var buff = new ActiveBuff { MagicId = BuffSkill, Duration = BuffSeconds, ExpireTicks = DateTime.UtcNow.AddSeconds(BuffSeconds).Ticks };
        session.SetBuff(BuffSkill, buff);
        return buff;
    }

    private void CarryContinuanceItem() =>
        session.Inventory[InventoryConstants.InventoryStart] = new ItemSlot { ItemId = ContinuanceItem, Count = 1 };

    [Fact]
    public async Task ARunningBuffIsDoubledOnceWithAContinuanceItem()
    {
        var buff = Buff();
        long before = buff.ExpireTicks;
        CarryContinuanceItem();

        (await service.ExtendAsync(session, BuffSkill)).Should().BeTrue();
        (await service.ExtendAsync(session, BuffSkill)).Should().BeFalse();

        buff.ExpireTicks.Should().Be(before + TimeSpan.FromSeconds(BuffSeconds).Ticks);
        sent.Should().ContainSingle().Which.GetData().Should().Equal(MagicProcessPacketWriter.CreateExtendDuration(BuffSkill).GetData());
    }

    [Fact]
    public async Task NothingHappensWithoutTheItemOrTheBuff()
    {
        Buff();
        (await service.ExtendAsync(session, BuffSkill)).Should().BeFalse();

        session.ClearBuffs();
        CarryContinuanceItem();
        (await service.ExtendAsync(session, BuffSkill)).Should().BeFalse();
        sent.Should().BeEmpty();
    }
}
