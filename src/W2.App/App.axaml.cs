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
    private MeterService _meter = null!;
    private MainWindowViewModel _mainVm = null!;
    private MainWindow _mainWindow = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _config = ConfigStore.Load();

            // `--sim` drives the UI from a synthetic W2 (no hardware needed).
            var simulated = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--sim", StringComparison.OrdinalIgnoreCase));

            _meter = new MeterService(simulated);
            _mainVm = new MainWindowViewModel(_meter);

            if (simulated)
            {
                _mainVm.SelectPort("SIM");
                _meter.Connect("SIM");
            }
            else
            {
                // Follow the cable by its chip serial across COM renumbering, then auto-connect.
                var startupPort = PortIdentity.ResolvePort(_config.Port, _config.Serial);
                _mainVm.SelectPort(startupPort);
                if (startupPort is not null && MeterService.GetPortNames().Contains(startupPort))
                    _meter.Connect(startupPort);
            }

            _mainWindow = new MainWindow { DataContext = _mainVm };
            RestoreMainBounds(_mainWindow);

            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            _mainWindow.Closing += (_, _) => SaveAndCleanup();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RestoreMainBounds(Window w)
    {
        // Width is fixed and height auto-fits content, so only the position is restored.
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

    private void SaveAndCleanup()
    {
        try
        {
            _config.X = _mainWindow.Position.X;
            _config.Y = _mainWindow.Position.Y;

            var port = _meter.CurrentPort ?? _mainVm.SelectedPort;
            _config.Port = port;
            if (port is not null && PortIdentity.SerialFor(port) is { } serial) _config.Serial = serial;
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
        _meter.Dispose();
    }
}
