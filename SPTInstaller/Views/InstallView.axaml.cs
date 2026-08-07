using ReactiveUI.Avalonia;
using SPTInstaller.ViewModels;

namespace SPTInstaller.Views;

public partial class InstallView : ReactiveUserControl<InstallViewModel>
{
    public InstallView()
    {
        InitializeComponent();
    }
}