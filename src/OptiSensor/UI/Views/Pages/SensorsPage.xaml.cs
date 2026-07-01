using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using OptiSensor.Models;
using OptiSensor.UI;

namespace OptiSensor.UI.Views.Pages;

public partial class SensorsPage : System.Windows.Controls.UserControl
{
    private MainWindowViewModel? _viewModel;
    private double? _savedVerticalOffset;
    private double? _savedHorizontalOffset;
    private string? _savedSelectedSensorId;
    private bool _isPointerDown;

    public SensorsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetectedSensorsDataGrid.PreviewMouseDown += (_, _) => _isPointerDown = true;
        DetectedSensorsDataGrid.PreviewMouseUp += (_, _) => _isPointerDown = false;
        DetectedSensorsDataGrid.MouseLeave += (_, _) => _isPointerDown = false;
    }

    internal DetectedSensorViewModel? SelectedDetectedSensor =>
        DetectedSensorsDataGrid.SelectedItem as DetectedSensorViewModel;

    internal bool ShouldDeferRefresh => _isPointerDown || DetectedSensorsDataGrid.IsKeyboardFocusWithin;

    internal void CaptureScrollPosition()
    {
        var scrollViewer = FindDataGridScrollViewer();
        if (scrollViewer is null)
            return;

        _savedVerticalOffset = scrollViewer.VerticalOffset;
        _savedHorizontalOffset = scrollViewer.HorizontalOffset;
        _savedSelectedSensorId = SelectedDetectedSensor?.SensorId;
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

            if (!string.IsNullOrWhiteSpace(_savedSelectedSensorId))
            {
                var item = DetectedSensorsDataGrid.Items
                    .OfType<DetectedSensorViewModel>()
                    .FirstOrDefault(sensor => string.Equals(sensor.SensorId, _savedSelectedSensorId, StringComparison.OrdinalIgnoreCase));
                if (item is not null)
                    DetectedSensorsDataGrid.SelectedItem = item;
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainWindowViewModel oldViewModel)
        {
            foreach (var filter in oldViewModel.SensorCategoryFilters)
                filter.PropertyChanged -= OnCategoryFilterChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            foreach (var filter in _viewModel.SensorCategoryFilters)
                filter.PropertyChanged += OnCategoryFilterChanged;
        }

        ApplyFilter();
    }

    private void OnCategoryFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SensorCategoryFilterViewModel.IsChecked))
            ApplyFilter();
    }

    private void ApplyFilter()
    {
        var view = CollectionViewSource.GetDefaultView(DetectedSensorsDataGrid.ItemsSource);
        if (view is null)
            return;

        view.GroupDescriptions.Clear();
        view.Filter = item => FilterDetectedSensor(item as DetectedSensorViewModel);
        view.Refresh();
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

    private bool FilterDetectedSensor(DetectedSensorViewModel? sensor)
    {
        if (sensor is null)
            return false;

        return _viewModel?.IsCategoryVisible(sensor.Sensor.Category) ?? true;
    }
}
