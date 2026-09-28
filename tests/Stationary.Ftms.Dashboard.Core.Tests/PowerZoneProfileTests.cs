using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class PowerZoneProfileTests
{
    [Test]
    public async Task Create_MapsFtpPercentageBoundariesToSevenZones()
    {
        var profile = PowerZoneProfile.Create(200);

        using (Assert.Multiple())
        {
            await Assert.That(profile.GetZone(110).Code).IsEqualTo("Z1");
            await Assert.That(profile.GetZone(111).Code).IsEqualTo("Z2");
            await Assert.That(profile.GetZone(151).Code).IsEqualTo("Z3");
            await Assert.That(profile.GetZone(181).Code).IsEqualTo("Z4");
            await Assert.That(profile.GetZone(211).Code).IsEqualTo("Z5");
            await Assert.That(profile.GetZone(241).Code).IsEqualTo("Z6");
            await Assert.That(profile.GetZone(301).Code).IsEqualTo("Z7");
        }
    }

    [Test]
    public async Task Create_RejectsZeroFtp()
    {
        await Assert.That(() => PowerZoneProfile.Create(0)).Throws<ArgumentOutOfRangeException>();
    }
}