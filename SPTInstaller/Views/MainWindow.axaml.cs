using System.Linq;
using Avalonia.Controls;
using DialogHostAvalonia;
using ReactiveUI.Avalonia;
using SPTInstaller.CustomControls.Dialogs;
using SPTInstaller.ViewModels;

namespace SPTInstaller.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    private bool _closeConfirmed;
    private bool _askingToClose;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Opened += (_, _) => Activate();
    }
    
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || ViewModel?.Router.NavigationStack.LastOrDefault() is not InstallViewModel)
        {
            return;
        }

        e.Cancel = true;

        if (_askingToClose)
        {
            return;
        }

        _askingToClose = true;

        var confirmation = await DialogHost.Show(new ConfirmationDialog(
            "Stop installing SPT?",
            "The install folder is left unfinished. Downloaded files are kept, so the next try won't fetch them again.",
            "Stop and close", "Keep installing"));

        _askingToClose = false;

        if (true.Equals(confirmation))
        {
            Serilog.Log.Warning("Install stopped by closing the window");
            _closeConfirmed = true;
            Close();
        }
    }
}
