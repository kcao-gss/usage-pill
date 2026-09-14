using System.IO;
using UsagePill.Settings;

namespace UsagePill.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("usagepill").FullName;
    private string Path_ => Path.Combine(_dir, "settings.json");

    [Fact]
    public void DefaultsMatchTheSpec()
    {
        var settings = new AppSettings();

        Assert.Equal(5, settings.PollIntervalMinutes);
        Assert.Equal(32, settings.RingSizePx);
        Assert.Equal(PillOrientation.Horizontal, settings.Orientation);
        Assert.Equal(75, settings.WarnThresholdPercent);
        Assert.True(settings.Rings.Session);
        Assert.True(settings.Rings.WeeklyAll);
        Assert.True(settings.Rings.WeeklyPerModel);
        Assert.False(settings.ShowResetTimeText);
    }

    [Fact]
    public void NormalizeClampsTheRingSize()
    {
        Assert.Equal(24, new AppSettings { RingSizePx = 8 }.Normalized().RingSizePx);
        Assert.Equal(48, new AppSettings { RingSizePx = 900 }.Normalized().RingSizePx);
    }

    [Fact]
    public void NormalizeForcesTheSessionRingOn()
    {
        var settings = new AppSettings { Rings = new RingSwitches { Session = false } }.Normalized();

        Assert.True(settings.Rings.Session);
    }

    [Fact]
    public void RoundTripsThroughDisk()
    {
        var store = new SettingsStore(Path_);
        store.Save(new AppSettings { RingSizePx = 40, Orientation = PillOrientation.Vertical, ShowResetTimeText = true });

        var loaded = store.Load();

        Assert.Equal(40, loaded.RingSizePx);
        Assert.Equal(PillOrientation.Vertical, loaded.Orientation);
        Assert.True(loaded.ShowResetTimeText);
    }

    [Fact]
    public void LoadReturnsDefaultsWhenTheFileIsMissing()
    {
        Assert.Equal(32, new SettingsStore(Path_).Load().RingSizePx);
    }

    [Fact]
    public void LoadReturnsDefaultsWhenTheFileIsCorrupt()
    {
        File.WriteAllText(Path_, "{ not json");

        Assert.Equal(32, new SettingsStore(Path_).Load().RingSizePx);
    }

    [Fact]
    public void LoadNormalizesWhatItReads()
    {
        File.WriteAllText(Path_, """
        { "ringSizePx": 200, "orientation": "sideways", "rings": { "session": false } }
        """);

        var loaded = new SettingsStore(Path_).Load();

        Assert.Equal(48, loaded.RingSizePx);
        Assert.Equal(PillOrientation.Horizontal, loaded.Orientation);
        Assert.True(loaded.Rings.Session);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
