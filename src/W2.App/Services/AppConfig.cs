using System.Text.Json;
using W2.App.Settings;
using W2.Core;

namespace W2.App.Services;

/// <summary>Persisted state: window bounds, the meter list, display flags, and misc.</summary>
public sealed class AppConfig
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? SetupX { get; set; }
    public double? SetupY { get; set; }

    public List<MeterConfig> Meters { get; set; } = new();
    public bool CheckUpdatesAtStartup { get; set; }
    public DisplayConfig Display { get; set; } = new();

    // --- legacy single-meter fields (Phase 2 config); migrated on load ---
    public string? Port { get; set; }
    public string? Serial { get; set; }

    public void ApplyTo(DisplaySettings d)
    {
        d.ShowStatusLine = Display.ShowStatusLine;
        d.ShowPowerBar = Display.ShowPowerBar;
        d.ShowSwrBar = Display.ShowSwrBar;
        d.ShowReflected = Display.ShowReflected;
        d.ShowReturnLoss = Display.ShowReturnLoss;
        d.ShowPeak = Display.ShowPeak;
        d.ShowTx = Display.ShowTx;
        d.TimeoutSec = Display.TimeoutSec;
        d.AlwaysOnTop = Display.AlwaysOnTop;
        d.PerMeterWindows = Display.PerMeterWindows;
    }

    public void CaptureFrom(DisplaySettings d)
    {
        Display.ShowStatusLine = d.ShowStatusLine;
        Display.ShowPowerBar = d.ShowPowerBar;
        Display.ShowSwrBar = d.ShowSwrBar;
        Display.ShowReflected = d.ShowReflected;
        Display.ShowReturnLoss = d.ShowReturnLoss;
        Display.ShowPeak = d.ShowPeak;
        Display.ShowTx = d.ShowTx;
        Display.TimeoutSec = d.TimeoutSec;
        Display.AlwaysOnTop = d.AlwaysOnTop;
        Display.PerMeterWindows = d.PerMeterWindows;
    }

    /// <summary>Fold a pre-multi-meter config (single Port/Serial) into the Meters list.</summary>
    public void MigrateLegacy()
    {
        if (Meters.Count == 0 && Port is not null)
        {
            Meters.Add(new MeterConfig { Id = Guid.NewGuid().ToString("N")[..7], Name = "W2 #1", Port = Port, Serial = Serial });
        }
        Port = null;
        Serial = null;
    }
}

public sealed class MeterConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..7];
    public string Name { get; set; } = "W2";
    public string? Port { get; set; }
    public string? Serial { get; set; }

    // Remembered position of this meter's dedicated window (used in per-meter window mode).
    public double? WinX { get; set; }
    public double? WinY { get; set; }
}

/// <summary>Plain (serializable) mirror of <see cref="DisplaySettings"/>.</summary>
public sealed class DisplayConfig
{
    public bool ShowStatusLine { get; set; } = true;
    public bool ShowPowerBar { get; set; } = true;
    public bool ShowSwrBar { get; set; } = true;
    public bool ShowReflected { get; set; } = true;
    public bool ShowReturnLoss { get; set; } = true;
    public bool ShowPeak { get; set; } = true;
    public bool ShowTx { get; set; } = true;
    public int TimeoutSec { get; set; } = 180;
    public bool AlwaysOnTop { get; set; }
    public bool PerMeterWindows { get; set; }
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string Path
    {
        get
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "W2Monitor");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "config.json");
        }
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path)) ?? new AppConfig();
                cfg.MigrateLegacy();
                return cfg;
            }
        }
        catch
        {
            // The file exists but couldn't be read/parsed. Preserve it as config.json.bak instead of
            // silently running with defaults that the next Save would overwrite it with — which would
            // lose the user's meters and serial pinning with no recovery path.
            AtomicFile.Backup(Path);
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        // Atomic (temp + rename): a crash mid-write must not truncate config.json to nothing.
        try { AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(config, Options)); }
        catch { /* best effort */ }
    }
}
