using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace SPTInstaller.CustomControls;

public partial class InfoCard : UserControl
{
    public InfoCard()
    {
        InitializeComponent();
    }

    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<InfoCard, string>(nameof(Kind), "info");

    /// <summary>info, warning or danger</summary>
    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<InfoCard, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }


    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<InfoCard, string?>(nameof(Text));

    /// <summary>Each non-empty line becomes its own arrow point</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<string>> LinesProperty =
        AvaloniaProperty.Register<InfoCard, IEnumerable<string>>(nameof(Lines), []);

    public IEnumerable<string> Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KindProperty)
        {
            Classes.Set("warning", Kind == "warning");
            Classes.Set("danger", Kind == "danger");
        }
        else if (change.Property == TextProperty)
        {
            // Messages written for plain text sometimes carry their own "* " bullets, the card draws arrows instead
            Lines = (Text ?? "").Split('\n').Select(line => line.Trim().TrimStart('*', '•', '◉').Trim())
                .Where(line => line.Length > 0).ToList();
        }
    }
}
