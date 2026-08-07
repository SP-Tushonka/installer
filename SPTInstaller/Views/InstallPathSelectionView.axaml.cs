using Avalonia.Controls;
using ReactiveUI.Avalonia;
using SPTInstaller.ViewModels;

namespace SPTInstaller.Views;

public partial class InstallPathSelectionView : ReactiveUserControl<InstallPathSelectionViewModel>
{
    public InstallPathSelectionView()
    {
        InitializeComponent();
    }
    
    private void TextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        ViewModel?.ValidatePath();
    }
}