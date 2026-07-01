using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OptiSensor.Libre;
using OptiSensor.Models;
using OptiSensor.Settings;

namespace OptiSensor.UI;

internal sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppSettings _settings;
    private readonly SensorDiscoveryService _sensorDiscoveryService = new();

    public MainWindowViewModel(AppSettings settings)
    {
        _settings = settings;

        foreach (var sensor in _settings.SelectedSensors.OrderBy(sensor => sensor.Order))
            SelectedSensors.Add(new SelectedOverlaySensorViewModel(sensor, SyncSelectedSensorsToSettings));

        ReorderSelectedSensors();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DetectedSensorViewModel> DetectedSensors { get; } = [];
    public ObservableCollection<SelectedOverlaySensorViewModel> SelectedSensors { get; } = [];

    public int DetectedSensorCount => DetectedSensors.Count;
    public int EnabledSelectedSensorCount => SelectedSensors.Count(sensor => sensor.Enabled);
    public int TotalSelectedSensorCount => SelectedSensors.Count;

    public void RefreshDetectedSensors()
    {
        var snapshot = _sensorDiscoveryService.Discover();

        DetectedSensors.Clear();
        foreach (var sensor in snapshot.Sensors.OrderBy(sensor => sensor.Category).ThenBy(sensor => sensor.HardwareName).ThenBy(sensor => sensor.SensorName))
            DetectedSensors.Add(new DetectedSensorViewModel(sensor));

        OnCountsChanged();
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
            DisplayName = GetDefaultDisplayName(detectedSensor.Sensor),
            Unit = detectedSensor.Sensor.Unit,
            Format = GetDefaultFormat(detectedSensor.Sensor.Unit),
            Order = SelectedSensors.Count,
            Enabled = true
        };

        SelectedSensors.Add(new SelectedOverlaySensorViewModel(model, SyncSelectedSensorsToSettings));
        ReorderSelectedSensors();
        SyncSelectedSensorsToSettings();
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

    public void Save()
    {
        ReorderSelectedSensors();
        SyncSelectedSensorsToSettings();
        _settings.Save();
    }

    public void Dispose()
    {
        _sensorDiscoveryService.Dispose();
    }

    public static string GetDefaultFormat(string unit)
    {
        return unit switch
        {
            "C" => "{0:0}C",
            "W" => "{0:0}W",
            "%" => "{0:0}%",
            "RPM" => "{0:0}RPM",
            _ => "{0:0}"
        };
    }

    private static string GetDefaultDisplayName(DetectedSensorInfo sensor)
    {
        return sensor.Category switch
        {
            OptiSensorCategory.Gpu => sensor.SensorType == "Temperature" ? "GPU" : string.Empty,
            OptiSensorCategory.Cpu => sensor.SensorType == "Temperature" ? "CPU" : string.Empty,
            OptiSensorCategory.Battery => "BAT",
            OptiSensorCategory.Fan => "FAN",
            _ => string.Empty
        };
    }

    private void SyncSelectedSensorsToSettings()
    {
        _settings.SelectedSensors = SelectedSensors
            .OrderBy(sensor => sensor.Order)
            .Select(sensor => sensor.ToModel())
            .ToList();

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
