using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;

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

    private void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
            return;

        var sample = string.IsNullOrEmpty(textBox.Text) ? "MMMMM" : textBox.Text + "M";
        var typeface = new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch);
        var formattedText = new FormattedText(
            sample,
            CultureInfo.CurrentCulture,
            textBox.FlowDirection,
            typeface,
            textBox.FontSize,
            System.Windows.Media.Brushes.Transparent,
            VisualTreeHelper.GetDpi(textBox).PixelsPerDip);
        var horizontalChrome = textBox.Padding.Left + textBox.Padding.Right + textBox.BorderThickness.Left + textBox.BorderThickness.Right;
        textBox.Width = Math.Max(32, Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace + horizontalChrome));
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
