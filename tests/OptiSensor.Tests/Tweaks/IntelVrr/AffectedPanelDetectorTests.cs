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

    [Fact]
    public void DecodeUShortArrayAsString_ProductCodeCharCodes_DecodesAsLiteralDigitString()
    {
        // WmiMonitorID.ProductCodeID is a ushort[] of character codes, exactly like
        // ManufacturerName/UserFriendlyName - NOT two raw packed EDID bytes. The affected panel's
        // real ProductCodeID decodes to the literal ASCII string "0801", not a hex-packed word.
        var productCodeChars = new ushort[] { '0', '8', '0', '1' };

        var decoded = AffectedPanelDetector.DecodeUShortArrayAsString(productCodeChars);

        Assert.Equal("0801", decoded);
    }

    [Fact]
    public void DecodeUShortArrayAsString_FullIdentityCharCodes_DecodesAllThreeFieldsAndMatches()
    {
        // Exercises the same shared decode helper used by EnumeratePanelIdentities for all three
        // WmiMonitorID fields, as close as reasonably possible to the real WMI-backed code path
        // without standing up a live WMI provider.
        var manufacturerChars = new ushort[] { 'C', 'S', 'W' };
        var productCodeChars = new ushort[] { '0', '8', '0', '1' };
        var nameChars = "PN8007QB1-2".Select(c => (ushort)c).ToArray();

        var manufacturer = AffectedPanelDetector.DecodeUShortArrayAsString(manufacturerChars);
        var productCode = AffectedPanelDetector.DecodeUShortArrayAsString(productCodeChars);
        var name = AffectedPanelDetector.DecodeUShortArrayAsString(nameChars);

        Assert.Equal("CSW", manufacturer);
        Assert.Equal("0801", productCode);
        Assert.Equal("PN8007QB1-2", name);

        var identity = new PanelIdentity(manufacturer!, productCode!, name, Active: true, InstanceName: @"DISPLAY\CSW0801\4&abc123&0&UID0");
        Assert.True(AffectedPanelDetector.IsAffectedPanel(identity));
    }
}
