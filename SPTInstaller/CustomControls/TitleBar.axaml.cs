using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System.Windows.Input;
using SPTInstaller.Models;

namespace SPTInstaller.CustomControls;

public partial class TitleBar : UserControl
{
    public TitleBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }

    public static readonly StyledProperty<IReadOnlyList<StepItem>> StepsProperty =
        AvaloniaProperty.Register<TitleBar, IReadOnlyList<StepItem>>(nameof(Steps), []);

    public IReadOnlyList<StepItem> Steps
    {
        get => GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public static readonly StyledProperty<string> VersionProperty =
        AvaloniaProperty.Register<TitleBar, string>(nameof(Version), "");

    public string Version
    {
        get => GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public static readonly StyledProperty<ICommand> XButtonCommandProperty =
        AvaloniaProperty.Register<TitleBar, ICommand>(nameof(XButtonCommand));

    public ICommand XButtonCommand
    {
        get => GetValue(XButtonCommandProperty);
        set => SetValue(XButtonCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand> MinButtonCommandProperty =
        AvaloniaProperty.Register<TitleBar, ICommand>(nameof(MinButtonCommand));

    public ICommand MinButtonCommand
    {
        get => GetValue(MinButtonCommandProperty);
        set => SetValue(MinButtonCommandProperty, value);
    }
}
