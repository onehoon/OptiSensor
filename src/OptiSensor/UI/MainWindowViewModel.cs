using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OptiSensor.App;
using OptiSensor.Libre;
using OptiSensor.Models;
using OptiSensor.Overlay;
using OptiSensor.Settings;

namespace OptiSensor.UI;

internal sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const string UngroupedGroupId = "ungrouped";
    private const string UngroupedGroupName = "Ungrouped";
    private readonly AppSettings _settings;
    private readonly OverlayLineBuilder _overlayLineBuilder = new();
    private readonly SensorDiscoveryService _sensorDiscoveryService;
    private readonly ObservableCollection<SelectedOverlaySensorViewModel> _emptySelectedSensors = [];
    private bool _hasUnsavedChanges;
    private bool _isRefreshing;
    private OverlayGroupViewModel? _selectedOverlayGroup;
    private bool _initialSensorRefreshCompleted;
    private bool _disposed;

    public MainWindowViewModel(AppSettings settings)
    {
        _settings = settings;
        _sensorDiscoveryService = new SensorDiscoveryService(settings.SensorSource);

        var categoryFilterSnapshot = _settings.GetActiveSensorCategoryFilterSnapshot();
        foreach (var category in Enum.GetValues<OptiSensorCategory>())
        {
            var filter = new SensorCategoryFilterViewModel(category, categoryFilterSnapshot[category]);
            filter.PropertyChanged += SensorCategoryFilter_PropertyChanged;
            SensorCategoryFilters.Add(filter);
        }

        foreach (var group in _settings.GetOverlayGroupsSnapshot())
            OverlayGroups.Add(new OverlayGroupViewModel(
                group,
                SyncOverlayGroupsToSettings,
                SyncSelectedSensorsToSettings,
                MoveSensorToGroup));

        EnsureUngroupedGroup();

        if (VisibleOverlayGroups.Count == 0)
            OverlayGroups.Add(CreateGroup("GPU", OverlayGroups.Count));

        SelectedOverlayGroup = VisibleOverlayGroups.OrderBy(group => group.Order).FirstOrDefault();
        ReorderOverlayGroups(markChanged: false);
        foreach (var group in OverlayGroups)
            ReorderSensors(group.Sensors, markChanged: false);
        RebuildAllSelectedSensors();
        SyncOverlayGroupsToSettings(markUnsaved: false);
        HasUnsavedChanges = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DetectedSensorViewModel> DetectedSensors { get; } = [];
    public ObservableCollection<SensorCategoryFilterViewModel> SensorCategoryFilters { get; } = [];
    public ObservableCollection<OverlayGroupViewModel> OverlayGroups { get; } = [];
    public ObservableCollection<SelectedOverlaySensorViewModel> AllSelectedSensors { get; } = [];
    public ObservableCollection<SelectedOverlaySensorViewModel> VisibleSelectedSensors { get; } = [];
    public IReadOnlyList<OverlayGroupViewModel> VisibleOverlayGroups =>
        OverlayGroups.Where(group => !string.Equals(group.Id, UngroupedGroupId, StringComparison.OrdinalIgnoreCase)).ToArray();
    public IReadOnlyList<OverlayGroupViewModel> GroupSelectionGroups =>
        OverlayGroups.OrderBy(group => group.Order).ToArray();
    public ObservableCollection<SelectedOverlaySensorViewModel> SelectedSensors => SelectedOverlayGroup?.Sensors ?? _emptySelectedSensors;

    public int DetectedSensorCount => DetectedSensors.Count;
    public int EnabledSelectedSensorCount => OverlayGroups.Where(group => group.Enabled).Sum(group => group.Sensors.Count(sensor => sensor.Enabled));
    public int TotalSelectedSensorCount => OverlayGroups.Sum(group => group.Sensors.Count);
    public int OverlayGroupCount => VisibleOverlayGroups.Count;
    public string SettingsStateText => HasUnsavedChanges ? "Unsaved changes" : "Saved";
    public string SensorsTitle => _settings.SensorSource == SensorSourceKind.HwInfo
        ? "Sensors (HWiNFO)"
        : "Sensors (Libre)";

    public OverlayGroupViewModel? SelectedOverlayGroup
    {
        get => _selectedOverlayGroup;
        set
        {
            if (_selectedOverlayGroup == value)
                return;

            _selectedOverlayGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSensors));
            RebuildVisibleSelectedSensors();
            SyncDetectedSensorSelectionStates();
            OnCountsChanged();
        }
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges == value)
                return;

            _hasUnsavedChanges = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SettingsStateText));
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (_isRefreshing == value)
                return;

            _isRefreshing = value;
            OnPropertyChanged();
        }
    }

    public async Task RefreshDetectedSensorsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IsRefreshing = true;
        try
        {
            var includedCategories = SensorCategoryFilters
                .Where(filter => filter.IsChecked)
                .Select(filter => filter.Category)
                .ToHashSet();

            foreach (var category in OverlayGroups
                         .SelectMany(group => group.Sensors)
                         .Select(sensor => sensor.Category))
            {
                includedCategories.Add(category);
            }

            var useFastStart = !_initialSensorRefreshCompleted;
            var snapshot = await Task.Run(
                () => _sensorDiscoveryService.Discover(includedCategories.ToArray(), fastStart: useFastStart),
                cancellationToken).ConfigureAwait(true);

            // The reader read completed before checking cancellation again: a shutdown
            // requested mid-read must not still mutate UI collections afterward.
            cancellationToken.ThrowIfCancellationRequested();

            _initialSensorRefreshCompleted = true;

            SyncDetectedSensors(snapshot.Sensors);

            UpdateSelectedSensorAvailability(snapshot.Sensors);
            OnCountsChanged();

        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public bool AddDetectedSensor(DetectedSensorViewModel detectedSensor)
    {
        if (AllSelectedSensors.Any(sensor => string.Equals(sensor.SensorId, detectedSensor.SensorId, StringComparison.OrdinalIgnoreCase)))
            return false;

        var ungrouped = EnsureUngroupedGroup();

        var model = new SelectedOverlaySensor
        {
            SensorId = detectedSensor.Sensor.SensorId,
            HardwareType = detectedSensor.Sensor.HardwareType,
            HardwareName = detectedSensor.Sensor.HardwareName,
            SensorType = detectedSensor.Sensor.SensorType,
            SensorName = detectedSensor.Sensor.SensorName,
            Category = detectedSensor.Sensor.Category,
            DisplayName = SensorFormatDefaults.GetDefaultDisplayName(detectedSensor.Sensor),
            Unit = detectedSensor.Sensor.Unit,
            Format = SensorFormatDefaults.GetDefaultFormat(detectedSensor.Sensor.Unit),
            Order = ungrouped.Sensors.Count,
            Enabled = true
        };

        ungrouped.Sensors.Add(new SelectedOverlaySensorViewModel(model, ungrouped.Id, SyncSelectedSensorsToSettings, MoveSensorToGroup));
        ReorderSensors(ungrouped.Sensors, markChanged: true);
        RebuildAllSelectedSensors();
        SyncOverlayGroupsToSettings();
        UpdateSelectedSensorAvailability(DetectedSensors.Select(sensor => sensor.Sensor).ToArray());
        return true;
    }

    public void ToggleDetectedSensorSelection(DetectedSensorViewModel detectedSensor)
    {
        if (detectedSensor.IsSelected)
        {
            if (!AddDetectedSensor(detectedSensor))
                detectedSensor.SetSelectedSilently(true);

            return;
        }

        var selectedSensor = AllSelectedSensors.FirstOrDefault(sensor =>
            string.Equals(sensor.SensorId, detectedSensor.SensorId, StringComparison.OrdinalIgnoreCase));
        if (selectedSensor is null)
            return;

        RemoveSelectedSensor(selectedSensor);
    }

    public void RemoveSelectedSensor(SelectedOverlaySensorViewModel selectedSensor)
    {
        var ownerGroup = OverlayGroups.FirstOrDefault(group => group.Sensors.Contains(selectedSensor));
        if (ownerGroup is null)
            return;

        ownerGroup.Sensors.Remove(selectedSensor);
        ReorderSensors(ownerGroup.Sensors, markChanged: true);
        RebuildAllSelectedSensors();
        SyncOverlayGroupsToSettings();
        SyncDetectedSensorSelectionStates();
    }

    public void MoveSelectedSensorUp(SelectedOverlaySensorViewModel selectedSensor)
    {
        var ownerGroup = OverlayGroups.FirstOrDefault(group => group.Sensors.Contains(selectedSensor));
        if (ownerGroup is null)
            return;

        var index = ownerGroup.Sensors.IndexOf(selectedSensor);
        if (index <= 0)
            return;

        ownerGroup.Sensors.Move(index, index - 1);
        ReorderSensors(ownerGroup.Sensors, markChanged: true);
        RebuildAllSelectedSensors();
        SyncOverlayGroupsToSettings();
    }

    public void MoveSelectedSensorDown(SelectedOverlaySensorViewModel selectedSensor)
    {
        var ownerGroup = OverlayGroups.FirstOrDefault(group => group.Sensors.Contains(selectedSensor));
        if (ownerGroup is null)
            return;

        var index = ownerGroup.Sensors.IndexOf(selectedSensor);
        if (index < 0 || index >= ownerGroup.Sensors.Count - 1)
            return;

        ownerGroup.Sensors.Move(index, index + 1);
        ReorderSensors(ownerGroup.Sensors, markChanged: true);
        RebuildAllSelectedSensors();
        SyncOverlayGroupsToSettings();
    }

    /// <summary>
    /// Validates and normalizes the current Draft, then writes a deep snapshot into
    /// <paramref name="candidate"/>'s active source profile. Never touches the live
    /// AppSettings this view model was constructed with, and never saves to disk.
    /// </summary>
    public bool TryApplyDraftTo(AppSettings candidate, out string? errorMessage)
    {
        foreach (var group in OverlayGroups)
            ReorderSensors(group.Sensors, markChanged: true);
        if (!ValidateFormats(out errorMessage))
            return false;

        var orderedGroups = OverlayGroups.OrderBy(group => group.Order).Select(group => group.ToModel()).ToArray();
        var categoryFilters = SensorCategoryFilters
            .Select(filter => new KeyValuePair<OptiSensorCategory, bool>(filter.Category, filter.IsChecked))
            .ToArray();

        candidate.ReplaceOverlayGroups(orderedGroups);
        candidate.ReplaceSensorCategoryFilters(categoryFilters);

        errorMessage = null;
        return true;
    }

    /// <summary>Called by MainWindow only after the candidate has been saved to disk and applied to the live AppSettings.</summary>
    public void MarkSaved()
    {
        RebuildVisibleSelectedSensors();
        HasUnsavedChanges = false;
    }

    public string GetOverlayPreviewText()
    {
        var snapshot = new LibreSensorSnapshot(
            DetectedSensors.Select(sensor => sensor.Sensor).ToArray(),
            new LibreReadMetrics(0, 0, DetectedSensors.Count, 0, 0, 0, 0, false));
        var groups = OverlayGroups
            .Where(group => !string.Equals(group.Id, UngroupedGroupId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(group => group.Order)
            .Select(group => group.ToModel())
            .ToArray();

        var line = _overlayLineBuilder.BuildLine(snapshot, groups);
        if (!string.IsNullOrWhiteSpace(line))
            return line;

        var enabledGroups = OverlayGroups
            .Where(group => !string.Equals(group.Id, UngroupedGroupId, StringComparison.OrdinalIgnoreCase))
            .Where(group => group.Enabled)
            .OrderBy(group => group.Order)
            .Select(group => string.IsNullOrWhiteSpace(group.Name) ? "(unnamed group)" : group.Name)
            .ToArray();

        if (enabledGroups.Length == 0)
            return "No enabled groups.";

        return string.Join(" | ", enabledGroups.Select(groupName => $"{groupName} (empty)"));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sensorDiscoveryService.Dispose();
    }

    public void AddOverlayGroup()
    {
        var group = CreateGroup(string.Empty, OverlayGroups.Count);
        OverlayGroups.Add(group);
        SelectedOverlayGroup = group;
        ReorderOverlayGroups(markChanged: true);
        SyncOverlayGroupsToSettings();
        NotifyOverlayGroupCollectionsChanged();
    }

    public void RemoveOverlayGroup(OverlayGroupViewModel group)
    {
        if (string.Equals(group.Id, UngroupedGroupId, StringComparison.OrdinalIgnoreCase))
            return;

        OverlayGroups.Remove(group);
        if (VisibleOverlayGroups.Count == 0)
            OverlayGroups.Add(CreateGroup("GPU", OverlayGroups.Count));

        SelectedOverlayGroup = VisibleOverlayGroups.OrderBy(candidate => candidate.Order).FirstOrDefault();
        ReorderOverlayGroups(markChanged: true);
        RebuildAllSelectedSensors();
        SyncOverlayGroupsToSettings();
        NotifyOverlayGroupCollectionsChanged();
        SyncDetectedSensorSelectionStates();
    }

    public void MoveOverlayGroupUp(OverlayGroupViewModel group)
    {
        var index = OverlayGroups.IndexOf(group);
        if (index <= 1)
            return;

        OverlayGroups.Move(index, index - 1);
        ReorderOverlayGroups(markChanged: true);
        SyncOverlayGroupsToSettings();
        NotifyOverlayGroupCollectionsChanged();
    }

    public void MoveOverlayGroupDown(OverlayGroupViewModel group)
    {
        var index = OverlayGroups.IndexOf(group);
        if (index < 0 || index >= OverlayGroups.Count - 1)
            return;

        OverlayGroups.Move(index, index + 1);
        ReorderOverlayGroups(markChanged: true);
        SyncOverlayGroupsToSettings();
        NotifyOverlayGroupCollectionsChanged();
    }

    // Despite the name (kept for the existing OverlayGroupViewModel/SelectedOverlaySensorViewModel
    // callback wiring), these no longer touch AppSettings: they only update in-memory Draft/UI
    // state. AppSettings is only written once, by TryApplyDraftTo, after Save validation succeeds.
    private void SyncSelectedSensorsToSettings()
    {
        HasUnsavedChanges = true;
        OnCountsChanged();
    }

    private void SyncOverlayGroupsToSettings()
    {
        SyncOverlayGroupsToSettings(markUnsaved: true);
    }

    private void SyncOverlayGroupsToSettings(bool markUnsaved)
    {
        RebuildVisibleSelectedSensors();

        if (markUnsaved)
            HasUnsavedChanges = true;

        OnCountsChanged();
    }

    private void NotifyOverlayGroupCollectionsChanged()
    {
        OnPropertyChanged(nameof(VisibleOverlayGroups));
        OnPropertyChanged(nameof(GroupSelectionGroups));
    }

    private void EnsureSelectedGroup()
    {
        if (SelectedOverlayGroup is not null && !string.Equals(SelectedOverlayGroup.Id, UngroupedGroupId, StringComparison.OrdinalIgnoreCase))
            return;

        SelectedOverlayGroup = VisibleOverlayGroups.OrderBy(group => group.Order).FirstOrDefault();
        if (SelectedOverlayGroup is not null)
            return;

        var created = CreateGroup("GPU", OverlayGroups.Count);
        OverlayGroups.Add(created);
        SelectedOverlayGroup = created;
    }

    private OverlayGroupViewModel EnsureUngroupedGroup()
    {
        var existing = OverlayGroups.FirstOrDefault(group => string.Equals(group.Id, UngroupedGroupId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!string.Equals(existing.Name, UngroupedGroupName, StringComparison.Ordinal))
                existing.Name = UngroupedGroupName;

            if (existing.Enabled)
                existing.Enabled = false;

            return existing;
        }

        var ungrouped = new OverlayGroupViewModel(
            new OverlayGroup
            {
                Id = UngroupedGroupId,
                Name = UngroupedGroupName,
                Order = 0,
                Enabled = false,
                Sensors = []
            },
            SyncOverlayGroupsToSettings,
            SyncSelectedSensorsToSettings,
            MoveSensorToGroup);

        OverlayGroups.Insert(0, ungrouped);
        return ungrouped;
    }

    private OverlayGroupViewModel CreateGroup(string name, int order)
    {
        return new OverlayGroupViewModel(
            new OverlayGroup
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Order = order,
                Enabled = true,
                Sensors = []
            },
            SyncOverlayGroupsToSettings,
            SyncSelectedSensorsToSettings,
            MoveSensorToGroup);
    }

    private bool MoveSensorToGroup(SelectedOverlaySensorViewModel sensor, string targetGroupId)
    {
        var sourceGroup = OverlayGroups.FirstOrDefault(group => group.Sensors.Contains(sensor));
        if (sourceGroup is null)
            return false;

        if (string.Equals(sourceGroup.Id, targetGroupId, StringComparison.OrdinalIgnoreCase))
            return true;

        var targetGroup = OverlayGroups.FirstOrDefault(group =>
            string.Equals(group.Id, targetGroupId, StringComparison.OrdinalIgnoreCase));
        if (targetGroup is null)
            return false;

        if (targetGroup.Sensors.Any(existing => string.Equals(existing.SensorId, sensor.SensorId, StringComparison.OrdinalIgnoreCase)))
            return false;

        sourceGroup.Sensors.Remove(sensor);
        ReorderSensors(sourceGroup.Sensors, markChanged: true);

        targetGroup.Sensors.Add(sensor);
        sensor.SetGroupSilently(targetGroup.Id);
        ReorderSensors(targetGroup.Sensors, markChanged: true);

        RebuildAllSelectedSensors();
        SyncOverlayGroupsToSettings();
        SyncDetectedSensorSelectionStates();
        return true;
    }

    private void ReorderOverlayGroups(bool markChanged)
    {
        for (var index = 0; index < OverlayGroups.Count; index++)
        {
            if (markChanged)
                OverlayGroups[index].Order = index;
            else
                OverlayGroups[index].SetOrderSilently(index);
        }
    }

    private static void ReorderSensors(ObservableCollection<SelectedOverlaySensorViewModel> sensors, bool markChanged)
    {
        for (var index = 0; index < sensors.Count; index++)
        {
            if (markChanged)
                sensors[index].Order = index;
            else
                sensors[index].SetOrderSilently(index);
        }
    }

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(DetectedSensorCount));
        OnPropertyChanged(nameof(EnabledSelectedSensorCount));
        OnPropertyChanged(nameof(TotalSelectedSensorCount));
        OnPropertyChanged(nameof(OverlayGroupCount));
    }

    private void UpdateSelectedSensorAvailability(IReadOnlyCollection<DetectedSensorInfo> detectedSensors)
    {
        foreach (var selectedSensor in OverlayGroups.SelectMany(group => group.Sensors))
        {
            var match = SensorMatcher.FindBestMatch(selectedSensor.ToModel(), detectedSensors);
            var viewModel = match is null ? null : new DetectedSensorViewModel(match);
            selectedSensor.UpdateAvailability(viewModel);
        }

        SyncDetectedSensorSelectionStates();
    }

    private bool ValidateFormats(out string? errorMessage)
    {
        foreach (var sensor in OverlayGroups.SelectMany(group => group.Sensors))
        {
            var format = string.IsNullOrWhiteSpace(sensor.Format)
                ? SensorFormatDefaults.GetDefaultFormat(sensor.Unit)
                : sensor.Format;

            try
            {
                _ = string.Format(CultureInfo.InvariantCulture, format, 123.4f);
            }
            catch (FormatException)
            {
                errorMessage = $"Invalid format: {sensor.Format}{Environment.NewLine}Please use a .NET numeric format such as {{0:0}}°C, {{0:0.0}}W, or {{0:0}}%.";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }

    private bool IsSensorSelectedInCurrentGroup(string sensorId)
    {
        return AllSelectedSensors.Any(sensor => string.Equals(sensor.SensorId, sensorId, StringComparison.OrdinalIgnoreCase));
    }

    private void SensorCategoryFilter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SensorCategoryFilterViewModel.IsChecked))
            return;

        HasUnsavedChanges = true;
    }

    private void SyncDetectedSensorSelectionStates()
    {
        foreach (var detectedSensor in DetectedSensors)
            detectedSensor.SetSelectedSilently(IsSensorSelectedInCurrentGroup(detectedSensor.SensorId));
    }

    public bool IsCategoryVisible(OptiSensorCategory category)
    {
        var filter = SensorCategoryFilters.FirstOrDefault(item => item.Category == category);
        return filter?.IsChecked ?? true;
    }

    private void SyncDetectedSensors(IReadOnlyCollection<DetectedSensorInfo> sensors)
    {
        var orderedSensors = sensors
            .OrderBy(sensor => sensor.Category)
            .ThenBy(sensor => sensor.HardwareName)
            .ThenBy(sensor => sensor.SensorName)
            .ToArray();

        var existingById = DetectedSensors
            .GroupBy(sensor => sensor.SensorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var target = new List<DetectedSensorViewModel>(orderedSensors.Length);

        foreach (var sensor in orderedSensors)
        {
            if (existingById.TryGetValue(sensor.SensorId, out var viewModel))
            {
                viewModel.UpdateSensor(sensor);
                existingById.Remove(sensor.SensorId);
            }
            else
            {
                viewModel = new DetectedSensorViewModel(sensor, ToggleDetectedSensorSelection);
            }

            viewModel.SetSelectedSilently(IsSensorSelectedInCurrentGroup(sensor.SensorId));
            target.Add(viewModel);
        }

        foreach (var stale in existingById.Values.ToArray())
            DetectedSensors.Remove(stale);

        for (var index = 0; index < target.Count; index++)
        {
            var expected = target[index];
            if (index < DetectedSensors.Count && ReferenceEquals(DetectedSensors[index], expected))
                continue;

            var currentIndex = DetectedSensors.IndexOf(expected);
            if (currentIndex >= 0)
            {
                DetectedSensors.Move(currentIndex, index);
            }
            else
            {
                DetectedSensors.Insert(index, expected);
            }
        }

        while (DetectedSensors.Count > target.Count)
            DetectedSensors.RemoveAt(DetectedSensors.Count - 1);
    }

    private void RebuildAllSelectedSensors()
    {
        AllSelectedSensors.Clear();

        foreach (var sensor in OverlayGroups
                     .SelectMany(group => group.Sensors)
                     .OrderBy(sensor => sensor.GroupId == UngroupedGroupId ? 0 : 1)
                     .ThenBy(sensor => sensor.GroupId)
                     .ThenBy(sensor => sensor.Order))
        {
            AllSelectedSensors.Add(sensor);
        }

        RebuildVisibleSelectedSensors();
        OnPropertyChanged(nameof(SelectedSensors));
    }

    private void RebuildVisibleSelectedSensors()
    {
        VisibleSelectedSensors.Clear();

        var selectedGroupId = SelectedOverlayGroup?.Id;
        IEnumerable<SelectedOverlaySensorViewModel> visibleSensors = AllSelectedSensors;

        if (!string.IsNullOrWhiteSpace(selectedGroupId))
        {
            visibleSensors = visibleSensors.Where(sensor =>
                string.Equals(sensor.GroupId, UngroupedGroupId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sensor.GroupId, selectedGroupId, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var sensor in visibleSensors)
            VisibleSelectedSensors.Add(sensor);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

}
