using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Common.Tests;

public class PacketUtf8StringTests
{
    [Theory]
    [InlineData("Hola Mundo")]
    [InlineData("¿Dónde está la Manzana de Moradon?")]
    [InlineData("Menissiah te encargó participar en la Batalla del Caos")]
    [InlineData("Türkçe: ışığın gücü")]
    [InlineData("Русский текст")]
    [InlineData("한국어 텍스트")]
    [InlineData("")]
    public void WriteUtf8String_RoundTripsNonAsciiText(string text)
    {
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteUtf8String(text);

        packet.ReadUtf8String().Should().Be(text);
    }

    [Fact]
    public void WriteString_StaysAsciiSoRetailLayoutsDoNotShift()
    {
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteString("Dónde");

        packet.ReadString().Should().Be("D?nde");
    }

    [Fact]
    public void WriteUtf8String_CarriesTheLongestDialogueInTheTextTable()
    {
        var longest = new string('á', 828);
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteUtf8String(longest);

        packet.ReadUtf8String().Should().Be(longest);
    }

    [Fact]
    public void WriteUtf8String_RejectsTextThatWouldOverflowTheLengthPrefix()
    {
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);

        var write = () => packet.WriteUtf8String(new string('a', ushort.MaxValue + 1));

        write.Should().Throw<ArgumentException>();
    }
}
