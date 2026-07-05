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
    private MainWindow _mainWindow = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _config = ConfigStore.Load();

            // PHASE 2/3 TODO: construct MeterService/MeterManager here, resolve the saved
            // port by chip serial (PortIdentity.ResolvePort) and auto-connect, then feed
            // a live MainWindowViewModel. Scaffold just shows a placeholder VM.
            _mainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
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
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
    }
}
