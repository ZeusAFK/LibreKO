using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class RequiredNameChangeTests
{
    private const byte Face = 3;

    private static Packet Record(byte face)
    {
        var p = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        p.WriteString("Zeus");
        p.WriteByte(1);
        p.WriteShort(101);
        p.WriteByte(60);
        p.WriteByte(0);
        p.WriteByte(face);
        p.WriteUInt(0);
        p.WriteUShort(21);
        for (int i = 0; i < InventoryConstants.VisualSlotCount; i++)
        {
            p.WriteInt(0);
            p.WriteShort(0);
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void AFaceAboveTheOffsetMarksTheCharacterForARename()
    {
        var character = CharacterRecordWire.Read(Record(Face + CharacterRecordWire.NameChangeFaceOffset));

        Assert.True(character.NameChangeRequired);
        Assert.Equal(Face, character.Face);
    }

    [Fact]
    public void AnOrdinaryFaceLeavesTheCharacterAlone()
    {
        var character = CharacterRecordWire.Read(Record(Face));

        Assert.False(character.NameChangeRequired);
        Assert.Equal(Face, character.Face);
    }

    [Theory]
    [InlineData("Zeus2", true)]
    [InlineData("Ze us", false)]
    [InlineData("Zeús", false)]
    [InlineData("", false)]
    public void OnlyLettersAndDigitsPass(string name, bool expected) =>
        Assert.Equal(expected, RequiredNameChange.IsAlphanumeric(name));

    [Fact]
    public void TheConfirmLineDropsTheFontTags() =>
        Assert.Equal("Would you like to change your ID to Zeus?",
            RequiredNameChange.ConfirmLine("Would you like to change your ID to <font color=@#ffff00@>%s</font>?", "Zeus"));

    [Theory]
    [InlineData(SelectingCharacterNameChangeResult.Success, RequiredNameChange.ChangedText)]
    [InlineData(SelectingCharacterNameChangeResult.Failed, RequiredNameChange.RefusedText)]
    [InlineData(SelectingCharacterNameChangeResult.InvalidCharacters, RequiredNameChange.InvalidCharactersText)]
    public void EachResultShowsItsText(SelectingCharacterNameChangeResult result, int text) =>
        Assert.Equal(text, RequiredNameChange.ResultText(result)?.Id);
}
