using OptiSensor.Tweaks.IntelVrr;
using Xunit;

namespace OptiSensor.Tests.Tweaks.IntelVrr;

public class AffectedPanelDetectorTests
{
    [Fact]
    public void IsAffectedPanel_AllThreeIdentitiesMatch_ReturnsTrue()
    {
        var identity = new PanelIdentity("CSW", "0801", "PN8007QB1-2");

        Assert.True(AffectedPanelDetector.IsAffectedPanel(identity));
    }

    [Fact]
    public void IsAffectedPanel_PanelNameNull_ManufacturerAndProductCodeMatch_ReturnsFalse()
    {
        // Reviewer-requested fail-open behavior: a missing/unreadable panel name must NOT be
        // treated as a match even if manufacturer + product code agree.
        var identity = new PanelIdentity("CSW", "0801", null);

        Assert.False(AffectedPanelDetector.IsAffectedPanel(identity));
    }

    [Fact]
    public void IsAffectedPanel_WrongManufacturer_ReturnsFalse()
    {
        var identity = new PanelIdentity("AUO", "0801", "PN8007QB1-2");

        Assert.False(AffectedPanelDetector.IsAffectedPanel(identity));
    }

    [Fact]
    public void IsAffectedPanel_WrongProductCode_ReturnsFalse()
    {
        var identity = new PanelIdentity("CSW", "1234", "PN8007QB1-2");

        Assert.False(AffectedPanelDetector.IsAffectedPanel(identity));
    }

    [Fact]
    public void IsAffectedPanel_WrongPanelName_ReturnsFalse()
    {
        var identity = new PanelIdentity("CSW", "0801", "SomeOtherPanel");

        Assert.False(AffectedPanelDetector.IsAffectedPanel(identity));
    }
}
