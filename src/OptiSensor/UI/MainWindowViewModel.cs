using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OptiSensor.Libre;
using OptiSensor.Models;
using OptiSensor.Overlay;
using OptiSensor.Settings;

namespace OptiSensor.UI;

internal sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettings _settings;
    private readonly OverlayLineBuilder _overlayLineBuilder = new();
    private readonly SensorDiscoveryService _sensorDiscoveryService = new();
    private readonly ObservableCollection<SelectedOverlaySensorViewModel> _emptySelectedSensors = [];
    private bool _hasUnsavedChanges;
    private bool _isRefreshing;
    private OverlayGroupViewModel? _selectedOverlayGroup;

    public MainWindowViewModel(AppSettings settings)
    {
        _settings = settings;

        foreach (var group in _settings.GetOverlayGroupsSnapshot())
            OverlayGroups.Add(new OverlayGroupViewModel(group, SyncOverlayGroupsToSettings, MoveSensorToGroup));

        if (OverlayGroups.Count == 0)
            OverlayGroups.Add(CreateGroup("GPU", 0));

        SelectedOverlayGroup = OverlayGroups.OrderBy(group => group.Order).FirstOrDefault();
        ReorderOverlayGroups(markChanged: false);
        ReorderSelectedSensors(markChanged: false);
        SyncOverlayGroupsToSettings(markUnsaved: false);
        HasUnsavedChanges = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DetectedSensorViewModel> DetectedSensors { get; } = [];
    public ObservableCollection<OverlayGroupViewModel> OverlayGroups { get; } = [];
    public ObservableCollection<SelectedOverlaySensorViewModel> SelectedSensors => SelectedOverlayGroup?.Sensors ?? _emptySelectedSensors;

    public int DetectedSensorCount => DetectedSensors.Count;
    public int EnabledSelectedSensorCount => OverlayGroups.Where(group => group.Enabled).Sum(group => group.Sensors.Count(sensor => sensor.Enabled));
    public int TotalSelectedSensorCount => OverlayGroups.Sum(group => group.Sensors.Count);
    public int OverlayGroupCount => OverlayGroups.Count;
    public string SettingsStateText => HasUnsavedChanges ? "Unsaved changes" : "Saved";

    public OverlayGroupViewModel? SelectedOverlayGroup
    {
        get => _selectedOverlayGroup;
        set
        {
            if (_selectedOverlayGroup == value)
                return;

            _selectedOverlayGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSensors));
            SyncDetectedSensorSelectionStates();
            OnCountsChanged();
        }
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges == value)
                return;

            _hasUnsavedChanges = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SettingsStateText));
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (_isRefreshing == value)
                return;

            _isRefreshing = value;
            OnPropertyChanged();
        }
    }

    public async Task RefreshDetectedSensorsAsync()
    {
        IsRefreshing = true;
        try
        {
            var snapshot = await Task.Run(() => _sensorDiscoveryService.Discover()).ConfigureAwait(true);

            DetectedSensors.Clear();
            foreach (var sensor in snapshot.Sensors.OrderBy(sensor => sensor.Category).ThenBy(sensor => sensor.HardwareName).ThenBy(sensor => sensor.SensorName))
            {
                var viewModel = new DetectedSensorViewModel(sensor, ToggleDetectedSensorSelection);
                viewModel.SetSelectedSilently(IsSensorSelectedInCurrentGroup(sensor.SensorId));
                DetectedSensors.Add(viewModel);
            }

            UpdateSelectedSensorAvailability(snapshot.Sensors);
            OnCountsChanged();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public bool AddDetectedSensor(DetectedSensorViewModel detectedSensor)
    {
        if (SelectedSensors.Any(sensor => string.Equals(sensor.SensorId, detectedSensor.SensorId, StringComparison.OrdinalIgnoreCase)))
            return false;

        EnsureSelectedGroup();

        var model = new SelectedOverlaySensor
        {
            SensorId = detectedSensor.Sensor.SensorId,
            HardwareType = detectedSensor.Sensor.HardwareType,
            HardwareName = detectedSensor.Sensor.HardwareName,
            SensorType = detectedSensor.Sensor.SensorType,
            SensorName = detectedSensor.Sensor.SensorName,
            Category = detectedSensor.Sensor.Category,
            DisplayName = SensorFormatDefaults.GetDefaultDisplayName(detectedSensor.Sensor),
            Unit = detectedSensor.Sensor.Unit,
            Format = SensorFormatDefaults.GetDefaultFormat(detectedSensor.Sensor.Unit),
            Order = SelectedSensors.Count,
            Enabled = true
        };

        SelectedSensors.Add(new SelectedOverlaySensorViewModel(model, SelectedOverlayGroup!.Id, SyncSelectedSensorsToSettings, MoveSensorToGroup));
        ReorderSelectedSensors(markChanged: true);
        SyncOverlayGroupsToSettings();
        UpdateSelectedSensorAvailability(DetectedSensors.Select(sensor => sensor.Sensor).ToArray());
        return true;
    }

    public void ToggleDetectedSensorSelection(DetectedSensorViewModel detectedSensor)
    {
        if (detectedSensor.IsSelected)
        {
            if (!AddDetectedSensor(detectedSensor))
                detectedSensor.SetSelectedSilently(true);

            return;
        }

        var selectedSensor = SelectedSensors.FirstOrDefault(sensor =>
            string.Equals(sensor.SensorId, detectedSensor.SensorId, StringComparison.OrdinalIgnoreCase));
        if (selectedSensor is null)
            return;

        RemoveSelectedSensor(selectedSensor);
    }

    public void RemoveSelectedSensor(SelectedOverlaySensorViewModel selectedSensor)
    {
        SelectedSensors.Remove(selectedSensor);
        ReorderSelectedSensors(markChanged: true);
        SyncOverlayGroupsToSettings();
        SyncDetectedSensorSelectionStates();
    }

    public void MoveSelectedSensorUp(SelectedOverlaySensorViewModel selectedSensor)
    {
        var index = SelectedSensors.IndexOf(selectedSensor);
        if (index <= 0)
            return;

        SelectedSensors.Move(index, index - 1);
        ReorderSelectedSensors(markChanged: true);
        SyncOverlayGroupsToSettings();
    }

    public void MoveSelectedSensorDown(SelectedOverlaySensorViewModel selectedSensor)
    {
        var index = SelectedSensors.IndexOf(selectedSensor);
        if (index < 0 || index >= SelectedSensors.Count - 1)
            return;

        SelectedSensors.Move(index, index + 1);
        ReorderSelectedSensors(markChanged: true);
        SyncOverlayGroupsToSettings();
    }

    public bool TrySave(out string? errorMessage)
    {
        ReorderSelectedSensors(markChanged: true);
        if (!ValidateFormats(out errorMessage))
            return false;

        SyncOverlayGroupsToSettings();
        _settings.Save();
        HasUnsavedChanges = false;
        errorMessage = null;
        return true;
    }

    public string GetOverlayPreviewText()
    {
        var snapshot = new LibreSensorSnapshot(DetectedSensors.Select(sensor => sensor.Sensor).ToArray());
        var groups = OverlayGroups
            .OrderBy(group => group.Order)
            .Select(group => group.ToModel())
            .ToArray();

        var line = _overlayLineBuilder.BuildLine(snapshot, groups);
        if (!string.IsNullOrWhiteSpace(line))
            return line;

        var enabledGroups = OverlayGroups
            .Where(group => group.Enabled)
            .OrderBy(group => group.Order)
            .Select(group => string.IsNullOrWhiteSpace(group.Name) ? "(unnamed group)" : group.Name)
            .ToArray();

        if (enabledGroups.Length == 0)
            return "No enabled groups.";

        return string.Join(" | ", enabledGroups.Select(groupName => $"{groupName} (empty)"));
    }

    public void Dispose()
    {
        _sensorDiscoveryService.Dispose();
    }

    public void AddOverlayGroup()
    {
        var group = CreateGroup($"Group {OverlayGroups.Count + 1}", OverlayGroups.Count);
        OverlayGroups.Add(group);
        SelectedOverlayGroup = group;
        ReorderOverlayGroups(markChanged: true);
        SyncOverlayGroupsToSettings();
    }

    public void RemoveOverlayGroup(OverlayGroupViewModel group)
    {
        OverlayGroups.Remove(group);
        if (OverlayGroups.Count == 0)
            OverlayGroups.Add(CreateGroup("GPU", 0));

        SelectedOverlayGroup = OverlayGroups.OrderBy(candidate => candidate.Order).FirstOrDefault();
        ReorderOverlayGroups(markChanged: true);
        SyncOverlayGroupsToSettings();
        SyncDetectedSensorSelectionStates();
    }

    public void MoveOverlayGroupUp(OverlayGroupViewModel group)
    {
        var index = OverlayGroups.IndexOf(group);
        if (index <= 0)
            return;

        OverlayGroups.Move(index, index - 1);
        ReorderOverlayGroups(markChanged: true);
        SyncOverlayGroupsToSettings();
    }

    public void MoveOverlayGroupDown(OverlayGroupViewModel group)
    {
        var index = OverlayGroups.IndexOf(group);
        if (index < 0 || index >= OverlayGroups.Count - 1)
            return;

        OverlayGroups.Move(index, index + 1);
        ReorderOverlayGroups(markChanged: true);
        SyncOverlayGroupsToSettings();
    }

    private void SyncSelectedSensorsToSettings()
    {
        SyncOverlayGroupsToSettings();
    }

    private void SyncOverlayGroupsToSettings()
    {
        SyncOverlayGroupsToSettings(markUnsaved: true);
    }

    private void SyncOverlayGroupsToSettings(bool markUnsaved)
    {
        _settings.ReplaceOverlayGroups(OverlayGroups.OrderBy(group => group.Order).Select(group => group.ToModel()));

        if (markUnsaved)
            HasUnsavedChanges = true;

        OnCountsChanged();
    }

    private void EnsureSelectedGroup()
    {
        if (SelectedOverlayGroup is not null)
            return;

        var group = CreateGroup("GPU", OverlayGroups.Count);
        OverlayGroups.Add(group);
        SelectedOverlayGroup = group;
    }

    private OverlayGroupViewModel CreateGroup(string name, int order)
    {
        return new OverlayGroupViewModel(
            new OverlayGroup
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Order = order,
                Enabled = true,
                Sensors = []
            },
            SyncOverlayGroupsToSettings,
            MoveSensorToGroup);
    }

    private bool MoveSensorToGroup(SelectedOverlaySensorViewModel sensor, string targetGroupId)
    {
        var sourceGroup = OverlayGroups.FirstOrDefault(group => group.Sensors.Contains(sensor));
        if (sourceGroup is null)
            return false;

        if (string.Equals(sourceGroup.Id, targetGroupId, StringComparison.OrdinalIgnoreCase))
            return true;

        var targetGroup = OverlayGroups.FirstOrDefault(group =>
            string.Equals(group.Id, targetGroupId, StringComparison.OrdinalIgnoreCase));
        if (targetGroup is null)
            return false;

        if (targetGroup.Sensors.Any(existing => string.Equals(existing.SensorId, sensor.SensorId, StringComparison.OrdinalIgnoreCase)))
            return false;

        sourceGroup.Sensors.Remove(sensor);
        ReorderSensors(sourceGroup.Sensors, markChanged: true);

        targetGroup.Sensors.Add(sensor);
        sensor.SetGroupSilently(targetGroup.Id);
        ReorderSensors(targetGroup.Sensors, markChanged: true);

        SyncOverlayGroupsToSettings();
        SyncDetectedSensorSelectionStates();
        return true;
    }

    private void ReorderOverlayGroups(bool markChanged)
    {
        for (var index = 0; index < OverlayGroups.Count; index++)
        {
            if (markChanged)
                OverlayGroups[index].Order = index;
            else
                OverlayGroups[index].SetOrderSilently(index);
        }
    }

    private void ReorderSelectedSensors(bool markChanged)
    {
        ReorderSensors(SelectedSensors, markChanged);
    }

    private static void ReorderSensors(ObservableCollection<SelectedOverlaySensorViewModel> sensors, bool markChanged)
    {
        for (var index = 0; index < sensors.Count; index++)
        {
            if (markChanged)
                sensors[index].Order = index;
            else
                sensors[index].SetOrderSilently(index);
        }
    }

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(DetectedSensorCount));
        OnPropertyChanged(nameof(EnabledSelectedSensorCount));
        OnPropertyChanged(nameof(TotalSelectedSensorCount));
        OnPropertyChanged(nameof(OverlayGroupCount));
    }

    private void UpdateSelectedSensorAvailability(IReadOnlyCollection<DetectedSensorInfo> detectedSensors)
    {
        foreach (var selectedSensor in OverlayGroups.SelectMany(group => group.Sensors))
        {
            var match = SensorMatcher.FindBestMatch(selectedSensor.ToModel(), detectedSensors);
            var viewModel = match is null ? null : new DetectedSensorViewModel(match);
            selectedSensor.UpdateAvailability(viewModel);
        }

        SyncDetectedSensorSelectionStates();
    }

    private bool ValidateFormats(out string? errorMessage)
    {
        foreach (var sensor in OverlayGroups.SelectMany(group => group.Sensors))
        {
            var format = string.IsNullOrWhiteSpace(sensor.Format)
                ? SensorFormatDefaults.GetDefaultFormat(sensor.Unit)
                : sensor.Format;

            try
            {
                _ = string.Format(CultureInfo.InvariantCulture, format, 123.4f);
            }
            catch (FormatException)
            {
                errorMessage = $"Invalid format: {sensor.Format}{Environment.NewLine}Please use a .NET numeric format such as {{0:0}}°C, {{0:0.0}}W, or {{0:0}}%.";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }

    private bool IsSensorSelectedInCurrentGroup(string sensorId)
    {
        return SelectedSensors.Any(sensor => string.Equals(sensor.SensorId, sensorId, StringComparison.OrdinalIgnoreCase));
    }

    private void SyncDetectedSensorSelectionStates()
    {
        foreach (var detectedSensor in DetectedSensors)
            detectedSensor.SetSelectedSilently(IsSensorSelectedInCurrentGroup(detectedSensor.SensorId));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
