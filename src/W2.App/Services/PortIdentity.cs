using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace W2.App.Services;

/// <summary>
/// Pins a USB-serial adapter to a stable identity instead of its volatile device name, so a
/// W2 keeps its identity when the OS renumbers ports. The whole thing is expressed as one
/// map {currentPortName -> stableId}; SerialFor/ResolvePort then work identically on every OS.
///
/// - Windows: id = FTDI/USB chip serial (WMI); port = COMx.
/// - Linux/Pi: id = the /dev/serial/by-id/* name (stable per cable); port = the /dev/tty* it
///   currently links to. This is the non-Windows analog of the FTDI pinning.
/// - macOS/other: no map (graceful fallback to the saved port name).
/// </summary>
public static class PortIdentity
{
    private static readonly Regex Ftdi =
        new(@"FTDIBUS\\VID_[0-9A-Fa-f]{4}\+PID_[0-9A-Fa-f]{4}\+([^\\]+)\\", RegexOptions.Compiled);
    private static readonly Regex Usb =
        new(@"USB\\VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}\\([^\\]+)$", RegexOptions.Compiled);
    private static readonly Regex ComName = new(@"\((COM\d+)\)", RegexOptions.Compiled);

    private const string ByIdDir = "/dev/serial/by-id";

    /// <summary>Current port name -> stable cable id, for every port that has one.</summary>
    public static Dictionary<string, string> GetMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (OperatingSystem.IsWindows()) PopulateWindows(map);
            else if (OperatingSystem.IsLinux()) PopulateLinux(map);
        }
        catch { /* WMI/filesystem unavailable — fall back to saved port name */ }
        return map;
    }

    /// <summary>
    /// Each /dev/serial/by-id/* entry is a stable symlink to the volatile /dev/ttyUSB*|ttyACM*.
    /// Map {resolved tty -> by-id name} so the by-id name pins the cable across renumbering.
    /// </summary>
    private static void PopulateLinux(Dictionary<string, string> map)
    {
        if (!Directory.Exists(ByIdDir)) return;
        foreach (var link in Directory.GetFileSystemEntries(ByIdDir))
        {
            var target = File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
            if (target is not null) map[target] = System.IO.Path.GetFileName(link);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void PopulateWindows(Dictionary<string, string> map)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass='Ports'");
        foreach (ManagementBaseObject o in searcher.Get())
        {
            if (o["Name"] is not string name || o["PNPDeviceID"] is not string pnp) continue;
            var cm = ComName.Match(name);
            if (!cm.Success) continue;
            var serial = ExtractSerial(pnp);
            if (serial is not null) map[cm.Groups[1].Value] = serial;
        }
    }

    private static string? ExtractSerial(string pnp)
    {
        var f = Ftdi.Match(pnp);
        if (f.Success) return f.Groups[1].Value;
        var u = Usb.Match(pnp);
        if (u.Success && !u.Groups[1].Value.Contains('&')) return u.Groups[1].Value;  // '&' = location id, not a real serial
        return null;
    }

    /// <summary>Serial of the adapter currently on <paramref name="port"/>, or null.</summary>
    public static string? SerialFor(string port) => GetMap().TryGetValue(port, out var s) ? s : null;

    /// <summary>The COM port that currently hosts <paramref name="serial"/>; falls back to savedPort.</summary>
    public static string? ResolvePort(string? savedPort, string? serial)
    {
        if (string.IsNullOrEmpty(serial)) return savedPort;
        foreach (var kv in GetMap())
            if (string.Equals(kv.Value, serial, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        return savedPort;
    }
}
