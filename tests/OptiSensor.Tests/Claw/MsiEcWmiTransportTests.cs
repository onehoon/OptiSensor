using System.Runtime.InteropServices;
using OptiSensor.Claw;
using Xunit;

namespace OptiSensor.Tests.Claw;

public class MsiEcWmiTransportTests
{
    [Fact]
    public void Classify_GlobalAccessDenialIsTransportWide()
    {
        Assert.Equal(MsiEcReadStatus.TransportUnavailable,
            MsiEcWmiTransport.Classify(new UnauthorizedAccessException()));
    }

    [Theory]
    [InlineData(typeof(COMException))]
    [InlineData(typeof(InvalidOperationException))]
    public void Classify_AmbiguousFailuresStayMetricLocal(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        Assert.Equal(MsiEcReadStatus.MetricUnavailable, MsiEcWmiTransport.Classify(ex));
    }

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
