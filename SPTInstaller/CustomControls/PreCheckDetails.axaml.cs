using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SPTInstaller.Models;

namespace SPTInstaller.CustomControls;

public partial class PreCheckDetails : UserControl
{
    static PreCheckDetails()
    {
        IsVisibleProperty.Changed.AddClassHandler<Grid>((grid, _) =>
        {
            if (grid.IsVisible && grid.Classes.Contains("precheck-details"))
            {
                grid.FindDescendantOfType<ScrollViewer>()?.ScrollToHome();
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