using System.ComponentModel;
using OptiSensor.Models;

namespace OptiSensor.UI;

internal sealed class DetectedSensorViewModel : INotifyPropertyChanged
{
    private readonly Action<DetectedSensorViewModel>? _selectionChanged;
    private bool _isSelected;
    private bool _suppressSelectionChanged;

    public DetectedSensorViewModel(DetectedSensorInfo sensor)
        : this(sensor, selectionChanged: null)
    {
    }

    public DetectedSensorViewModel(DetectedSensorInfo sensor, Action<DetectedSensorViewModel>? selectionChanged)
    {
        Sensor = sensor;
        _selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DetectedSensorInfo Sensor { get; }
    public string SensorId => Sensor.SensorId;
    public string Category => Sensor.Category.ToString();
    public string HardwareType => Sensor.HardwareType;
    public string HardwareName => Sensor.HardwareName;
    public string SensorName => Sensor.SensorName;
    public string SensorType => Sensor.SensorType;
    public string Unit => Sensor.Unit;
    public string ValueText => Sensor.Value is float value
        ? $"{value:0.#} {GetDisplayUnit()}"
        : string.Empty;

    private string GetDisplayUnit()
    {
        return string.Equals(Sensor.SensorType, "Temperature", StringComparison.OrdinalIgnoreCase)
            ? "°C"
            : Sensor.Unit;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));

            if (!_suppressSelectionChanged)
                _selectionChanged?.Invoke(this);
        }
    }

    public void SetSelectedSilently(bool isSelected)
    {
        _suppressSelectionChanged = true;
        try
        {
            IsSelected = isSelected;
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }
}
