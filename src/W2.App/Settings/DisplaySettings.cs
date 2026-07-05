using W2.App.ViewModels;

namespace W2.App.Settings;

/// <summary>
/// Observable "what to show / how to behave" settings. Setup toggles them, the main window
/// binds row visibility to them, and config persists them. Mirrors the PowerShell app's
/// $show hashtable plus the TX timeout and always-on-top.
/// </summary>
public sealed class DisplaySettings : ViewModelBase
{
    private bool _showStatusLine = true;
    public bool ShowStatusLine { get => _showStatusLine; set => SetProperty(ref _showStatusLine, value); }

    private bool _showPowerBar = true;
    public bool ShowPowerBar { get => _showPowerBar; set => SetProperty(ref _showPowerBar, value); }

    private bool _showSwrBar = true;
    public bool ShowSwrBar { get => _showSwrBar; set => SetProperty(ref _showSwrBar, value); }

    private bool _showReflected = true;
    public bool ShowReflected { get => _showReflected; set => SetProperty(ref _showReflected, value); }

    private bool _showReturnLoss = true;
    public bool ShowReturnLoss { get => _showReturnLoss; set => SetProperty(ref _showReturnLoss, value); }

    private bool _showPeak = true;
    public bool ShowPeak { get => _showPeak; set => SetProperty(ref _showPeak, value); }

    private bool _showTx = true;
    public bool ShowTx { get => _showTx; set => SetProperty(ref _showTx, value); }

    private int _timeoutSec = 180;
    public int TimeoutSec { get => _timeoutSec; set => SetProperty(ref _timeoutSec, Math.Clamp(value, 30, 1800)); }

    private bool _alwaysOnTop;
    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }
}
