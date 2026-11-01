using Avalonia.Threading;
using ReactiveUI;

namespace SPTInstaller.ViewModels;

public class ViewModelBase : ReactiveObject, IActivatableViewModel, IRoutableViewModel
{
    public ViewModelActivator Activator { get; } = new();
    
    public string? UrlPathSegment => Guid.NewGuid().ToString().Substring(0, 7);
    
    public IScreen HostScreen { get; }

    /// <summary>
    /// Where this screen sits in the title bar's steps
    /// </summary>
    public virtual Models.InstallerStep Step => Models.InstallerStep.Start;

    public virtual bool StepFailed => false;
    
    /// <summary>
    /// Navigate to another viewmodel
    /// </summary>
    /// <param name="ViewModel"></param>
    public void NavigateTo(ViewModelBase ViewModel)
    {
        Dispatcher.UIThread.InvokeAsync(() => { HostScreen.Router.Navigate.Execute(ViewModel).Subscribe(); });
    }
    
    public void NavigateBack()
    {
        Dispatcher.UIThread.InvokeAsync(() => { HostScreen.Router.NavigateBack.Execute().Subscribe(); });
    }
    
    public ViewModelBase(IScreen Host)
    {
        HostScreen = Host;
    }
}