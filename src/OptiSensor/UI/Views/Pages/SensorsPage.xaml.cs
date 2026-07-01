using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace OptiSensor.UI.Views.Pages;

public partial class SensorsPage : System.Windows.Controls.UserControl
{
    private readonly Dictionary<string, bool> _categoryExpandedStates = new(StringComparer.OrdinalIgnoreCase);
    private double? _savedVerticalOffset;
    private double? _savedHorizontalOffset;

    public SensorsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyGrouping();
    }

    internal DetectedSensorViewModel? SelectedDetectedSensor =>
        DetectedSensorsDataGrid.SelectedItem as DetectedSensorViewModel;

    internal void CaptureScrollPosition()
    {
        var scrollViewer = FindDataGridScrollViewer();
        if (scrollViewer is null)
            return;

        _savedVerticalOffset = scrollViewer.VerticalOffset;
        _savedHorizontalOffset = scrollViewer.HorizontalOffset;
    }

    internal void RestoreScrollPosition()
    {
        if (_savedVerticalOffset is null && _savedHorizontalOffset is null)
            return;

        var targetVerticalOffset = _savedVerticalOffset;
        var targetHorizontalOffset = _savedHorizontalOffset;

        Dispatcher.BeginInvoke(() =>
        {
            var scrollViewer = FindDataGridScrollViewer();
            if (scrollViewer is null)
                return;

            if (targetVerticalOffset is not null)
                scrollViewer.ScrollToVerticalOffset(Math.Min(targetVerticalOffset.Value, scrollViewer.ScrollableHeight));

            if (targetHorizontalOffset is not null)
                scrollViewer.ScrollToHorizontalOffset(Math.Min(targetHorizontalOffset.Value, scrollViewer.ScrollableWidth));
        }, DispatcherPriority.Loaded);
    }

    private void ApplyGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(DetectedSensorsDataGrid.ItemsSource);
        if (view is null)
            return;

        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DetectedSensorViewModel.Category)));
    }

    private void GroupItem_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.GroupItem groupItem)
            return;

        var groupName = GetGroupName(groupItem.DataContext);
        if (groupName is null)
            return;

        var expander = FindDescendant<System.Windows.Controls.Expander>(groupItem);
        if (expander is null)
            return;

        // Mark initialized group key so startup Expanded events from template creation are ignored.
        expander.Tag = groupName;

        if (_categoryExpandedStates.TryGetValue(groupName, out var isExpanded))
            expander.IsExpanded = isExpanded;
    }

    private void GroupExpander_Expanded(object sender, System.Windows.RoutedEventArgs e)
    {
        UpdateGroupExpandedState(sender, isExpanded: true);
    }

    private void GroupExpander_Collapsed(object sender, System.Windows.RoutedEventArgs e)
    {
        UpdateGroupExpandedState(sender, isExpanded: false);
    }

    private System.Windows.Controls.ScrollViewer? FindDataGridScrollViewer()
    {
        return FindDescendant<System.Windows.Controls.ScrollViewer>(DetectedSensorsDataGrid);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                return typed;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static string? GetGroupName(object? dataContext)
    {
        return dataContext is CollectionViewGroup group
            ? group.Name?.ToString()
            : null;
    }

    private void UpdateGroupExpandedState(object sender, bool isExpanded)
    {
        if (sender is not System.Windows.Controls.Expander expander)
            return;

        // Ignore template initialization events that fire before GroupItem_Loaded wires the group key.
        if (expander.Tag is not string groupName || string.IsNullOrWhiteSpace(groupName))
            return;

        if (string.IsNullOrWhiteSpace(groupName))
            return;

        _categoryExpandedStates[groupName] = isExpanded;
    }
}
