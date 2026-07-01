using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OptiSensor.Models;

namespace OptiSensor.UI;

internal sealed class OverlayGroupViewModel : INotifyPropertyChanged
{
    private readonly Action _changed;
    private string _name;
    private int _order;
    private bool _enabled;

    public OverlayGroupViewModel(
        OverlayGroup group,
        Action changed,
        Func<SelectedOverlaySensorViewModel, string, bool>? moveSensorToGroup = null)
    {
        _changed = changed;
        Id = group.Id;
        _name = group.Name;
        _order = group.Order;
        _enabled = group.Enabled;

        foreach (var sensor in group.Sensors.OrderBy(sensor => sensor.Order))
            Sensors.Add(new SelectedOverlaySensorViewModel(sensor, group.Id, changed, moveSensorToGroup));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public ObservableCollection<SelectedOverlaySensorViewModel> Sensors { get; } = [];

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public int Order
    {
        get => _order;
        set => SetField(ref _order, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(unnamed group)" : Name;

    public void SetOrderSilently(int order)
    {
        SetField(ref _order, order, markChanged: false, propertyName: nameof(Order));
    }

    public OverlayGroup ToModel()
    {
        return new OverlayGroup
        {
            Id = Id,
            Name = Name,
            Order = Order,
            Enabled = Enabled,
            Sensors = Sensors
                .OrderBy(sensor => sensor.Order)
                .Select(sensor => sensor.ToModel())
                .ToList()
        };
    }

    private bool SetField<T>(ref T field, T value, bool markChanged = true, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(Name))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        if (markChanged)
            _changed();
        return true;
    }
}
