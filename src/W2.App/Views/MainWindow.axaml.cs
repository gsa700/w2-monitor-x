using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace W2.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = v.IndexOf('+');            // strip any SourceLink "+<hash>" build suffix
        if (plus >= 0) v = v[..plus];
        Title = string.IsNullOrEmpty(v) ? "W2 Monitor" : $"W2 Monitor v{v}";
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
