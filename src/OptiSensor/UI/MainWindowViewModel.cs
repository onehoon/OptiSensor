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
    private readonly SensorDiscoveryService _sensorDiscoveryService = new();
    private bool _hasUnsavedChanges;
    private bool _isRefreshing;

    public MainWindowViewModel(AppSettings settings)
    {
        _settings = settings;

        foreach (var sensor in _settings.GetSelectedSensorsSnapshot())
            SelectedSensors.Add(new SelectedOverlaySensorViewModel(sensor, SyncSelectedSensorsToSettings));

        ReorderSelectedSensors();
        SyncSelectedSensorsToSettings(markUnsaved: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DetectedSensorViewModel> DetectedSensors { get; } = [];
    public ObservableCollection<SelectedOverlaySensorViewModel> SelectedSensors { get; } = [];

    public int DetectedSensorCount => DetectedSensors.Count;
    public int EnabledSelectedSensorCount => SelectedSensors.Count(sensor => sensor.Enabled);
    public int TotalSelectedSensorCount => SelectedSensors.Count;
    public string SettingsStateText => HasUnsavedChanges ? "Unsaved changes" : "Saved";

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
                DetectedSensors.Add(new DetectedSensorViewModel(sensor));

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

        SelectedSensors.Add(new SelectedOverlaySensorViewModel(model, SyncSelectedSensorsToSettings));
        ReorderSelectedSensors();
        SyncSelectedSensorsToSettings();
        UpdateSelectedSensorAvailability(DetectedSensors.Select(sensor => sensor.Sensor).ToArray());
        return true;
    }

    public void RemoveSelectedSensor(SelectedOverlaySensorViewModel selectedSensor)
    {
        SelectedSensors.Remove(selectedSensor);
        ReorderSelectedSensors();
        SyncSelectedSensorsToSettings();
    }

    public void MoveSelectedSensorUp(SelectedOverlaySensorViewModel selectedSensor)
    {
        var index = SelectedSensors.IndexOf(selectedSensor);
        if (index <= 0)
            return;

        SelectedSensors.Move(index, index - 1);
        ReorderSelectedSensors();
        SyncSelectedSensorsToSettings();
    }

    public void MoveSelectedSensorDown(SelectedOverlaySensorViewModel selectedSensor)
    {
        var index = SelectedSensors.IndexOf(selectedSensor);
        if (index < 0 || index >= SelectedSensors.Count - 1)
            return;

        SelectedSensors.Move(index, index + 1);
        ReorderSelectedSensors();
        SyncSelectedSensorsToSettings();
    }

    public bool TrySave(out string? errorMessage)
    {
        ReorderSelectedSensors();
        if (!ValidateFormats(out errorMessage))
            return false;

        SyncSelectedSensorsToSettings();
        _settings.Save();
        HasUnsavedChanges = false;
        errorMessage = null;
        return true;
    }

    public void Dispose()
    {
        _sensorDiscoveryService.Dispose();
    }

    private void SyncSelectedSensorsToSettings()
    {
        SyncSelectedSensorsToSettings(markUnsaved: true);
    }

    private void SyncSelectedSensorsToSettings(bool markUnsaved)
    {
        _settings.ReplaceSelectedSensors(SelectedSensors
            .OrderBy(sensor => sensor.Order)
            .Select(sensor => sensor.ToModel()));

        if (markUnsaved)
            HasUnsavedChanges = true;

        OnCountsChanged();
    }

    private void ReorderSelectedSensors()
    {
        for (var index = 0; index < SelectedSensors.Count; index++)
            SelectedSensors[index].Order = index;
    }

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(DetectedSensorCount));
        OnPropertyChanged(nameof(EnabledSelectedSensorCount));
        OnPropertyChanged(nameof(TotalSelectedSensorCount));
    }

    private void UpdateSelectedSensorAvailability(IReadOnlyCollection<DetectedSensorInfo> detectedSensors)
    {
        foreach (var selectedSensor in SelectedSensors)
        {
            var match = SensorMatcher.FindBestMatch(selectedSensor.ToModel(), detectedSensors);
            var viewModel = match is null ? null : new DetectedSensorViewModel(match);
            selectedSensor.UpdateAvailability(viewModel);
        }
    }

    private bool ValidateFormats(out string? errorMessage)
    {
        foreach (var sensor in SelectedSensors)
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
                errorMessage = $"Invalid format: {sensor.Format}{Environment.NewLine}Please use a .NET numeric format such as {{0:0}}C, {{0:0.0}}W, or {{0:0}}%.";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
