using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using W2.Core;

namespace W2.App.Services;

/// <summary>What an uninstall should take with it besides the program itself.</summary>
/// <param name="RemoveSettings">
/// Delete <c>config.json</c> (and its <c>.bak</c>). Defaults to false at every call site: settings
/// are cheap to recreate but hold the meter list and each cable's chip-serial pinning, which is
/// fiddly enough to redo that silently discarding it would be rude.
/// </param>
public readonly record struct UninstallOptions(bool RemoveSettings);

/// <summary>Outcome of an install.</summary>
/// <param name="ExePath">The installed executable.</param>
/// <param name="Registered">
/// Whether the desktop registration is verifiably in place. The install itself succeeded either
/// way — the program is copied and runs — but when this is false it will not appear in Settings →
/// Apps → Installed apps, which is the only route most people have to uninstall it. Worth telling
/// the user about rather than reporting a clean install.
/// </param>
public readonly record struct InstallResult(string ExePath, bool Registered);

/// <summary>
/// An install could not proceed for a reason the user can act on — almost always because the
/// installed copy is still running. Carries a message meant to be shown as-is.
/// </summary>
public sealed class InstallBlockedException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Installs and removes the per-user copy of the app. Ported from LP-100A Monitor, which
/// established this shape for the station tools; the differences are noted where they occur.
///
/// Per-user by necessity, not preference: <see cref="UpdateService.ApplyAndRestart"/> replaces the
/// running executable in place, which needs no elevation under %LOCALAPPDATA% and would need it on
/// every single update under Program Files. A machine-wide install would quietly break the updater.
///
/// The registry work shells out to <c>reg.exe</c> rather than using <c>Microsoft.Win32.Registry</c>.
/// The app targets plain <c>net10.0</c> so it can cross-publish Linux and Raspberry Pi builds from
/// one TFM, and the registry APIs only ship in <c>net10.0-windows</c>; the standalone package is
/// deprecated and stuck at 5.0.0. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>,
/// so paths with spaces need no hand-quoting. Same "Windows-only, guarded at runtime" shape the
/// WMI adapter-serial code in <see cref="PortIdentity"/> already uses.
/// </summary>
public static class InstallService
{
    /// <summary>
    /// Registry key under HKCU that puts the app in Settings → Apps → Installed apps. Spelled with
    /// the full hive name because the <c>.reg</c> file format requires it; <c>reg.exe</c> accepts
    /// either form on its command line, so one constant serves both.
    /// </summary>
    private const string UninstallKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\W2Monitor";

    /// <summary>Display name, used for the installed-apps entry and the Start Menu shortcut.</summary>
    public const string DisplayName = "W2 Monitor";

    private const string Description = "Monitor for the Elecraft W2 RF power / SWR meter";

    public static string ExeFileName => OperatingSystem.IsWindows() ? "W2Monitor.exe" : "W2Monitor";

    /// <summary>Full path of the running executable.</summary>
    public static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot determine the current executable path.");

    public static string ExeDirectory => Path.GetDirectoryName(ExePath)!;

    /// <summary>
    /// Per-user programs directory: <c>%LOCALAPPDATA%\Programs</c> on Windows,
    /// <c>~/.local/share</c> on Linux. <see cref="Environment.SpecialFolder.LocalApplicationData"/>
    /// already resolves to the right base on both; only Windows wants the extra <c>Programs</c>
    /// level, because <c>~/.local/share</c> is itself where per-user application data belongs.
    /// </summary>
    public static string ProgramsDirectory
    {
        get
        {
            var b = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return OperatingSystem.IsWindows() ? Path.Combine(b, "Programs") : b;
        }
    }

    private static string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Where the menu entry goes: <c>~/.local/share/applications</c>.</summary>
    private static string DesktopFilePath =>
        Path.Combine(ProgramsDirectory, "applications", DesktopEntry.FileName);

    /// <summary>Icon path in the XDG hicolor theme, at the 256px size the embedded PNG carries.</summary>
    private static string IconFilePath => Path.Combine(
        ProgramsDirectory, "icons", "hicolor", "256x256", "apps", "w2-monitor.png");

