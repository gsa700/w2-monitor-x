namespace W2.App.ViewModels;

/// <summary>
/// Phase 0 placeholder. Shows the app is alive and the Avalonia/binding/palette stack
/// works end to end. PHASE 2 replaces this with a live readout driven by a MeterService
/// (forward power, SWR, bars, peak hold); PHASE 3 makes it multi-meter.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    public string TitleText => "W2 MONITOR";

    private string _statusText = "Scaffold build — no meter wired up yet (Phase 0)";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string PowerText => "— W";
    public string SwrText => "—";
}
