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
    public event EventHandler? AddGroupRequested;
    public event EventHandler? MoveGroupUpRequested;
    public event EventHandler? MoveGroupDownRequested;
    public event EventHandler? RemoveGroupRequested;
    public event EventHandler? SaveRequested;

    internal SelectedOverlaySensorViewModel? SelectedOverlaySensor =>
        SelectedSensorsDataGrid.SelectedItem as SelectedOverlaySensorViewModel;

    internal OverlayGroupViewModel? SelectedGroup =>
        GroupsListBox.SelectedItem as OverlayGroupViewModel;

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

    private void AddGroupButton_Click(object sender, RoutedEventArgs e)
    {
        AddGroupRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MoveGroupUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveGroupUpRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MoveGroupDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveGroupDownRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveGroupButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveGroupRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }
}
