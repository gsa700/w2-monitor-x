using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using W2.App.Services;

namespace W2.App.ViewModels;

/// <summary>
/// Manages the meter list: add/remove, assign a COM port, connect/disconnect, and pick which
/// meter the main display follows (manual focus). Rows are updated in place on
/// <see cref="MeterManager.MetersChanged"/> so live status doesn't disturb the selection.
/// </summary>
public sealed class SetupViewModel : ViewModelBase
{
    private readonly MeterManager _manager;
    private bool _syncingSelection;

    public SetupViewModel(MeterManager manager)
    {
        _manager = manager;
        _manager.MetersChanged += Sync;

        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(RemoveSelected, () => SelectedRow is not null);
        ToggleConnectCommand = new RelayCommand(ToggleSelected, () => SelectedRow is not null && SelectedRow.Meter.Port is not null);
        ConnectAllCommand = new RelayCommand(() => _manager.ConnectAll());
        DisconnectAllCommand = new RelayCommand(() => _manager.DisconnectAll());
        RefreshCommand = new RelayCommand(RefreshPorts);

        RefreshPorts();
        Sync();
    }

    public ObservableCollection<MeterRow> Rows { get; } = new();
    public ObservableCollection<string> Ports { get; } = new();

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand ToggleConnectCommand { get; }
    public RelayCommand ConnectAllCommand { get; }
    public RelayCommand DisconnectAllCommand { get; }
    public RelayCommand RefreshCommand { get; }

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

            RemoveCommand.RaiseCanExecuteChanged();
            ToggleConnectCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ToggleConnectLabel));
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

        OnPropertyChanged(nameof(ToggleConnectLabel));
        ToggleConnectCommand.RaiseCanExecuteChanged();
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
        Text = $"{Meter.Name}  ·  {Meter.Port ?? "unassigned"}";
        StatusText = Meter.Status;
        DotBrush = Meter.StatusIsError ? Palette.RedBrush
            : Meter is { IsConnected: true, Current: not null } ? Palette.GreenBrush
            : Meter.IsConnected ? Palette.AmberBrush : Palette.DimBrush;
    }
}
