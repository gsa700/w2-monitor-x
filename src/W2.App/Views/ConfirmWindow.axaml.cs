using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace W2.App.Views;

/// <summary>Minimal modal Yes/No dialog. Use via <c>await new ConfirmWindow(title, msg).ShowDialog&lt;bool&gt;(owner)</c>.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow() => InitializeComponent();

    public ConfirmWindow(string title, string message) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnContinue(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
