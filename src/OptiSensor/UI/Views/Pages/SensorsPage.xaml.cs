using System.Windows;

namespace OptiSensor.UI.Views.Pages;

public partial class SensorsPage : System.Windows.Controls.UserControl
{
    public SensorsPage()
    {
        InitializeComponent();
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? AddRequested;

    internal DetectedSensorViewModel? SelectedDetectedSensor =>
        DetectedSensorsDataGrid.SelectedItem as DetectedSensorViewModel;

    public bool IsRefreshEnabled
    {
        get => RefreshButton.IsEnabled;
        set => RefreshButton.IsEnabled = value;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        AddRequested?.Invoke(this, EventArgs.Empty);
    }
}
