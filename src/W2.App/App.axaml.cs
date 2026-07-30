using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using W2.App.Services;
using W2.App.Settings;
using W2.App.ViewModels;
using W2.App.Views;
using W2.Core;

namespace W2.App;

public partial class App : Application
{
    private AppConfig _config = new();
    private DisplaySettings _display = null!;
    private MeterManager _manager = null!;
    private SetupViewModel _setupVm = null!;

    // Window mode: either one auto-focus window, or a dedicated window per meter.
    private MainWindow? _focusWindow;
    private readonly Dictionary<string, MainWindow> _meterWindows = new();
    private SetupWindow? _setupWindow;

    // Set while we close meter windows ourselves (mode switch, meter removal, update exit) so the
    // user-close cascade in NotifyMainWindowClosing doesn't fire — those closes must not quit the app.
    private bool _programmaticClose;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _config = ConfigStore.Load();
            _display = new DisplaySettings();
            _config.ApplyTo(_display);

            var simulated = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--sim", StringComparison.OrdinalIgnoreCase));

            _manager = new MeterManager(simulated);
            _setupVm = new SetupViewModel(_manager, _display)
            {
                CheckUpdatesAtStartup = _config.CheckUpdatesAtStartup,
                SelectedTabIndex = _config.SetupTab,   // the setter clamps a stale or hand-edited value
            };

            // A copy installed by hand before there was an installer is adopted where it stands, so
            // it appears in Installed apps without being copied to a second location.
            if (!simulated)
                try { InstallService.EnsureRegistered(); } catch { /* never block startup over this */ }

            if (simulated) BuildSimMeters();
            else RestoreMeters();

            // Per-meter mode → one window per meter; otherwise the single auto-focus window.
            List<MainWindow> windows = PerMeter
                ? _manager.Meters.Select(CreateMeterWindow).ToList()
                : new List<MainWindow> { CreateFocusWindow() };

            desktop.MainWindow = windows[0];
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            _display.PropertyChanged += OnDisplayChanged;
            _manager.MetersChanged += OnMetersChanged;
            desktop.Exit += (_, _) => { SaveConfig(); _manager.Dispose(); };

            // A previous in-app update whose file swap failed left a marker and relaunched the old exe.
            var updateFailed = !simulated && UpdateService.ConsumeUpdateFailed();
            if (updateFailed) _setupVm.NoteUpdateFailed();

