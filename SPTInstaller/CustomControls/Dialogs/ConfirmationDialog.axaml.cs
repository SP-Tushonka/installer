using Avalonia;
using Avalonia.Controls;

namespace SPTInstaller.CustomControls.Dialogs;

/// <summary>
/// Closes the dialog host with true for confirm and false for cancel
/// </summary>
public partial class ConfirmationDialog : UserControl
{
    public ConfirmationDialog(string message, string? detail = null, string confirmText = "Yes", string cancelText = "No")
    {
        Message = message;
        Detail = detail;
        ConfirmText = confirmText;
        CancelText = cancelText;

        InitializeComponent();
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<ConfirmationDialog, string>(nameof(Message));

    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<ConfirmationDialog, string?>(nameof(Detail));

    public string ConfirmText
    {
        get => GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public static readonly StyledProperty<string> ConfirmTextProperty =
        AvaloniaProperty.Register<ConfirmationDialog, string>(nameof(ConfirmText));

    public string CancelText
    {
        get => GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public static readonly StyledProperty<string> CancelTextProperty =
        AvaloniaProperty.Register<ConfirmationDialog, string>(nameof(CancelText));
}
