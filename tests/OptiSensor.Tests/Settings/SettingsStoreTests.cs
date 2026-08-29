using System.Text.Json;
using OptiSensor.Settings;
using Xunit;

namespace OptiSensor.Tests.Settings;

/// <summary>
/// Settings recovery must distinguish "the content is invalid" (JSON parse failure -> back up and
/// reset) from "the file could not be read right now" (I/O failure -> leave it alone). An I/O error
/// must never be treated as proof that the user's settings are corrupt.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "optisensor-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void MissingFile_CreatesDefaults()
    {
        var settings = SettingsStore.LoadOrCreate(SettingsPath);

        Assert.True(settings.StartWithWindows);
        Assert.False(settings.IntelVrrRangeFixEnabled);
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void InvalidJson_IsBackedUpAndReplacedWithDefaults()
    {
        File.WriteAllText(SettingsPath, "{ this is not valid json ");

        var settings = SettingsStore.LoadOrCreate(SettingsPath);

        Assert.True(settings.StartWithWindows);
        Assert.Single(Directory.GetFiles(_dir, "settings.json.bad.*"));
        // The replacement file is valid JSON again.
        Assert.NotNull(JsonSerializer.Deserialize<object>(File.ReadAllText(SettingsPath)));
    }

    [Fact]
    public void IOException_DoesNotRenameOrReplaceTheExistingFile()
    {
        const string original = "{\"startWithWindows\":false,\"intelVrrRangeFixEnabled\":true}";
        File.WriteAllText(SettingsPath, original);

        // Hold an exclusive handle so File.ReadAllText fails with a sharing violation (IOException).
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => SettingsStore.LoadOrCreate(SettingsPath));
        }

        Assert.True(File.Exists(SettingsPath));
        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.Empty(Directory.GetFiles(_dir, "settings.json.bad.*"));
    }
}
