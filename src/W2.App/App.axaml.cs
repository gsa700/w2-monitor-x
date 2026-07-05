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
    private MainWindow _mainWindow = null!;
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

            _mainWindow = new MainWindow { DataContext = new MainWindowViewModel(_manager, _display) };
            RestoreMainBounds(_mainWindow);
            _mainWindow.Topmost = _display.AlwaysOnTop;
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            _display.PropertyChanged += OnDisplayChanged;
            _mainWindow.Closing += (_, _) => SaveAndCleanup();
            var openSetup = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase));
            _mainWindow.Opened += async (_, _) =>
            {
                if (openSetup) ShowSetup();   // debug helper for verifying the Setup window
                if (_config.CheckUpdatesAtStartup)
                {
                    await _setupVm.CheckUpdatesAsync();
                    if (_setupVm.UpdateAvailable) ShowSetup();   // surface it
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

    public void ShowSetup()
    {
        if (_setupWindow is null)
        {
            _setupWindow = new SetupWindow { DataContext = _setupVm, Topmost = _display.AlwaysOnTop };
            RestoreSetupBounds(_setupWindow);
            _setupWindow.Show(_mainWindow);   // owned by main -> closes with it
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

    /// <summary>Close the app so the staged update helper can swap the executable and relaunch.</summary>
    public void ExitForUpdate() => _mainWindow.Close();

    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisplaySettings.AlwaysOnTop))
        {
            _mainWindow.Topmost = _display.AlwaysOnTop;
            if (_setupWindow is not null) _setupWindow.Topmost = _display.AlwaysOnTop;
        }
    }

    private void RestoreMainBounds(Window w)
    {
        if (_config is { X: not null, Y: not null })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)_config.X.Value, (int)_config.Y.Value);
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void RestoreSetupBounds(Window w)
    {
        if (_config is { SetupX: not null, SetupY: not null })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)_config.SetupX.Value, (int)_config.SetupY.Value);
        }
    }

    private void SaveAndCleanup()
    {
        try
        {
            _config.X = _mainWindow.Position.X;
            _config.Y = _mainWindow.Position.Y;
            if (_setupWindow is not null)
            {
                _config.SetupX = _setupWindow.Position.X;
                _config.SetupY = _setupWindow.Position.Y;
            }

            if (!_manager.IsSimulated)
            {
                _config.Meters = _manager.Meters.Select(m => new MeterConfig
                {
                    Id = m.Id,
                    Name = m.Name,
                    Port = m.Port,
                    Serial = m.Port is not null && PortIdentity.SerialFor(m.Port) is { } s ? s : m.Serial,
                }).ToList();
            }
            _config.CheckUpdatesAtStartup = _setupVm.CheckUpdatesAtStartup;
            _config.CaptureFrom(_display);
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
        _manager.Dispose();
    }
}
