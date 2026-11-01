using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SPTInstaller.CustomControls;

public partial class PageHeader : UserControl
{
    public PageHeader()
    {
        InitializeComponent();
    }

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<PageHeader, Geometry?>(nameof(Icon));

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly StyledProperty<IBrush> IconBrushProperty =
        AvaloniaProperty.Register<PageHeader, IBrush>(nameof(IconBrush), Brushes.White);

    public IBrush IconBrush
    {
        get => GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PageHeader, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly StyledProperty<string?> BadgeTextProperty =
        AvaloniaProperty.Register<PageHeader, string?>(nameof(BadgeText));

    public string? BadgeText
    {
        get => GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public static readonly StyledProperty<bool> IsDangerProperty =
        AvaloniaProperty.Register<PageHeader, bool>(nameof(IsDanger));

    public bool IsDanger
    {
        get => GetValue(IsDangerProperty);
        set => SetValue(IsDangerProperty, value);
    }

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<PageHeader, string?>(nameof(Subtitle));

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<PageHeader, object?>(nameof(Actions));

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
