using System.ComponentModel;
using System.Runtime.CompilerServices;
using OptiSensor.Models;

namespace OptiSensor.UI;

internal sealed class SensorCategoryFilterViewModel : INotifyPropertyChanged
{
    private bool _isChecked;

    public SensorCategoryFilterViewModel(OptiSensorCategory category, bool isChecked = true)
    {
        Category = category;
        _isChecked = isChecked;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OptiSensorCategory Category { get; }
    public string DisplayName => Category.ToString();

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;

            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }
}