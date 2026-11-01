using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using SPTInstaller.Models;

namespace SPTInstaller.CustomControls;

public partial class PreCheckDetails : UserControl
{
    static PreCheckDetails()
    {
        IsVisibleProperty.Changed.AddClassHandler<Border>((border, _) =>
        {
            if (border.IsVisible && border.Classes.Contains("precheck-details"))
            {
                // Layout hasn't run yet when it becomes visible, so bring it into view once it has a size
                Avalonia.Threading.Dispatcher.UIThread.Post(() => border.BringIntoView(),
                    Avalonia.Threading.DispatcherPriority.Background);
            }
        });
    }

    public PreCheckDetails()
    {
        InitializeComponent();
    }
    
    public static readonly StyledProperty<ObservableCollection<PreCheckBase>> PreChecksProperty =
        AvaloniaProperty.Register<PreCheckDetails, ObservableCollection<PreCheckBase>>(nameof(PreChecks));
    
    public ObservableCollection<PreCheckBase> PreChecks
    {
        get => GetValue(PreChecksProperty);
        set => SetValue(PreChecksProperty, value);
    }
    
    public static readonly StyledProperty<bool> HasSelectionProperty =
        AvaloniaProperty.Register<PreCheckDetails, bool>(nameof(HasSelection));
    
    public bool HasSelection
    {
        get => GetValue(HasSelectionProperty);
        set => SetValue(HasSelectionProperty, value);
    }
}