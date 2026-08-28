using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class MsiEcWmiTransportTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(221)]
    [InlineData(255)]
    public void BuildPackage_MatchesAddonRequestContract(int selector)
    {
        var package = MsiEcWmiTransport.BuildPackage(selector);

        Assert.Equal(32, package.Length);
        Assert.Equal((byte)selector, package[0]);
        Assert.All(package.Skip(1), b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void BuildPackage_RejectsOutOfRangeSelector(int selector)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiEcWmiTransport.BuildPackage(selector));
    }
}
