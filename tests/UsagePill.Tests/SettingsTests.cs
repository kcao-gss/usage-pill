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

    [Fact]
    public void LoadSurvivesANullRingsObject()
    {
        File.WriteAllText(Path_, """{ "ringSizePx": 40, "rings": null }""");

        var loaded = new SettingsStore(Path_).Load();

        Assert.NotNull(loaded.Rings);
        Assert.True(loaded.Rings.Session);
        Assert.True(loaded.Rings.WeeklyAll);
        Assert.True(loaded.Rings.WeeklyPerModel);
        Assert.Equal(40, loaded.RingSizePx);
    }

    [Fact]
    public void LoadSurvivesANullWindowObject()
    {
        File.WriteAllText(Path_, """{ "ringSizePx": 40, "window": null }""");

        var loaded = new SettingsStore(Path_).Load();

        Assert.NotNull(loaded.Window);
        Assert.Equal(40, loaded.Window.Left);
        Assert.Equal(40, loaded.Window.Top);
    }

    [Fact]
    public void SaveWritesTheOrientationInLowerCase()
    {
        new SettingsStore(Path_).Save(new AppSettings { Orientation = PillOrientation.Vertical });

        var json = File.ReadAllText(Path_);

        Assert.Contains("\"orientation\": \"vertical\"", json);
        Assert.DoesNotContain("Vertical", json);
    }

    [Fact]
    public void SaveAcceptsAPathWithNoDirectoryPart()
    {
        // A bare file name has an empty directory part whatever the current directory is,
        // so this exercises the guard in Save without touching process-wide state.
        var name = "bare-settings-" + Guid.NewGuid().ToString("n") + ".json";
        try
        {
            new SettingsStore(name).Save(new AppSettings { RingSizePx = 40 });

            Assert.Equal(40, new SettingsStore(name).Load().RingSizePx);
        }
        finally
        {
            File.Delete(name);
        }
    }

    [Fact]
    public void SaveLeavesNoTempFileBehindWhenTheMoveFails()
    {
        // A directory sitting on the target path makes the final move fail.
        Directory.CreateDirectory(Path_);

        // The exact failure type is platform specific; what matters is the cleanup.
        Assert.NotNull(Record.Exception(() => new SettingsStore(Path_).Save(new AppSettings())));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void SaveLeavesNoTempFileBehindOnSuccess()
    {
        new SettingsStore(Path_).Save(new AppSettings());

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
