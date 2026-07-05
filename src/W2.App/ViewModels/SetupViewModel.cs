using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using W2.App.Services;
using W2.App.Settings;
using W2.Core;

namespace W2.App.ViewModels;

/// <summary>
/// Manages the meter list (add/remove, assign a COM port, connect/disconnect, pick focus), the
/// display toggles, and the in-app updater. Rows are updated in place on
/// <see cref="MeterManager.MetersChanged"/> so live status doesn't disturb the selection.
/// </summary>
public sealed class SetupViewModel : ViewModelBase
{
    private readonly MeterManager _manager;
    private bool _syncingSelection;

    public SetupViewModel(MeterManager manager, DisplaySettings display)
    {
        _manager = manager;
        Display = display;
        _manager.MetersChanged += Sync;

        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(RemoveSelected, () => SelectedRow is not null);
        ToggleConnectCommand = new RelayCommand(ToggleSelected, () => SelectedRow is not null && SelectedRow.Meter.Port is not null);
        ConnectAllCommand = new RelayCommand(() => _manager.ConnectAll());
        DisconnectAllCommand = new RelayCommand(() => _manager.DisconnectAll());
        RefreshCommand = new RelayCommand(RefreshPorts);
        DetectCommand = new RelayCommand(() => _ = DetectAsync(), () => !_manager.IsSimulated);

        SearchCommand = new RelayCommand(() => SelectedRow?.Meter.ToggleSearch(), () => CanControl);
        AutoRangeCommand = new RelayCommand(() => SelectedRow?.Meter.ToggleAutoRange(), () => CanControl);
        AvgPepCommand = new RelayCommand(() => SelectedRow?.Meter.ToggleAvgPep(), () => CanControl);
        SensorCommand = new RelayCommand(() => SelectedRow?.Meter.SwitchSensor(), () => CanControl);
        RangeCommand = new RelayCommand(() => SelectedRow?.Meter.StepRange(), () => CanControl);
        LedsCommand = new RelayCommand(() => SelectedRow?.Meter.ToggleLeds(), () => CanControl);
        ResetPeakCommand = new RelayCommand(() => _manager.Focus?.ResetPeak());

        CheckUpdatesCommand = new RelayCommand(() => _ = CheckUpdatesAsync(), () => !_updateBusy);
        UpdateNowCommand = new RelayCommand(() => _ = UpdateNowAsync(), () => _updateInfo?.AssetUrl is not null && !_updateBusy);
        OpenReleaseCommand = new RelayCommand(OpenRelease);
        UpdateStatus = $"You have {UpdateService.CurrentVersion}.";

        RefreshPorts();
        Sync();
    }