    /// <summary>
    /// Convenience symlink so <c>w2-monitor</c> works from a terminal. <c>~/.local/bin</c> is on
    /// PATH by default on Raspberry Pi OS and most desktop distributions.
    /// </summary>
    private static string SymlinkPath =>
        Path.Combine(HomeDirectory, ".local", "bin", "w2-monitor");

    public static string InstallDirectory => InstallLayout.InstallDirectoryUnder(ProgramsDirectory);

    public static string InstalledExePath => Path.Combine(InstallDirectory, ExeFileName);

    /// <summary>Directories accepted as installed — the canonical one plus pre-installer hand-installs.</summary>
    public static IEnumerable<string> InstalledDirectories =>
        InstallLayout.InstalledDirectoriesUnder(ProgramsDirectory);

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", DisplayName + ".lnk");

    /// <summary>How this copy is running. Derived from its path every time — never cached or stored.</summary>
    public static InstallMode Mode => InstallLayout.Detect(
        ExeDirectory,
        File.Exists(Path.Combine(ExeDirectory, InstallLayout.PortableMarker)),
        InstallDirectory,
        InstalledDirectories);

    /// <summary>
    /// Copy this executable into the install directory and register it. Returns the path of the
    /// installed copy, which the caller should launch before exiting.
    /// </summary>
    /// <remarks>
    /// Copying only the executable is sufficient because the published build is self-contained and
    /// single-file — there is no payload beside it to keep in step. Settings already live in
    /// <see cref="ConfigStore.DataDir"/>, so an install picks up whatever was there before and an
    /// uninstall can leave it behind.
    /// </remarks>
    public static InstallResult Install()
    {
        Directory.CreateDirectory(InstallDirectory);

        var target = InstalledExePath;
        if (!InstallLayout.SamePath(ExeDirectory, InstallDirectory))
        {
            try
            {
                File.Copy(ExePath, target, overwrite: true);
            }
            catch (IOException ex)
            {
                // Running a newly downloaded copy while the installed one is still open is an
                // ordinary thing to do, and Windows will not let the open executable be replaced.
                // Say that, rather than surfacing a raw sharing violation from File.Copy.
                throw new InstallBlockedException(
                    $"{DisplayName} is already running from the install folder. "
                    + "Close it and try installing again.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InstallBlockedException(
                    $"Could not write to {InstallDirectory}. Check the folder's permissions.", ex);
            }
        }

        // A copied executable arrives without its executable bit on Unix; without this the
        // installed copy and the menu entry both silently fail to launch.
        if (!OperatingSystem.IsWindows()) MakeExecutable(target);

        return new InstallResult(target, Register(target));
    }

    /// <summary>
    /// Register the copy at <paramref name="exePath"/> with the desktop environment: an
    /// installed-apps entry and Start Menu shortcut on Windows, a <c>.desktop</c> entry, icon and
    /// <c>~/.local/bin</c> symlink on Linux. Safe to call repeatedly — everything overwrites rather
    /// than duplicating.
    /// </summary>
    /// <returns>Whether the registration is verifiably in place afterwards.</returns>
    public static bool Register(string exePath) => Register(exePath, "install");

    /// <summary>
    /// Register, and record what happened. <paramref name="trigger"/> is what prompted the attempt —
    /// <c>startup</c>, <c>install</c> or <c>update</c> — which is the field that matters, because the
    /// fault being chased is specific to one of them.
    /// </summary>
    /// <remarks>
    /// Every path through here writes exactly one line, including the paths that previously returned
    /// false or threw with nothing to show for it. That is the point: an attempt that leaves no trace
    /// is indistinguishable from one that never happened, which is what made this take a registry
    /// last-write timestamp to diagnose (BACKLOG, 2026-07-31).
    /// </remarks>
    public static bool Register(string exePath, string trigger)
    {
        var detail = "";
        var ok = false;
        try
        {
            ok = OperatingSystem.IsWindows()
                ? RegisterWindows(exePath, out detail)
                : RegisterUnix(exePath, out detail);
        }
        catch (Exception ex)
        {
            // Nothing here rethrows: registration has never been worth failing an install or a
            // startup over. It is worth *recording*, which is the part that was missing.
            detail = $"threw {ex.GetType().Name}: {ex.Message}";
        }
        RecordAttempt(trigger, ok, detail);
        return ok;
    }

