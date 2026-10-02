using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class GenieClipTests
{
    [Theory]
    [InlineData(WeaponAnimation.Sword, WeaponAnimation.Shield, GenieClip.Melee)]
    [InlineData(WeaponAnimation.None, WeaponAnimation.Bow, GenieClip.Archery)]
    [InlineData(WeaponAnimation.Crossbow, WeaponAnimation.None, GenieClip.Archery)]
    [InlineData(WeaponAnimation.None, WeaponAnimation.LongBow, GenieClip.Archery)]
    [InlineData(WeaponAnimation.Staff, WeaponAnimation.None, GenieClip.Melee)]
    public void TheGenieCutsItsClipByTheWeaponInHand(int right, int left, double scale)
    {
        Assert.Equal(scale, GenieClip.Scale(right, left));
    }
}
