using System.Reflection;
using System.Threading.Tasks;
using ReactiveUI;
using SPTInstaller.Models;

namespace SPTInstaller.ViewModels;

public class InstallerUpdateViewModel : ViewModelBase
{
    
    public InstallerUpdateInfo UpdateInfo { get; set; } = new();

    public override InstallerStep Step => InstallerStep.Start;

    private string _installPath;
    public InstallerUpdateViewModel(IScreen Host, string installPath) : base(Host)
    {
        _installPath = installPath;

        Task.Run(async () =>
        {
            await UpdateInfo.CheckForUpdates(Assembly.GetExecutingAssembly().GetName().Version);
            
            if (!UpdateInfo.UpdateAvailable)
            {
                NavigateTo(new OverviewViewModel(HostScreen, _installPath));
            }
        });
    }
    
    public void NotNowCommand()
    {
        NavigateTo(new OverviewViewModel(HostScreen, _installPath));
    }
    
    public async Task UpdateInstallCommand()
    {
        await UpdateInfo.UpdateInstaller();
    }
}