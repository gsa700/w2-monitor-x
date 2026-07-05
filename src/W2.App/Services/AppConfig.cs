using System.Text.Json;

namespace W2.App.Services;

/// <summary>Persisted state: window bounds, the meter list, and misc flags.</summary>
public sealed class AppConfig
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? SetupX { get; set; }
    public double? SetupY { get; set; }

    public List<MeterConfig> Meters { get; set; } = new();
    public bool CheckUpdatesAtStartup { get; set; }

    // --- legacy single-meter fields (Phase 2 config); migrated on load ---
    public string? Port { get; set; }
    public string? Serial { get; set; }

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
        catch { /* fall through to defaults */ }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        try { File.WriteAllText(Path, JsonSerializer.Serialize(config, Options)); }
        catch { /* best effort */ }
    }
}