            var openSetup = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase));
            windows[0].Opened += async (_, _) =>
            {
                // This run exists only to uninstall: ask, act, and go. Nothing else should start.
                if (Program.PendingUninstall)
                {
                    await RunUninstallAsync();
                    return;
                }

                for (var i = 1; i < windows.Count; i++) windows[i].Show();   // the primary is auto-shown

                // A copy running from wherever it was unzipped offers to install itself.
                if (!simulated && InstallService.Mode == InstallMode.Loose && await OfferInstallAsync())
                    return;

                // Opening Setup to say something about an update lands on the Updates tab — otherwise
                // it would open on whichever tab was last used and the reason wouldn't be on screen.
                if (updateFailed) ShowSetup(SetupViewModel.UpdatesTab);
                else if (openSetup) ShowSetup();

                if (_config.CheckUpdatesAtStartup)
                {
                    await _setupVm.CheckUpdatesAsync();
                    if (_setupVm.UpdateAvailable) ShowSetup(SetupViewModel.UpdatesTab);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>True when per-meter window mode should be active (needs at least one meter).</summary>
    private bool PerMeter => _display.PerMeterWindows && _manager.Meters.Count > 0;

    private void BuildSimMeters()
    {
        foreach (var name in new[] { "W2 #1 (sim)", "W2 #2 (sim)" })
        {
            var m = _manager.Add(name);
            m.Port = "SIM";
            m.Connect();
        }
    }

    private void RestoreMeters()
    {
        foreach (var mc in _config.Meters)
        {
            var m = _manager.Add(mc.Name, mc.Port, mc.Serial, mc.Id);
            var port = PortIdentity.ResolvePort(mc.Port, mc.Serial);
            if (port is not null)
            {
                m.Port = port;
                // Don't take the serial ports for a run that exists only to uninstall.
                if (!Program.PendingUninstall && MeterService.GetPortNames().Contains(port)) m.Connect();
            }
        }
    }

    // --- window mode ---

    private MainWindow CreateFocusWindow()
    {
        var w = new MainWindow { DataContext = new MainWindowViewModel(_manager, _display), Topmost = _display.AlwaysOnTop };
        RestoreBounds(w, _config.X, _config.Y);
        _focusWindow = w;
        return w;
    }

    private MainWindow CreateMeterWindow(MeterService m)
    {
        var w = new MainWindow { DataContext = new MainWindowViewModel(m, _display), Topmost = _display.AlwaysOnTop };
        if (_manager.IsSimulated) RestoreBounds(w, null, null);
        else { var c = ConfigFor(m.Id); RestoreBounds(w, c.WinX, c.WinY); }
        _meterWindows[m.Id] = w;
        return w;
    }

    /// <summary>Reconcile the open windows to the current mode. Opens the new set before closing the
    /// old, so we never momentarily have zero windows (which would exit the app).</summary>
    private void ApplyWindowMode()
    {
        if (PerMeter)
        {
            foreach (var m in _manager.Meters)
                if (!_meterWindows.ContainsKey(m.Id)) CreateMeterWindow(m).Show();
            _focusWindow?.Close();
        }
        else
        {
            if (_focusWindow is null) CreateFocusWindow().Show();
            _programmaticClose = true;
            foreach (var w in _meterWindows.Values.ToList()) w.Close();
            _programmaticClose = false;
        }
    }

    private void OnMetersChanged()
    {
        // In per-meter mode, follow the meter list: add a window for a new meter, drop one for a removed meter.
        if (!PerMeter) return;
        foreach (var m in _manager.Meters)
            if (!_meterWindows.ContainsKey(m.Id)) CreateMeterWindow(m).Show();
        // Programmatic: dropping one removed meter's window must not cascade-close the others.
        _programmaticClose = true;
        foreach (var id in _meterWindows.Keys.Where(id => _manager.Meters.All(m => m.Id != id)).ToList())
            _meterWindows[id].Close();
        _programmaticClose = false;
    }

    public void NotifyMainWindowClosing(MainWindow w)
    {
        if (ReferenceEquals(w, _focusWindow))
        {
            _config.X = w.Position.X;
            _config.Y = w.Position.Y;
            _focusWindow = null;
        }
        else
        {
            var id = _meterWindows.FirstOrDefault(kv => ReferenceEquals(kv.Value, w)).Key;
            if (id is not null)
            {
                if (!_manager.IsSimulated) { var c = ConfigFor(id); c.WinX = w.Position.X; c.WinY = w.Position.Y; }
                _meterWindows.Remove(id);

                // Per-meter windows are one logical app view — a user closing any one closes the rest
                // (and, via OnLastWindowClose, exits) rather than leaving orphans with no way to reopen.
                // Skipped for our own programmatic closes (removing a single meter, switching modes).
                if (PerMeter && !_programmaticClose)
                {
                    _programmaticClose = true;
                    foreach (var other in _meterWindows.Values.ToList()) other.Close();
                    _programmaticClose = false;
                }
            }
        }
        SaveConfig();
    }

    // --- Setup window ---

    /// <param name="tab">Tab to select first, for when Setup is opened to show something specific.</param>
    public void ShowSetup(int? tab = null)
    {
        if (tab is not null) _setupVm.SelectedTabIndex = tab.Value;

        if (_setupWindow is null)
        {
            _setupWindow = new SetupWindow { DataContext = _setupVm, Topmost = _display.AlwaysOnTop };
            RestoreBounds(_setupWindow, _config.SetupX, _config.SetupY);
            _setupWindow.Show();
        }
        else
        {
            _setupWindow.Show();
        }
        _setupWindow.Activate();
    }

    public void NotifySetupClosing(SetupWindow w)
    {
        _config.SetupX = w.Position.X;
        _config.SetupY = w.Position.Y;
        _setupWindow = null;
    }

    /// <summary>Close every window so the staged update helper can swap the executable and relaunch.</summary>
    public void ExitForUpdate() => CloseAllWindows();

    /// <summary>
    /// Close every window, which exits the app under <see cref="ShutdownMode.OnLastWindowClose"/>.
    /// </summary>
    private void CloseAllWindows()
    {
        _programmaticClose = true;   // every window is closing anyway; don't run the per-meter cascade
        foreach (var w in AllWindows().ToList()) w.Close();
    }

    /// <summary>Modal confirmation, owned by whatever window is available.</summary>
    /// <param name="negative">Null for a one-button message that only reports an outcome.</param>
    public Task<bool> ConfirmAsync(string title, string message,
        string affirmative = "Continue", string? negative = "Cancel", string? detail = null)
    {
        var owner = (Window?)_setupWindow ?? _focusWindow ?? _meterWindows.Values.FirstOrDefault();
        var dlg = new ConfirmWindow(title, message, affirmative, negative, detail) { Topmost = _display.AlwaysOnTop };
        return owner is not null ? dlg.ShowDialog<bool>(owner) : Task.FromResult(false);
    }

    private Task NotifyAsync(string title, string message, string? detail = null) =>
        ConfirmAsync(title, message, affirmative: "OK", negative: null, detail: detail);

    // --- install / uninstall ---

    /// <summary>
    /// Offer to install a copy that's running from wherever it was unzipped. Returns true when the
    /// install happened and this process is handing over to the installed copy.
    /// </summary>
    private async Task<bool> OfferInstallAsync()
    {
        var accepted = await ConfirmAsync(
            $"Install {InstallService.DisplayName}",
            $"Install {InstallService.DisplayName} on this computer?",
            affirmative: "Install",
            negative: "Not now",
            detail: $"Copies the program to {InstallService.InstallDirectory} and lists it in "
                  + "Settings → Apps → Installed apps, with a Start Menu shortcut. Your meters and "
                  + "settings are untouched either way.\n\n"
                  + "To run from here permanently without being asked again, put a file named "
                  + $"{InstallLayout.PortableMarker} beside the program.");

        if (!accepted) return false;

        try
        {
            var installed = InstallService.Install();

            // Installed but not listed is a real outcome, not a detail: the program works, yet the
            // usual way to remove it is missing. Say so here rather than report a clean install and
            // leave it to be discovered later in Settings.
            if (!installed.Registered)
            {
                await NotifyAsync(
                    "Installed, but not listed",
                    $"{InstallService.DisplayName} was installed to {InstallService.InstallDirectory}, "
                    + "but could not be added to Settings → Apps → Installed apps.",
                    "The program itself works normally — only the usual way to uninstall it is "
                    + "missing. Installing again often clears it.");
            }

            InstallService.LaunchDetached(installed.ExePath);
            CloseAllWindows();
            return true;
        }
        catch (InstallBlockedException ex)
        {
            await NotifyAsync("Could not install", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            await NotifyAsync("Could not install", $"The install did not complete: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The interactive <c>--uninstall</c> path, reached from the installed-apps entry. Asks what to
    /// do about the settings, hands the deletion to a detached helper, and exits.
    /// </summary>
    private async Task RunUninstallAsync()
    {
        var confirmed = await ConfirmAsync(
            $"Remove {InstallService.DisplayName}",
            $"Remove {InstallService.DisplayName} from this computer?",
            affirmative: "Remove",
            negative: "Cancel",
            detail: $"Deletes the program from {InstallService.InstallDirectory}.");

        if (!confirmed) { CloseAllWindows(); return; }

        // Asked separately, and declining is the safe button: the settings hold the meter list and
        // each cable's chip-serial pinning, which is fiddly enough to redo that it shouldn't go
        // along with the program by default. Closing the dialog also means "keep".
        var removeSettings = await ConfirmAsync(
            "Remove settings too?",
            "Also delete your saved settings?",
            affirmative: "Delete settings",
            negative: "Keep settings",
            detail: "The meter list, each cable's chip serial, window positions and display options, "
                  + $"in {ConfigStore.DataDir}. Keeping them means a later install picks up where "
                  + "you left off.");

        try { InstallService.Uninstall(new UninstallOptions(removeSettings)); }
        catch (Exception ex) { await NotifyAsync("Could not uninstall", ex.Message); }

        CloseAllWindows();
    }

    private IEnumerable<Window> AllWindows()
    {
        if (_focusWindow is not null) yield return _focusWindow;
        foreach (var w in _meterWindows.Values) yield return w;
        if (_setupWindow is not null) yield return _setupWindow;
    }

    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DisplaySettings.AlwaysOnTop):
                foreach (var w in AllWindows()) w.Topmost = _display.AlwaysOnTop;
                break;
            case nameof(DisplaySettings.PerMeterWindows):
                ApplyWindowMode();
                break;
        }
    }

    // --- config ---

    private MeterConfig ConfigFor(string id)
    {
        var c = _config.Meters.FirstOrDefault(x => x.Id == id);
        if (c is null) { c = new MeterConfig { Id = id }; _config.Meters.Add(c); }
        return c;
    }

    private static void RestoreBounds(Window w, double? x, double? y)
    {
        if (x is not null && y is not null)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)x.Value, (int)y.Value);
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void SaveConfig()
    {
        try
        {
            if (_focusWindow is not null) { _config.X = _focusWindow.Position.X; _config.Y = _focusWindow.Position.Y; }
            if (_setupWindow is not null) { _config.SetupX = _setupWindow.Position.X; _config.SetupY = _setupWindow.Position.Y; }

            if (!_manager.IsSimulated)
            {
                foreach (var (id, w) in _meterWindows) { var c = ConfigFor(id); c.WinX = w.Position.X; c.WinY = w.Position.Y; }
                SyncMeterConfig();
            }

            _config.CheckUpdatesAtStartup = _setupVm.CheckUpdatesAtStartup;
            _config.SetupTab = _setupVm.SelectedTabIndex;
            _config.CaptureFrom(_display);
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
    }

    /// <summary>Update meter identity in config (add/update/remove) while preserving window bounds.</summary>
    private void SyncMeterConfig()
    {
        _config.Meters.RemoveAll(c => _manager.Meters.All(m => m.Id != c.Id));
        foreach (var m in _manager.Meters)
        {
            var c = ConfigFor(m.Id);
            c.Name = m.Name;
            c.Port = m.Port;
            c.Serial = m.Port is not null && PortIdentity.SerialFor(m.Port) is { } s ? s : m.Serial;
        }
    }
}
