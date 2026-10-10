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
                _installButtonRelease = value.Release;
            }

            // Requirements are per release, so a version picked by the user reruns the checks. The first pick is
            // made before the screen's own run, which already covers it.
            var installer = ServiceHelper.Get<InstallController?>();

            if (installer != null && value != null && _channelsLoaded)
            {
                Task.Run(async () =>
                {
                    var result = await installer.RunPreChecks();
                    SelectFirstFailedCheck();
                    UpdateInstallButton(result.Succeeded);
                });
            }
        }
    }

    private ReleaseInfo _installButtonRelease;

    private bool _channelsLoaded;

    private bool _showChannels;

    public bool ShowChannels
    {
        get => _showChannels;
        set => this.RaiseAndSetIfChanged(ref _showChannels, value);
    }

    public ICommand SelectPreCheckCommand { get; set; }
    public ICommand StartInstallCommand { get; set; }
    
    public ICommand LaunchWithDebug { get; set; }

    public ICommand ChangeInstallPathCommand { get; set; }
    
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
        // A damaged cached file is cleared and fetched once more before giving up
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var releaseFile = await DownloadCacheHelper.GetOrDownloadFileAsync("release.json",
                    DownloadCacheHelper.ReleaseUrls, null, DownloadCacheHelper.SuggestedTtl);

                if (releaseFile == null)
                {
                    Log.Warning("Could not fetch release info for the version list");
                    return;
                }

                var manifest = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(releaseFile.FullName), JsonOptions.Default);

                if (manifest?.Releases == null || manifest.Releases.Count == 0)
                {
                    Log.Warning("No releases were published");
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

                channels = await HostReachability.KeepReachableAsync(channels, channel => channel.Release.Mirrors[channel.MirrorIndex].DownloadUrl);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var channel in channels)
                    {
                        Channels.Add(channel);
                    }

                    SelectedChannel = Channels.FirstOrDefault();
                    ShowChannels = Channels.Count > 1;
                });

                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not build the version list");
                DownloadCacheHelper.ClearMetadataCache();
            }
        }
    }

    private void ReCheckRequested(object? sender, EventArgs e)
    {
        Task.Run(async () =>
        {
            if (sender is InstallController installer)
            {
                var result = await installer.RunPreChecks();
                SelectFirstFailedCheck();
                UpdateInstallButton(result.Succeeded);
            }
        });
    }

    // Shows why install is blocked, or what deserves attention, without making the user hunt for it
    private void SelectFirstFailedCheck()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (PreChecks.Any(check => check.IsSelected))
            {
                return;
            }

            var failed = PreChecks.FirstOrDefault(check => check.State == StatusSpinner.SpinnerState.Error)
                         ?? PreChecks.FirstOrDefault(check => check.State == StatusSpinner.SpinnerState.Warning);

            if (failed != null)
            {
                SelectPreCheckCommand?.Execute(failed);
            }
        });
    }

    private void UpdateInstallButton(bool checksPassed)
    {
        AllowInstall = checksPassed;

        if (_installButtonRelease == null)
        {
            return;
        }

        InstallButtonText = checksPassed
            ? $"Start Install: v{_installButtonRelease.SPTVersion}"
            : "Fix the failed checks to install";
        InstallButtonCheckState = checksPassed ? StatusSpinner.SpinnerState.OK : StatusSpinner.SpinnerState.Error;
    }
    
    public PreChecksViewModel(IScreen host) : base(host)
    {
        var data = ServiceHelper.Get<InternalData?>();
        var installer = ServiceHelper.Get<InstallController?>();
        
        Debugging = data.DebugMode;

        // The checks are shared, so a selection made before changing the install path would otherwise carry over
        foreach (var precheck in PreChecks)
        {
            precheck.IsSelected = false;
        }

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

        ChangeInstallPathCommand = ReactiveCommand.Create(() =>
        {
            installer.RecheckRequested -= ReCheckRequested;
            NavigateTo(new InstallPathSelectionViewModel(HostScreen, InstallPath, autoAdvance: false));
        });
        
        Task.Run(async () =>
        {
            InstallButtonText = "Getting latest release ...";
            InstallButtonCheckState = StatusSpinner.SpinnerState.Running;

            // The selected release decides which runtimes the checks look for, so the version list loads first.
            // ReleaseCheckTask reads the same cached file.
            await LoadChannelsAsync();

            var result = await installer.RunPreChecks();
            _channelsLoaded = true;
            SelectFirstFailedCheck();

            if (_installButtonRelease == null)
            {
                InstallButtonText = "Could not get release metadata";
                InstallButtonCheckState = StatusSpinner.SpinnerState.Error;
                return;
            }

            UpdateInstallButton(result.Succeeded);
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