    public DisplaySettings Display { get; }
    public ObservableCollection<MeterRow> Rows { get; } = new();
    public ObservableCollection<string> Ports { get; } = new();

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand ToggleConnectCommand { get; }
    public RelayCommand ConnectAllCommand { get; }
    public RelayCommand DisconnectAllCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand DetectCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand AutoRangeCommand { get; }
    public RelayCommand AvgPepCommand { get; }
    public RelayCommand SensorCommand { get; }
    public RelayCommand RangeCommand { get; }
    public RelayCommand LedsCommand { get; }
    public RelayCommand ResetPeakCommand { get; }
    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand UpdateNowCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }

    // W2 control lamp state (reflects the selected meter).
    public bool CanControl => SelectedRow?.Meter is { IsConnected: true };
    public IBrush AutoRangeBrush => LampBrush(SelectedRow?.Meter is { AutoRangeOn: true });
    public IBrush LedsBrush => LampBrush(SelectedRow?.Meter is { LedsOn: true });
    public IBrush SearchBrush => LampBrush(SelectedRow?.Meter?.Search == true);
    public string AvgPepLabel => SelectedRow?.Meter?.Pep switch { true => "PEP", false => "AVG", _ => "Avg / PEP" };

    private static IBrush LampBrush(bool on) => on ? Palette.AmberBrush : Palette.PanelBrush;

    private MeterService? _controlMeter;

    private void OnControlMeterReading(MeterService m) => RefreshControls();

    private void RefreshControls()
    {
        OnPropertyChanged(nameof(CanControl));
        OnPropertyChanged(nameof(AutoRangeBrush));
        OnPropertyChanged(nameof(LedsBrush));
        OnPropertyChanged(nameof(SearchBrush));
        OnPropertyChanged(nameof(AvgPepLabel));
        foreach (var c in new[] { SearchCommand, AutoRangeCommand, AvgPepCommand, SensorCommand, RangeCommand, LedsCommand })
            c.RaiseCanExecuteChanged();
    }

    private string _detectStatus = "";
    public string DetectStatus { get => _detectStatus; private set => SetProperty(ref _detectStatus, value); }

    private MeterRow? _selectedRow;
    public MeterRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value)) return;
            if (!_syncingSelection) _manager.SetManualFocus(value?.Meter);

            // Reflect the selected meter's assigned port without reassigning it.
            _syncingSelection = true;
            SelectedPort = value?.Meter.Port;
            _syncingSelection = false;

            // Follow the selected meter's readings so the control lamps stay live.
            if (_controlMeter is not null) _controlMeter.ReadingReceived -= OnControlMeterReading;
            _controlMeter = value?.Meter;
            if (_controlMeter is not null) _controlMeter.ReadingReceived += OnControlMeterReading;

            RemoveCommand.RaiseCanExecuteChanged();
            ToggleConnectCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ToggleConnectLabel));
            RefreshControls();
        }
    }

    private string? _selectedPort;
    public string? SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (!SetProperty(ref _selectedPort, value)) return;
            if (_syncingSelection || SelectedRow is null || value is null) return;
            SelectedRow.Meter.Port = value;   // assign to the selected meter
            SelectedRow.Update();
            ToggleConnectCommand.RaiseCanExecuteChanged();
        }
    }

    public string ToggleConnectLabel => SelectedRow?.Meter.IsConnected == true ? "Disconnect" : "Connect";

    private void Add()
    {
        var n = _manager.Meters.Count + 1;
        var m = _manager.Add($"W2 #{n}");
        if (_manager.IsSimulated) m.Port = "SIM";   // sim meters need no real port
    }

    private void RemoveSelected()
    {
        if (SelectedRow is { } row) _manager.Remove(row.Meter);
    }

    private void ToggleSelected()
    {
        if (SelectedRow is not { } row) return;
        if (row.Meter.IsConnected) row.Meter.Disconnect(); else row.Meter.Connect();
    }

    private async Task DetectAsync()
    {
        if (Application.Current is not App app) return;
        var ok = await app.ConfirmAsync("Detect W2 meters",
            "Detect opens every free serial port and briefly asserts its control lines to look " +
            "for a W2. This can momentarily key a radio or cycle other gear on a CAT/PTT port. " +
            "Continue?");
        if (!ok) return;

        DetectStatus = "Scanning ports…";
        var skip = _manager.Meters.Where(m => m.Port is not null)
            .Select(m => m.Port!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = await Task.Run(() => W2Probe.Detect(MeterService.GetPortNames(), skip));

        var added = 0;
        foreach (var port in found)
        {
            if (_manager.Meters.Any(m => string.Equals(m.Port, port, StringComparison.OrdinalIgnoreCase))) continue;
            var m = _manager.Add($"W2 #{_manager.Meters.Count + 1}", port);
            m.Connect();
            added++;
        }
        DetectStatus = added > 0 ? $"Found {added} new meter(s)." : "No new W2 meters found.";
        RefreshPorts();
    }

    private void RefreshPorts()
    {
        var current = SelectedPort;
        Ports.Clear();
        foreach (var p in _manager.AvailablePorts().OrderBy(x => x)) Ports.Add(p);
        if (current is not null && Ports.Contains(current)) SelectedPort = current;
    }

    /// <summary>Mirror the manager's meters into observable rows, updating in place.</summary>
    private void Sync()
    {
        // Drop rows whose meter is gone.
        for (var i = Rows.Count - 1; i >= 0; i--)
            if (!_manager.Meters.Contains(Rows[i].Meter))
                Rows.RemoveAt(i);

        // Add rows for new meters.
        foreach (var m in _manager.Meters)
            if (Rows.All(r => !ReferenceEquals(r.Meter, m)))
                Rows.Add(new MeterRow(m));

        foreach (var r in Rows) r.Update();

        // Default to the first meter so the W2 controls work without a manual pick — most users
        // have a single W2. Only when nothing valid is selected, so it never fights a user choice.
        if ((SelectedRow is null || !Rows.Contains(SelectedRow)) && Rows.Count > 0)
            SelectedRow = Rows[0];

        OnPropertyChanged(nameof(ToggleConnectLabel));
        ToggleConnectCommand.RaiseCanExecuteChanged();
        RefreshControls();
    }

    // --- updates ---
    private UpdateInfo? _updateInfo;
    private string? _stagedExe;
    private bool _updateBusy;

    private bool _checkUpdatesAtStartup;
    public bool CheckUpdatesAtStartup { get => _checkUpdatesAtStartup; set => SetProperty(ref _checkUpdatesAtStartup, value); }

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }

    private IBrush _updateStatusBrush = Palette.DimBrush;
    public IBrush UpdateStatusBrush { get => _updateStatusBrush; private set => SetProperty(ref _updateStatusBrush, value); }

    private bool _updateAvailable;
    public bool UpdateAvailable { get => _updateAvailable; private set => SetProperty(ref _updateAvailable, value); }

    public async Task CheckUpdatesAsync()
    {
        _updateBusy = true; CheckUpdatesCommand.RaiseCanExecuteChanged(); UpdateNowCommand.RaiseCanExecuteChanged();
        UpdateStatus = "Checking for updates…"; UpdateStatusBrush = Palette.DimBrush;

        var info = await UpdateService.CheckAsync();
        _updateInfo = info;
        if (info.Error is not null)
        {
            UpdateStatus = $"Update check failed: {info.Error}"; UpdateStatusBrush = Palette.RedBrush; UpdateAvailable = false;
        }
        else if (info.UpdateAvailable && info.AssetUrl is not null)
        {
            UpdateStatus = $"Update available: {info.LatestTag} (you have {info.CurrentVersion})."; UpdateStatusBrush = Palette.GreenBrush; UpdateAvailable = true;
        }
        else if (info.UpdateAvailable)
        {
            UpdateStatus = $"{info.LatestTag} is available, but has no build for this platform."; UpdateStatusBrush = Palette.AmberBrush; UpdateAvailable = false;
        }
        else
        {
            UpdateStatus = $"Up to date ({info.CurrentVersion})."; UpdateStatusBrush = Palette.GreenBrush; UpdateAvailable = false;
        }

        _updateBusy = false; CheckUpdatesCommand.RaiseCanExecuteChanged(); UpdateNowCommand.RaiseCanExecuteChanged();
    }

    private async Task UpdateNowAsync()
    {
        if (_updateInfo?.AssetUrl is not { } url) return;
        _updateBusy = true; UpdateNowCommand.RaiseCanExecuteChanged(); CheckUpdatesCommand.RaiseCanExecuteChanged();
        try
        {
            UpdateStatus = "Downloading update…"; UpdateStatusBrush = Palette.DimBrush;
            _stagedExe = await UpdateService.DownloadAndStageAsync(url);
            UpdateStatus = "Update ready — restarting to apply…"; UpdateStatusBrush = Palette.GreenBrush;
            UpdateService.ApplyAndRestart(_stagedExe);
            (Application.Current as App)?.ExitForUpdate();
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update failed: {ex.Message}"; UpdateStatusBrush = Palette.RedBrush;
            _updateBusy = false; UpdateNowCommand.RaiseCanExecuteChanged(); CheckUpdatesCommand.RaiseCanExecuteChanged();
        }
    }

    private void OpenRelease()
    {
        var url = _updateInfo?.ReleaseUrl ?? $"https://github.com/{UpdateService.Repo}/releases/latest";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* ignore */ }
    }
}

/// <summary>Observable view of one meter for the Setup list.</summary>
public sealed class MeterRow : ViewModelBase
{
    public MeterRow(MeterService meter) { Meter = meter; Update(); }

    public MeterService Meter { get; }

    private string _text = "";
    public string Text { get => _text; private set => SetProperty(ref _text, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private IBrush _dotBrush = Palette.DimBrush;
    public IBrush DotBrush { get => _dotBrush; private set => SetProperty(ref _dotBrush, value); }

    public void Update()
    {
        var port = Meter.Port ?? "unassigned";
        var serial = SerialDisplay.Shorten(Meter.Serial);
        Text = serial is null ? $"{Meter.Name}  ·  {port}" : $"{Meter.Name}  ·  {port}  ({serial})";
        StatusText = Meter.Status;
        DotBrush = Meter.StatusIsError ? Palette.RedBrush
            : Meter is { IsConnected: true, Current: not null } ? Palette.GreenBrush
            : Meter.IsConnected ? Palette.AmberBrush : Palette.DimBrush;
    }
}
