namespace OptiSensor.UI.Views.Pages;

public partial class DashboardPage : System.Windows.Controls.UserControl
{
    public DashboardPage()
    {
        InitializeComponent();
    }

    public void UpdateStatus(
        string status,
        string lastOverlay,
        string publishDetail,
        string settingsState,
        string optiScalerStatus)
    {
        StatusCard.Value = status;
        StatusCard.Detail = "OptiSensor tray helper";
        OverlayCard.Value = lastOverlay;
        OverlayCard.Detail = "Text currently published to the external overlay";
        PublishCard.Value = settingsState;
        PublishCard.Detail = publishDetail;
        OptiScalerCard.Value = optiScalerStatus;
        OptiScalerCard.Detail = "Target: Local\\OptiScalerExternalOverlay";
    }
}
