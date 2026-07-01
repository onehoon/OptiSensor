using System.ComponentModel;
using System.Windows.Data;

namespace OptiSensor.UI.Views.Pages;

public partial class SensorsPage : System.Windows.Controls.UserControl
{
    public SensorsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyGrouping();
    }

    internal DetectedSensorViewModel? SelectedDetectedSensor =>
        DetectedSensorsDataGrid.SelectedItem as DetectedSensorViewModel;

    private void ApplyGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(DetectedSensorsDataGrid.ItemsSource);
        if (view is null)
            return;

        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DetectedSensorViewModel.Category)));
    }
}
