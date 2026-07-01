using System.Windows;
using System.Windows.Controls;

namespace OptiSensor.UI.Views.Pages;

public partial class OverlayPage : System.Windows.Controls.UserControl
{
    public OverlayPage()
    {
        InitializeComponent();
    }

    public event EventHandler? MoveUpRequested;
    public event EventHandler? MoveDownRequested;
    public event EventHandler? RemoveRequested;

    internal SelectedOverlaySensorViewModel? SelectedOverlaySensor =>
        SelectedSensorsDataGrid.SelectedItem as SelectedOverlaySensorViewModel;

    public void CommitEdits()
    {
        SelectedSensorsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SelectedSensorsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    internal void SelectOverlaySensor(SelectedOverlaySensorViewModel selectedSensor)
    {
        SelectedSensorsDataGrid.SelectedItem = selectedSensor;
    }

    public void UpdatePreview(string preview)
    {
        PreviewTextBlock.Text = preview;
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveUpRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveDownRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }
}
