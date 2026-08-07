using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using DialogHostAvalonia;
using ReactiveUI;
using Serilog;
using SPTInstaller.Controllers;
using SPTInstaller.CustomControls;
using SPTInstaller.CustomControls.Dialogs;
using SPTInstaller.Helpers;
using SPTInstaller.Models;
using SPTInstaller.Models.ReleaseInfo;
using System.Text.Json;

namespace SPTInstaller.ViewModels;

public class PreChecksViewModel : ViewModelBase
{
    private bool _hasPreCheckSelected;
    
    public bool HasPreCheckSelected
    {
        get => _hasPreCheckSelected;
        set => this.RaiseAndSetIfChanged(ref _hasPreCheckSelected, value);
    }
    
    public ObservableCollection<PreCheckBase> PreChecks { get; set; } = new(ServiceHelper.GetAll<PreCheckBase>());
    
    public ObservableCollection<InstallChannel> Channels { get; } = new();

    private InstallChannel _selectedChannel;

    public InstallChannel SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedChannel, value);

            var data = ServiceHelper.Get<InternalData?>();

            if (data != null)
            {
                data.SelectedChannel = value;
            }

            if (value?.Release != null)
            {
                InstallButtonText = $"Start Install: v{value.Release.SPTVersion}";
            }

            // Requirements are per release, so the checks have to be evaluated again for the new one.
            var installer = ServiceHelper.Get<InstallController?>();

            if (installer != null && value != null)
            {
                Task.Run(async () =>
                {
                    var result = await installer.RunPreChecks();
                    AllowInstall = result.Succeeded;
                });
            }
        }
    }

    private ReleaseInfo _installButtonRelease;

    private bool _showChannels;

    public bool ShowChannels
    {
        get => _showChannels;
        set => this.RaiseAndSetIfChanged(ref _showChannels, value);
    }

    public ICommand SelectPreCheckCommand { get; set; }
    public ICommand StartInstallCommand { get; set; }
    
    public ICommand LaunchWithDebug { get; set; }
    
    private bool _debugging;
    
    public bool Debugging
    {
        get => _debugging;
        set => this.RaiseAndSetIfChanged(ref _debugging, value);
    }
    
    private string _installPath;
    
    public string InstallPath
    {
        get => _installPath;
        set => this.RaiseAndSetIfChanged(ref _installPath, value);
    }
    
    private string _installButtonText;
    
    public string InstallButtonText
    {
        get => _installButtonText;
        set => this.RaiseAndSetIfChanged(ref _installButtonText, value);
    }
    
    private bool _allowInstall;
    
    public bool AllowInstall
    {
        get => _allowInstall;
        set => this.RaiseAndSetIfChanged(ref _allowInstall, value);
    }
    
    private bool _allowDetailsButton = false;
    
    public bool AllowDetailsButton
    {
        get => _allowDetailsButton;
        set => this.RaiseAndSetIfChanged(ref _allowDetailsButton, value);
    }
    
    private string _cacheInfoText;
    
    public string CacheInfoText
    {
        get => _cacheInfoText;
        set => this.RaiseAndSetIfChanged(ref _cacheInfoText, value);
    }
    
    private StatusSpinner.SpinnerState _cacheCheckState;
    
    public StatusSpinner.SpinnerState CacheCheckState
    {
        get => _cacheCheckState;
        set => this.RaiseAndSetIfChanged(ref _cacheCheckState, value);
    }
    
    private StatusSpinner.SpinnerState _installButtonCheckState;
    
    public StatusSpinner.SpinnerState InstallButtonCheckState
    {
        get => _installButtonCheckState;
        set => this.RaiseAndSetIfChanged(ref _installButtonCheckState, value);
    }
    
    /// <summary>
    /// Every published release crossed with its mirrors, so a new release or mirror shows up without an
    /// installer change. The first mirror of the newest release is preselected.
    /// </summary>
    private async Task LoadChannelsAsync()
    {
        try
        {
            var releaseFile = await DownloadCacheHelper.GetOrDownloadFileAsync("release.json",
                DownloadCacheHelper.ReleaseMirrorUrl, null, DownloadCacheHelper.SuggestedTtl);

            if (releaseFile == null)
            {
                Log.Warning("Could not fetch release info for the version list, falling back to the newest release");
                return;
            }

            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(releaseFile.FullName), JsonOptions.Default);

            if (manifest?.Releases == null || manifest.Releases.Count == 0)
            {
                Log.Warning("No releases were published, falling back to the newest release");
                return;
            }

            var channels = new List<InstallChannel>();

            foreach (var release in manifest.Releases)
            {
                for (int i = 0; i < (release.Mirrors?.Count ?? 0); i++)
                {
                    channels.Add(new InstallChannel
                    {
                        Release = release,
                        MirrorIndex = i,
                        MirrorName = release.Mirrors[i].Name,
                    });
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var channel in channels)
                {
                    Channels.Add(channel);
                }

                SelectedChannel = Channels.FirstOrDefault();
                ShowChannels = Channels.Count > 1;
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not build the version list, falling back to the newest release");
        }
    }

    private void ReCheckRequested(object? sender, EventArgs e)
    {
        Task.Run(async () =>
        {
            if (sender is InstallController installer)
            {
                var result = await installer.RunPreChecks();
                AllowInstall = result.Succeeded;
            }
        });
    }
    
    public PreChecksViewModel(IScreen host) : base(host)
    {
        var data = ServiceHelper.Get<InternalData?>();
        var installer = ServiceHelper.Get<InstallController?>();
        
        Debugging = data.DebugMode;
        
        installer.RecheckRequested += ReCheckRequested;
        
        InstallButtonText = "Please wait ...";
        InstallButtonCheckState = StatusSpinner.SpinnerState.Pending;
        
        if (data == null || installer == null)
        {
            NavigateTo(new MessageViewModel(HostScreen,
                Result.FromError("Failed to get required service for prechecks")));
            return;
        }
        
        InstallPath = data.TargetInstallPath;
        
        Log.Information($"Install Path: {FileHelper.GetRedactedPath(InstallPath)}");

        // Fetched here so the choice exists before any task runs. ReleaseCheckTask reads the same
        // cached file, so this costs one request rather than two.
        Task.Run(LoadChannelsAsync);
        
        if (data.OriginalGamePath == data.TargetInstallPath)
        {
            Log.CloseAndFlush();
            
            var logFiles = Directory.GetFiles(InstallPath, "spt-installer_*.log");
            
            // remove log file from original game path if they exist
            foreach (var file in logFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
            
            NavigateTo(new MessageViewModel(HostScreen,
                Result.FromError(
                    "You have chosen to install in the same folder as the game. Please choose another folder. Refer to the install guide on where best to place the installer before running it."),
                noLog: true));
            return;
        }
        
        Task.Run(async () =>
        {
            if (FileHelper.CheckPathForProblemLocations(InstallPath, out var failedCheck))
            {
                switch (failedCheck.CheckAction)
                {
                    case PathCheckAction.Warn:
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            Log.Warning("Problem path detected, confirming install path ...");
                            var confirmation = await DialogHost.Show(new ConfirmationDialog(
                                $"It appears you are installing into a folder known to cause problems: {failedCheck.Target}." +
                                $"\nPlease consider installing somewhere else to avoid issues later on." +
                                $"\n\nAre you sure you want to install to this path?\n{InstallPath}"));
                            
                            if (confirmation == null || !bool.TryParse(confirmation.ToString(), out var confirm) ||
                                !confirm)
                            {
                                Log.Information("User declined install path");
                                NavigateBack();
                            }
                        });
                        
                        break;
                    }
                    
                    case PathCheckAction.Deny:
                    {
                        Log.Error("Problem path detected, install denied");
                        NavigateTo(new MessageViewModel(HostScreen,
                            Result.FromError(
                                $"We suspect you may be installing into a problematic folder: {failedCheck.Target}.\nWe won't be letting you install here. How did you do this?")));
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                
                Log.Information("User accepted install path");
            }
        });
        
        LaunchWithDebug = ReactiveCommand.Create(async () =>
        {
            try
            {
                App.ReLaunch(true, InstallPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to enter debug mode");
            }
        });
        
        SelectPreCheckCommand = ReactiveCommand.Create(async (PreCheckBase check) =>
        {
            foreach (var precheck in PreChecks)
            {
                if (check.Id == precheck.Id)
                {
                    precheck.IsSelected = true;
                    
                    HasPreCheckSelected = true;
                    
                    continue;
                }
                
                precheck.IsSelected = false;
            }
        });
        
        StartInstallCommand = ReactiveCommand.Create(async () =>
        {
            NavigateTo(new InstallViewModel(HostScreen));
        });
        
        Task.Run(async () =>
        {
            // run prechecks
            var result = await installer.RunPreChecks();
            
            // get latest spt version
            InstallButtonText = "Getting latest release ...";
            InstallButtonCheckState = StatusSpinner.SpinnerState.Running;
            
            var progress = new Progress<double>((d) => { });

            ReleaseInfo? sptReleaseInfo = null;
            var retries = 1;

            while (retries >= 0)
            {
                retries--;
                
                try
                {
                    var sptReleaseInfoFile =
                        await DownloadCacheHelper.GetOrDownloadFileAsync("release.json", DownloadCacheHelper.ReleaseMirrorUrl,
                            progress, DownloadCacheHelper.SuggestedTtl);
            
                    if (sptReleaseInfoFile == null)
                    {
                        InstallButtonText = "Could not get release metadata";
                        InstallButtonCheckState = StatusSpinner.SpinnerState.Error;
                        return;
                    }
                    
                    var manifest =
                        JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(sptReleaseInfoFile.FullName), JsonOptions.Default);

                    // The button names whatever the user picked, falling back to the newest published.
                    sptReleaseInfo = SelectedChannel?.Release ?? manifest?.Releases?.FirstOrDefault();
                }
                catch (Exception)
                {
                    DownloadCacheHelper.ClearMetadataCache();
                }
            }

            if (sptReleaseInfo == null)
            {
                InstallButtonText = "Could not parse latest release";
                InstallButtonCheckState = StatusSpinner.SpinnerState.Error;
                return;
            }
            
            InstallButtonText = $"Start Install: v{sptReleaseInfo.SPTVersion}";
            _installButtonRelease = sptReleaseInfo;
            InstallButtonCheckState = StatusSpinner.SpinnerState.OK;
            
            AllowDetailsButton = true;
            AllowInstall = result.Succeeded;
        });
        
        Task.Run(() =>
        {
            CacheInfoText = "Getting cache size ...";
            CacheCheckState = StatusSpinner.SpinnerState.Running;
            
            CacheInfoText = $"Cache Size: {DownloadCacheHelper.GetCacheSizeText()}";
            CacheCheckState = StatusSpinner.SpinnerState.OK;
        });
    }
}