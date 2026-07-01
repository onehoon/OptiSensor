using System.ComponentModel;
using System.Runtime.CompilerServices;
using OptiSensor.Libre;
using OptiSensor.Models;
using OptiSensor.Overlay;

namespace OptiSensor.UI;

internal sealed class SelectedOverlaySensorViewModel : INotifyPropertyChanged
{
    private readonly Action _changed;
    private bool _enabled;
    private int _order;
    private string _displayName;
    private string _format;
    private bool _isAvailable;
    private bool _hasPossibleMatch;
    private string _currentValueText = "Not found";

    public SelectedOverlaySensorViewModel(SelectedOverlaySensor sensor, Action changed)
    {
        _changed = changed;
        SensorId = sensor.SensorId;
        HardwareType = sensor.HardwareType;
        HardwareName = sensor.HardwareName;
        SensorType = sensor.SensorType;
        SensorName = sensor.SensorName;
        Category = sensor.Category;
        Unit = sensor.Unit;
        _enabled = sensor.Enabled;
        _order = sensor.Order;
        _displayName = sensor.DisplayName;
        _format = sensor.Format;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SensorId { get; }
    public string HardwareType { get; }
    public string HardwareName { get; }
    public string SensorType { get; }
    public string SensorName { get; }
    public OptiSensorCategory Category { get; }
    public string Unit { get; }
    public string AvailabilityText => IsAvailable ? "Yes" : HasPossibleMatch ? "Possible match (not used)" : "No";

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public int Order
    {
        get => _order;
        set => SetField(ref _order, value);
    }

    public void SetOrderSilently(int order)
    {
        SetField(ref _order, order, markChanged: false, propertyName: nameof(Order));
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string Format
    {
        get => _format;
        set => SetField(ref _format, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetField(ref _isAvailable, value, markChanged: false))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailabilityText)));
        }
    }

    public bool HasPossibleMatch
    {
        get => _hasPossibleMatch;
        private set
        {
            if (SetField(ref _hasPossibleMatch, value, markChanged: false))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailabilityText)));
        }
    }

    public string CurrentValueText
    {
        get => _currentValueText;
        private set => SetField(ref _currentValueText, value, markChanged: false);
    }

    public void UpdateAvailability(DetectedSensorViewModel? detectedSensor)
    {
        var exactMatch = detectedSensor is not null && SensorMatcher.HasExactSensorIdMatch(ToModel(), detectedSensor.Sensor);
        IsAvailable = detectedSensor is not null && exactMatch;
        HasPossibleMatch = detectedSensor is not null && !exactMatch;
        CurrentValueText = detectedSensor?.ValueText ?? "Not found";
    }

    public SelectedOverlaySensor ToModel()
    {
        return new SelectedOverlaySensor
        {
            SensorId = SensorId,
            HardwareType = HardwareType,
            HardwareName = HardwareName,
            SensorType = SensorType,
            SensorName = SensorName,
            Category = Category,
            DisplayName = DisplayName,
            Unit = Unit,
            Format = string.IsNullOrWhiteSpace(Format) ? SensorFormatDefaults.GetDefaultFormat(Unit) : Format,
            Order = Order,
            Enabled = Enabled
        };
    }

    private bool SetField<T>(ref T field, T value, bool markChanged = true, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (markChanged)
            _changed();
        return true;
    }
}
