using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class NationsTests
{
    [Theory]
    [InlineData(101, Nations.Karus)]
    [InlineData(115, Nations.Karus)]
    [InlineData(201, Nations.ElMorad)]
    [InlineData(211, Nations.ElMorad)]
    public void TheClassCodeNamesTheNation(int classCode, int nation) => Assert.Equal(nation, Nations.OfClass(classCode));
}
