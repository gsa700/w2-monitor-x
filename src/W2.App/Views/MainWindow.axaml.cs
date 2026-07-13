using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using W2.App.ViewModels;

namespace W2.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSetupClick(object? sender, RoutedEventArgs e) =>
        (Application.Current as App)?.ShowSetup();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        (Application.Current as App)?.NotifyMainWindowClosing(this);
        (DataContext as MainWindowViewModel)?.Dispose();
        base.OnClosing(e);
    }
}