    /// <summary>The most recent attempt this process made, or the last one on file if it hasn't tried yet.</summary>
    public static RegistrationAttempt? LastAttempt
    {
        get
        {
            if (_lastAttempt is not null) return _lastAttempt;
            try
            {
                if (!File.Exists(LogFilePath)) return null;
                return File.ReadAllLines(LogFilePath)
                    .Select(RegistrationLog.Parse)
                    .LastOrDefault(a => a is not null);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }

    private static RegistrationAttempt? _lastAttempt;

    /// <summary>Audit trail of registration attempts, beside the config it diagnoses alongside.</summary>
    public static string LogFilePath => Path.Combine(ConfigStore.DataDir, "registration.log");

    private static void RecordAttempt(string trigger, bool succeeded, string detail)
    {
        var attempt = new RegistrationAttempt(
            DateTime.UtcNow, UpdateService.CurrentVersion, trigger, succeeded, detail);
        _lastAttempt = attempt;

        try
        {
            var existing = File.Exists(LogFilePath) ? File.ReadAllLines(LogFilePath) : [];
            var kept = RegistrationLog.Tail(existing.Append(RegistrationLog.Format(attempt)));
            File.WriteAllLines(LogFilePath, kept);
        }
        catch (IOException) { /* the in-memory copy still reaches the UI */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <returns>Whether the desktop entry is on disk afterwards — the icon and the
    /// <c>~/.local/bin</c> symlink are conveniences, but without the entry there is no menu item.</returns>
    /// <remarks>
    /// Runs on every launch now, so each step skips itself when the result is already correct. The
    /// cost that mattered was <c>update-desktop-database</c>, which is a process spawn and a directory
    /// rebuild; it now runs only when the entry's contents actually changed.
    /// </remarks>
    private static bool RegisterUnix(string exePath, out string detail)
    {
        var steps = new List<string>();
        // Write the icon first: the entry should not reference a file that isn't there yet. Only if
        // it's missing — the bytes are baked into this build and can't have drifted.
        string? icon = null;
        try
        {
            if (File.Exists(IconFilePath))
            {
                icon = IconFilePath;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IconFilePath)!);
                using var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("app-icon.png");
                if (src is not null)
                {
                    using var dst = File.Create(IconFilePath);
                    src.CopyTo(dst);
                    icon = IconFilePath;
                }
            }
        }
        catch (IOException) { /* an entry without an icon still launches */ }
        catch (UnauthorizedAccessException) { }
        if (icon is null) steps.Add("no icon");

        var wanted = DesktopEntry.Build(DisplayName, exePath, icon, Description);
        var current = TryReadAllText(DesktopFilePath);
        if (current != wanted)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DesktopFilePath)!);
            File.WriteAllText(DesktopFilePath, wanted);
            steps.Add("entry rewritten");

            // Some environments only notice a changed entry once the database is rebuilt; others watch
            // the directory. Best effort, and harmless where the tool isn't installed.
            Run("update-desktop-database", [Path.GetDirectoryName(DesktopFilePath)!]);
        }
        else
        {
            steps.Add("entry already current");
        }

