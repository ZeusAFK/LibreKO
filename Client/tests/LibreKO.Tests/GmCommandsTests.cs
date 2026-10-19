using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class GmCommandsTests
{
    [Fact]
    public void TheReplyReadsTheOpenFlagAndTheCommands()
    {
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte(1);
        p.WriteShort(2);
        p.WriteString("Items and currency");
        p.WriteString("+gold <amount>");
        p.WriteString("Give or take gold");
        p.WriteString("World");
        p.WriteString("+online");
        p.WriteString("Show the online count");
        p.ResetOffset();

        var (commands, open) = AdminWire.ReadCommands(p);

        Assert.True(open);
        Assert.Equal(new[]
        {
            new GmCommandInfo("Items and currency", "+gold <amount>", "Give or take gold"),
            new GmCommandInfo("World", "+online", "Show the online count"),
        }, commands);
    }

    [Fact]
    public void SearchMatchesSyntaxDescriptionOrGroupAndKeepsTheGroups()
    {
        var commands = new[]
        {
            new GmCommandInfo("Items", "+gold <amount>", "Give or take gold"),
            new GmCommandInfo("Items", "+kc <name> <amount>", "Give or take Knight Cash"),
            new GmCommandInfo("World", "+online", "Show the online count"),
        };

        var knight = GmCommands.Grouped(commands, "knight");
        var world = GmCommands.Grouped(commands, "WORLD");
        var all = GmCommands.Grouped(commands, "");

        Assert.Single(knight);
        Assert.Equal("+kc <name> <amount>", knight[0].Commands[0].Syntax);
        Assert.Equal("World", Assert.Single(world).Group);
        Assert.Equal(new[] { "Items", "World" }, new[] { all[0].Group, all[1].Group });
    }
}
