using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using OptiSensor.App;
using OptiSensor.OptiScalerUpdate;

namespace OptiSensor.UI;

/// <summary>
/// The one modal that owns a single OptiScaler-replace operation: pick a game folder, run folder
/// discovery (<see cref="OptiScalerTargetDiscovery"/>), and on Replace hand the discovered existing
/// proxy DLL path to <see cref="OptiScalerUpdateService"/>. All state is local to this dialog; the
/// download starts only when the user clicks Replace.
/// </summary>
public partial class OptiScalerReplaceWindow : Window
{
    private readonly OptiScalerTargetDiscovery _discovery = new(new SystemFileVersionReader());
    private readonly OptiScalerUpdateService _updateService = new();

    private string? _selectedFolder;
    private OptiScalerDiscoveryResult? _lastDiscovery;
    private CancellationTokenSource? _replaceCts;
    private bool _busy;

    public OptiScalerReplaceWindow()
    {
        InitializeComponent();
        UpdateButtons();
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var picker = new OpenFolderDialog { Title = "Select the game folder that contains OptiScaler" };
        if (_selectedFolder is not null && Directory.Exists(_selectedFolder))
            picker.InitialDirectory = _selectedFolder;
        if (picker.ShowDialog(this) != true)
            return;

        _selectedFolder = picker.FolderName;
        FolderText.Text = _selectedFolder;
        StatusText.Text = string.Empty;
        RunDiscovery();
    }

    private void RunDiscovery()
    {
        if (_selectedFolder is null)
            return;

        _lastDiscovery = _discovery.Discover(_selectedFolder);
        DetectedText.Text = OptiScalerReplacePresentation.DescribeDiscovery(_lastDiscovery);
        UpdateButtons();
    }

    private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !OptiScalerReplacePresentation.CanReplace(_lastDiscovery, busy: false))
            return;

        var targetPath = _lastDiscovery!.TargetDllPath!;
        _busy = true;
        _replaceCts = new CancellationTokenSource();
        StatusText.Text = "Replacing...";
        UpdateButtons();

        OptiScalerUpdateResult result;
        try
        {
            result = await _updateService.UpdateAsync(targetPath, _replaceCts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SimpleLog.TryWriteException(ex);
            result = OptiScalerUpdateResult.Failed(OptiScalerUpdateReason.UnexpectedFailure,
                "The OptiScaler replacement did not complete.");
        }
        finally
        {
            _replaceCts.Dispose();
            _replaceCts = null;
            _busy = false;
        }

        StatusText.Text = OptiScalerReplacePresentation.DescribeResult(result);

        // Re-run discovery so the shown version reflects the file that is actually on disk now.
        RunDiscovery();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _replaceCts?.Cancel();
            return;
        }

        Close();
    }

    private void UpdateButtons()
    {
        SelectFolderButton.IsEnabled = !_busy;
        ReplaceButton.IsEnabled = OptiScalerReplacePresentation.CanReplace(_lastDiscovery, _busy);
        CancelButton.Content = _busy ? "Cancel" : "Close";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Don't tear the dialog down mid-replacement; ask the core to cancel and let the operation
        // unwind on its own.
        if (_busy)
        {
            e.Cancel = true;
            _replaceCts?.Cancel();
            return;
        }

        base.OnClosing(e);
    }
}
