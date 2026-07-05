using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using W2.App.Services;
using W2.App.ViewModels;
using W2.App.Views;

namespace W2.App;

public partial class App : Application
{
    private AppConfig _config = new();
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

            var simulated = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--sim", StringComparison.OrdinalIgnoreCase));

            _manager = new MeterManager(simulated);
            _setupVm = new SetupViewModel(_manager);

            if (simulated) BuildSimMeters();
            else RestoreMeters();

            _mainWindow = new MainWindow { DataContext = new MainWindowViewModel(_manager) };
            RestoreMainBounds(_mainWindow);
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            _mainWindow.Closing += (_, _) => SaveAndCleanup();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildSimMeters()
    {
        // Two phase-shifted synthetic meters so auto-focus visibly switches between them.
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
            // Follow the cable by its chip serial across COM renumbering, then auto-connect.
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
            _setupWindow = new SetupWindow { DataContext = _setupVm };
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

            // Persist the meter list (skip the synthetic sim meters).
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
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
        _manager.Dispose();
    }
}
