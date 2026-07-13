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

namespace W2.App;

public partial class App : Application
{
    private AppConfig _config = new();
    private DisplaySettings _display = null!;
    private MeterManager _manager = null!;
    private SetupViewModel _setupVm = null!;

    private MainWindow? _focusWindow;                              // the auto-focus window (optional)
    private readonly Dictionary<string, MainWindow> _meterWindows = new();   // dedicated per-meter windows
    private SetupWindow? _setupWindow;

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
            _setupVm = new SetupViewModel(_manager, _display) { CheckUpdatesAtStartup = _config.CheckUpdatesAtStartup };

            if (simulated) BuildSimMeters();
            else RestoreMeters();

            // Build the startup window set: the focus window (unless the user closed it last time)
            // plus any dedicated per-meter windows that were open. Always at least one window.
            var windows = new List<MainWindow>();
            if (_config.FocusWindowOpen) windows.Add(CreateFocusWindow());
            if (!simulated)
                foreach (var m in _manager.Meters)
                    if (ConfigFor(m.Id).WinOpen) windows.Add(CreateMeterWindow(m));
            if (windows.Count == 0) windows.Add(CreateFocusWindow());   // guard: never start windowless

            desktop.MainWindow = windows[0];
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            _display.PropertyChanged += OnDisplayChanged;
            desktop.Exit += (_, _) => { SaveConfig(); _manager.Dispose(); };

            var openSetup = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase));
            windows[0].Opened += async (_, _) =>
            {
                for (var i = 1; i < windows.Count; i++) windows[i].Show();   // the primary is auto-shown
                if (openSetup) ShowSetup();
                if (_config.CheckUpdatesAtStartup)
                {
                    await _setupVm.CheckUpdatesAsync();
                    if (_setupVm.UpdateAvailable) ShowSetup();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

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
                if (MeterService.GetPortNames().Contains(port)) m.Connect();
            }
        }
    }

    // --- window factories ---

    private MainWindow CreateFocusWindow()
    {
        var w = new MainWindow { DataContext = new MainWindowViewModel(_manager, _display), Topmost = _display.AlwaysOnTop };
        RestoreBounds(w, _config.X, _config.Y);
        _config.FocusWindowOpen = true;
        _focusWindow = w;
        return w;
    }

    private MainWindow CreateMeterWindow(MeterService m)
    {
        var w = new MainWindow { DataContext = new MainWindowViewModel(m, _display), Topmost = _display.AlwaysOnTop };
        if (_manager.IsSimulated) RestoreBounds(w, null, null);
        else { var c = ConfigFor(m.Id); c.WinOpen = true; RestoreBounds(w, c.WinX, c.WinY); }
        _meterWindows[m.Id] = w;
        return w;
    }

    /// <summary>Open (or focus) a dedicated window for a meter — from Setup's "Open window".</summary>
    public void OpenMeterWindow(MeterService m)
    {
        if (_meterWindows.TryGetValue(m.Id, out var existing)) { existing.Activate(); return; }
        CreateMeterWindow(m).Show();
        SaveConfig();
    }

    public void NotifyMainWindowClosing(MainWindow w)
    {
        if (ReferenceEquals(w, _focusWindow))
        {
            _config.X = w.Position.X;
            _config.Y = w.Position.Y;
            _config.FocusWindowOpen = false;
            _focusWindow = null;
        }
        else
        {
            var id = _meterWindows.FirstOrDefault(kv => ReferenceEquals(kv.Value, w)).Key;
            if (id is not null)
            {
                if (!_manager.IsSimulated) { var c = ConfigFor(id); c.WinX = w.Position.X; c.WinY = w.Position.Y; c.WinOpen = false; }
                _meterWindows.Remove(id);
            }
        }
        SaveConfig();
    }

    // --- Setup window ---

    public void ShowSetup()
    {
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
    public void ExitForUpdate()
    {
        foreach (var w in AllWindows().ToList()) w.Close();
    }

    /// <summary>Modal yes/no confirmation, owned by whatever window is available.</summary>
    public Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = (Window?)_setupWindow ?? _focusWindow ?? _meterWindows.Values.FirstOrDefault();
        var dlg = new ConfirmWindow(title, message) { Topmost = _display.AlwaysOnTop };
        return owner is not null ? dlg.ShowDialog<bool>(owner) : Task.FromResult(false);
    }

    private IEnumerable<Window> AllWindows()
    {
        if (_focusWindow is not null) yield return _focusWindow;
        foreach (var w in _meterWindows.Values) yield return w;
        if (_setupWindow is not null) yield return _setupWindow;
    }

    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisplaySettings.AlwaysOnTop))
            foreach (var w in AllWindows()) w.Topmost = _display.AlwaysOnTop;
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
                foreach (var (id, w) in _meterWindows) { var c = ConfigFor(id); c.WinX = w.Position.X; c.WinY = w.Position.Y; c.WinOpen = true; }
                SyncMeterConfig();
            }

            _config.CheckUpdatesAtStartup = _setupVm.CheckUpdatesAtStartup;
            _config.CaptureFrom(_display);
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
    }

    /// <summary>Update meter identity in config (add/update/remove) while preserving window state.</summary>
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