        try
        {
            // Replaces the link only if it's missing or aimed somewhere else — an install directory
            // that moved, say. Note this catch guards only the *creation*: probing for an absent link
            // used to throw FileNotFoundException from in here, which — being an IOException — landed
            // in the handler below and skipped the create, so on Linux the symlink was never made on
            // any launch. Symlink.ResolveTarget answers "no link" with null for that reason.
            Symlink.Ensure(SymlinkPath, exePath);
        }
        catch (IOException ex) { steps.Add($"symlink failed: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { steps.Add($"symlink failed: {ex.Message}"); }

        var ok = File.Exists(DesktopFilePath);
        if (!ok) steps.Add("no .desktop entry on disk afterwards");
        detail = string.Join("; ", steps);
        return ok;
    }

    private static string? TryReadAllText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Write the installed-apps entry and the Start Menu shortcut. Returns whether the entry is
    /// really there afterwards.
    /// </summary>
    /// <remarks>
    /// Verified and retried rather than written once and assumed. This failed in the field on
    /// LP-100A and left no trace: every value goes through a separate reg.exe, and a spawn that does
    /// not take produces no error, no log line and no visible difference from success. The install
    /// still copied the program and still launched — it simply did not appear in Settings → Apps →
    /// Installed apps, which is the one route a user has to remove it.
    /// </remarks>
    private static bool RegisterWindows(string exePath, out string detail)
    {
        detail = "";
        if (!OperatingSystem.IsWindows()) { detail = "not Windows"; return false; }

        var dir = Path.GetDirectoryName(exePath)!;

        var wrote = WriteUninstallEntry(exePath, dir, out var importExit);
        var verified = wrote && IsRegistered();
        var retried = false;
        if (!verified)
        {
            retried = true;
            Thread.Sleep(250);
            wrote = WriteUninstallEntry(exePath, dir, out importExit);
            verified = wrote && IsRegistered();
        }

        CreateShortcut(StartMenuShortcut, exePath, dir, Description);

        detail = $"reg import exit {importExit}{(retried ? ", after retry" : "")}" +
                 (verified ? "" : wrote ? ", but the verify query found no entry" : "");
        return verified;
    }

    /// <summary>
    /// Write the whole installed-apps entry in one <c>reg import</c>. Returns whether reg.exe said it
    /// worked.
    /// </summary>
    /// <remarks>
    /// One invocation rather than one per value, deliberately. Eleven separate <c>reg add</c> spawns
    /// are eleven things that can individually fail to happen, and a spawn that silently doesn't take
    /// is indistinguishable from one that did — which is the shape of the entry that was written and
    /// then found missing (BACKLOG, 2026-07-30). It also makes the whole registration one action for a
    /// security product to allow or block instead of eleven, and cheap enough to repeat on every
    /// launch, which is what lets a lost entry heal itself.
    ///
    /// The file goes to the temp directory as UTF-16 with a BOM — <c>reg import</c> reads ANSI too,
    /// but only Unicode survives a user profile path with non-ASCII characters in it.
    /// </remarks>
    private static bool WriteUninstallEntry(string exePath, string dir, out int importExit)
    {
        importExit = -1;
        var values = new List<RegValue>
        {
            RegFile.Sz("DisplayName", DisplayName),
            RegFile.Sz("DisplayVersion", UpdateService.CurrentVersion),
            RegFile.Sz("Publisher", "David Erickson (AB0R)"),
            RegFile.Sz("DisplayIcon", exePath),
            RegFile.Sz("InstallLocation", dir),
            RegFile.Sz("URLInfoAbout", $"https://github.com/{UpdateService.Repo}"),

            // Windows gives the user no way to answer a dialog it did not expect, so the entry's own
            // button runs the quiet path — which keeps the settings.
            RegFile.Sz("UninstallString", $"\"{exePath}\" --uninstall"),
            RegFile.Sz("QuietUninstallString", $"\"{exePath}\" --uninstall --quiet"),

            RegFile.Dword("NoModify", 1),
            RegFile.Dword("NoRepair", 1),
        };

        var sizeKb = FileSizeKb(exePath);
        if (sizeKb > 0) values.Add(RegFile.Dword("EstimatedSize", sizeKb));

        var file = Path.Combine(Path.GetTempPath(), "w2monitor-register.reg");
        try
        {
            File.WriteAllText(file, RegFile.Build(UninstallKey, values), new UnicodeEncoding(false, true));
            importExit = Run(RegExe, ["import", file]);
            return importExit == 0;
        }
        // -2 and -3 mark "never got as far as reg.exe", which reads differently from an exit code.
        catch (IOException) { importExit = -2; return false; }
        catch (UnauthorizedAccessException) { importExit = -3; return false; }
        finally
        {
            try { File.Delete(file); } catch { /* a leftover in temp is not worth failing over */ }
        }
    }

    /// <summary>
    /// Called at every startup. Adopts a copy that was put in an install directory by hand before
    /// there was an installer — registering it where it stands rather than copying it to the
    /// canonical folder, which would leave the original behind as an orphan — and re-asserts the
    /// registration of an already-installed copy.
    /// </summary>
    /// <remarks>
    /// This used to return early when <see cref="IsRegistered"/> was true, and that is exactly why a
    /// lost entry stayed lost: the check runs once at startup, the installed copy is launched
    /// immediately after a *successful* registration, so it saw the entry present and skipped — and
    /// nothing looks again for the rest of the process's life (BACKLOG, 2026-07-30). Re-asserting
    /// costs one <c>reg import</c> on Windows, and <see cref="RegisterUnix"/> skips the work whose
    /// result is already correct, so the price of self-healing is small enough to pay every launch.
    /// </remarks>
    /// <param name="trigger">
    /// What prompted this launch. The app passes <c>update</c> when it has just been replaced in
    /// place, because that is the launch on which registration has been observed to go missing, and a
    /// log that doesn't distinguish it from an ordinary start would not have caught it.
    /// </param>
    public static void EnsureRegistered(string trigger = "startup")
    {
        if (Mode != InstallMode.Installed)
        {
            RecordAttempt(trigger, false, $"skipped: mode is {Mode}, not Installed");
            return;
        }
        Register(ExePath, trigger);
    }

    /// <summary>
    /// Whether the desktop environment already knows about this copy — an installed-apps entry on
    /// Windows, a <c>.desktop</c> file on Linux.
    /// </summary>
    public static bool IsRegistered() => OperatingSystem.IsWindows()
        ? Run(RegExe, ["query", UninstallKey, "/v", "DisplayName"]) == 0
        : File.Exists(DesktopFilePath);

    /// <summary>
    /// Remove the registrations, then hand off to a detached helper that deletes the install
    /// directory once this process has exited. The caller must exit immediately after.
    /// </summary>
    /// <remarks>
    /// A running executable cannot delete itself, which is the same constraint
    /// <see cref="UpdateService.ApplyAndRestart"/> works around; this uses the same trampoline
    /// shape. The helper is written to the temp directory rather than the install directory,
    /// because the install directory is what it is about to remove.
    /// </remarks>
    public static void Uninstall(UninstallOptions options)
    {
        Unregister();

        var toDelete = new List<string>();

        // Only ever remove a directory the app owns. Mode is derived from the path, so a copy being
        // run from a download folder is Loose and its directory is not ours — and if someone
        // extracted the exe straight into Downloads, ExeDirectory *is* Downloads. Deleting it
        // recursively because the user asked to uninstall a program would be indefensible. An
        // Installed copy's directory is private to the app (canonical or an adopted legacy folder),
        // so that one can go whole; a shared directory such as ~/.local/bin never can, which is why
        // Unregister removes its items one file at a time.
        // (Divergence from LP-100A, which deletes ExeDirectory unconditionally — worth porting back.)
        if (Mode == InstallMode.Installed) toDelete.Add(ExeDirectory);

        toDelete.AddRange(DataFilesToRemove(options));

        var pid = Environment.ProcessId;

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "w2monitor-uninstall.ps1");
            var lines = new List<string>
            {
                $"while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}",
            };
            lines.AddRange(toDelete.Select(p =>
                $"Remove-Item -LiteralPath '{p.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue"));
            // Take the helper with it, so an uninstall doesn't leave its own tooling behind in temp.
            lines.Add($"Remove-Item -LiteralPath '{script.Replace("'", "''")}' -Force -ErrorAction SilentlyContinue");

            File.WriteAllText(script, string.Join("\n", lines) + "\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            var script = Path.Combine(Path.GetTempPath(), "w2monitor-uninstall.sh");
            var lines = new List<string>
            {
                "#!/bin/sh",
                $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done",
            };
            lines.AddRange(toDelete.Select(p => $"rm -rf {ShellQuote(p)}"));
            lines.Add($"rm -f {ShellQuote(script)}");

            File.WriteAllText(script, string.Join("\n", lines) + "\n");
            MakeExecutable(script);
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { script },
                UseShellExecute = false,
            });
        }
    }

    /// <summary>
    /// Wrap a path in single quotes for /bin/sh, closing and reopening around any single quote it
    /// contains. Paths come from the environment, so they are not assumed to be tame.
    /// </summary>
    private static string ShellQuote(string path) => "'" + path.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Which files under the data directory an uninstall should take. The directory itself is never
    /// removed wholesale — only the files actually consented to are listed, so anything a future
    /// version puts there is not swept up by an uninstall that predates it.
    /// </summary>
    public static IEnumerable<string> DataFilesToRemove(UninstallOptions options)
    {
        if (!options.RemoveSettings) yield break;

        var config = ConfigStore.ConfigFilePath;
        if (File.Exists(config)) yield return config;

        // AtomicFile keeps the last readable config beside it when a load fails; leaving that behind
        // would resurrect the meter list on the next install.
        var bak = config + ".bak";
        if (File.Exists(bak)) yield return bak;
    }

    private static void Unregister()
    {
        if (OperatingSystem.IsWindows())
        {
            Run(RegExe, ["delete", UninstallKey, "/f"]);
            TryDelete(StartMenuShortcut);
            return;
        }

        // Each removed as a single file. ~/.local/bin and the icon theme are shared directories:
        // nothing here may delete a directory it does not own.
        TryDelete(DesktopFilePath);
        TryDelete(IconFilePath);
        TryDelete(SymlinkPath);
        Run("update-desktop-database", [Path.GetDirectoryName(DesktopFilePath)!]);
    }

    private static void TryDelete(string path)
    {
        try
        {
            // Ask the link itself as well as File.Exists: whether File.Exists follows a dangling
            // symlink varies by runtime (measured true on .NET 10 / linux-arm64, false on the
            // runtime this was first written against), so neither question alone reliably finds a
            // link whose target is already gone.
            if (File.Exists(path) || Symlink.ResolveTarget(path) is not null)
                File.Delete(path);
        }
        catch (IOException) { /* a locked or vanished file is not worth failing an uninstall over */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Launch a copy of the app detached from this process.</summary>
    /// <remarks>
    /// The working directory is set explicitly, and must stay that way. A child process otherwise
    /// inherits this one's, and after an install that is the folder the user just installed FROM —
    /// which Windows then refuses to delete, because a live process's current directory cannot be
    /// removed. The install appears to finish and the download folder becomes undeletable for as
    /// long as the app runs, with nothing on screen connecting the two.
    /// </remarks>
    public static void LaunchDetached(string exePath) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = true,
        });

    private static int FileSizeKb(string path)
    {
        try { return (int)(new FileInfo(path).Length / 1024); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    /// <summary>
    /// Absolute path to reg.exe. Resolving it by bare name defers to the process's PATH, which is
    /// one more thing that can differ between the contexts this runs in for no good reason.
    /// </summary>
    private static string RegExe => Path.Combine(Environment.SystemDirectory, "reg.exe");

    /// <summary>Run a console tool with no window and return its exit code (-1 if it wouldn't start).</summary>
    private static int Run(string fileName, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }

    /// <summary>
    /// Write a .lnk via Windows Script Host. Reached by reflection rather than a <c>dynamic</c>
    /// call so nothing depends on the C# runtime binder being present in a single-file build.
    /// A missing shortcut is not worth failing an install over, so every failure here is swallowed.
    /// </summary>
    private static void CreateShortcut(string lnkPath, string target, string workingDirectory, string description)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;

            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);

            var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [lnkPath]);
            if (link is null) return;

            var linkType = link.GetType();
            void Set(string property, object value) =>
                linkType.InvokeMember(property, BindingFlags.SetProperty, null, link, [value]);

            Set("TargetPath", target);
            Set("WorkingDirectory", workingDirectory);
            Set("IconLocation", target + ",0");
            Set("Description", description);
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        catch (Exception)
        {
            // Windows Script Host can be disabled by policy. The app is fully usable without a
            // Start Menu entry, so this must not take the install down with it.
        }
    }
}
