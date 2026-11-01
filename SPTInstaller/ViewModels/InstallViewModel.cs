using ReactiveUI;
using SPTInstaller.Controllers;
using SPTInstaller.Helpers;
using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using SPTInstaller.Installer_Tasks;

namespace SPTInstaller.ViewModels;

public class InstallViewModel : ViewModelBase
{
    private IProgressableTask _currentTask;
    
    public IProgressableTask CurrentTask
    {
        get => _currentTask;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentTask, value);
            this.RaisePropertyChanged(nameof(ShowSources));
        }
    }

    private const string Automatic = "Automatic";

    public ObservableCollection<string> Sources { get; } = new(
        new[] { Automatic }.Concat(ServiceHelper.Get<InternalData>()?.SelectedChannel?.Release?.Mirrors?
            .Select(mirror => mirror.Name).Distinct() ?? []));

    public string SelectedSource
    {
        get => DownloadSources.Preferred ?? Automatic;
        set
        {
            DownloadSources.Preferred = value == Automatic ? null : value;
            this.RaisePropertyChanged();
        }
    }

    public bool ShowSources => Sources.Count > 2 && CurrentTask is DownloadTask;

    private string _sourceSpeeds = "";

    public string SourceSpeeds
    {
        get => _sourceSpeeds;
        set => this.RaiseAndSetIfChanged(ref _sourceSpeeds, value);
    }

    private readonly DispatcherTimer _speedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    
    public ObservableCollection<InstallerTaskBase> MyTasks { get; set; } =
        new(ServiceHelper.GetAll<InstallerTaskBase>());

    public override InstallerStep Step => InstallerStep.Install;

    public string? ReleaseText { get; } = string.IsNullOrEmpty(ServiceHelper.Get<InternalData>()?.SelectedChannel?.Release?.SPTVersion)
        ? null
        : $"SPT {ServiceHelper.Get<InternalData>()!.SelectedChannel.Release.SPTVersion}";
    
    public InstallViewModel(IScreen host) : base(host)
    {
        var installer = ServiceHelper.Get<InstallController>();
        
        installer.TaskChanged += (sender, task) => CurrentTask = task;

        _speedTimer.Tick += (_, _) => SourceSpeeds = DownloadSources.TakeSummary();
        _speedTimer.Start();
        
        Task.Run(async () =>
        {
            var result = await installer.RunTasks();

            await Dispatcher.UIThread.InvokeAsync(_speedTimer.Stop);
            
            NavigateTo(new MessageViewModel(HostScreen, result));
        });
    }
}