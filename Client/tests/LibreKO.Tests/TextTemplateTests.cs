using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class TextTemplateTests
{
    [Fact]
    public void ArgumentsFillTheSlotsInOrder()
    {
        Assert.Equal("- Rin defeat Aria ( 512, 640 ) -",
            TextTemplate.Fill("- %s defeat %s ( %d, %d ) -", "Rin", "Aria", 512, 640));
    }

    [Fact]
    public void MissingArgumentsLeaveTheSlotEmptyAndPercentSignsSurvive()
    {
        Assert.Equal("100% of  done", TextTemplate.Fill("%d%% of %s done", 100));
    }
}
