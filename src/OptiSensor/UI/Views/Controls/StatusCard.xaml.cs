using System.Windows;
namespace OptiSensor.UI.Views.Controls;

public partial class StatusCard : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(StatusCard),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(string),
        typeof(StatusCard),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail),
        typeof(string),
        typeof(StatusCard),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public StatusCard()
    {
        InitializeComponent();
        UpdateText();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is StatusCard statusCard)
            statusCard.UpdateText();
    }

    private void UpdateText()
    {
        if (!IsInitialized)
            return;

        TitleTextBlock.Text = Title;
        ValueTextBlock.Text = Value;
        DetailTextBlock.Text = Detail;
    }
}
