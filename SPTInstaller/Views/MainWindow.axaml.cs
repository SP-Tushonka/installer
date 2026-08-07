using ReactiveUI.Avalonia;
using SPTInstaller.ViewModels;

namespace SPTInstaller.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
    }
}