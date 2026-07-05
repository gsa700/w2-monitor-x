using System.Text.Json;

namespace W2.App.Services;

/// <summary>
/// Persisted state. Slim for the Phase 0 scaffold: window position plus a single
/// port/serial. PHASE 3 TODO: replace Port/Serial with a Meters[] list (one entry per
/// W2, each with its own COM + chip serial) once the multi-meter manager lands.
/// </summary>
public sealed class AppConfig
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public string? Port { get; set; }
    public string? Serial { get; set; }   // FTDI/USB chip serial, so the cable is followed across COM renumbering
    public bool CheckUpdatesAtStartup { get; set; }
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
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path)) ?? new AppConfig();
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
