using System.ComponentModel;
using System.Runtime.CompilerServices;
using OptiSensor.Models;

namespace OptiSensor.UI;

internal sealed class SelectedOverlaySensorViewModel : INotifyPropertyChanged
{
    private readonly Action _changed;
    private bool _enabled;
    private int _order;
    private string _displayName;
    private string _format;

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
            Format = string.IsNullOrWhiteSpace(Format) ? MainWindowViewModel.GetDefaultFormat(Unit) : Format,
            Order = Order,
            Enabled = Enabled
        };
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        _changed();
    }
}
