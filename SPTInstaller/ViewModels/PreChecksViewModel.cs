using System.Collections.Generic;
using System.Linq;
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
using SPTInstaller.Installer_Tasks.PreChecks;
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
    
    public ObservableCollection<PreCheckBase> PreChecks { get; } =
        new(ServiceHelper.Get<InstallController>()!.PreChecks.Cast<PreCheckBase>());
    
    private readonly List<InstallChannel> _channels = [];

    public ObservableCollection<ReleaseInfo> Versions { get; } = new();

    public ObservableCollection<DownloadServer> Servers { get; } = new();

    private ReleaseInfo? _selectedVersion;

    public ReleaseInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            // The wheel and arrow keys can reach an entry the list has disabled.
            // The list only follows a value that changed, so the pick is taken and then undone
            if (value != null && !value.Selectable)
            {
                var kept = _selectedVersion;
                this.RaiseAndSetIfChanged(ref _selectedVersion, value);

                Dispatcher.UIThread.Post(() =>
                {
                    _selectedVersion = kept;
                    this.RaisePropertyChanged(nameof(SelectedVersion));
                });

                return;
            }

            var previous = _selectedVersion;
            this.RaiseAndSetIfChanged(ref _selectedVersion, value);

            if (value == null)
            {
                return;
            }

            if (value.LinuxIncompatible)
            {
                Log.Warning("SPT {version} is not Linux compatible, it was picked in debug mode", value.SPTVersion);
            }

            if ((value.Legacy || value.LinuxIncompatible) && previous != null && !_revertingVersion && !_acceptedVersions.Contains(value))
            {
                Dispatcher.UIThread.Post(() => ConfirmVersionAsync(value, previous));
            }

            Servers.Clear();

            var channels = _channels.Where(channel => channel.Release == value).ToList();

            if (channels.Count > 1)
            {
                Servers.Add(new DownloadServer { Name = "Automatic" });
            }

            foreach (var channel in channels)
            {
                Servers.Add(new DownloadServer { Name = channel.MirrorName, Channel = channel });
            }

            // The choice carries over to other versions
            SelectedServer = Servers.FirstOrDefault(server => server.Channel != null && server.Name == DownloadSources.Preferred)
                             ?? Servers.FirstOrDefault();
        }
    }

    private DownloadServer? _selectedServer;

    public DownloadServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedServer, value);

            if (value == null)
            {
                return;
            }

            if (Servers.Count > 1)
            {
                DownloadSources.Preferred = value.Channel?.MirrorName;
            }

            SelectedChannel = value.Channel ?? Servers.First(server => server.Channel != null).Channel;
        }
    }

    private readonly HashSet<ReleaseInfo> _acceptedVersions = [];

    // Going back can land on another release that warns, which must not ask again or the two prompts loop
    private bool _revertingVersion;

    private async void ConfirmVersionAsync(ReleaseInfo release, ReleaseInfo previous)
    {
        const string legacyText = "No support is given for legacy releases in the official SPT Discord.";

        var confirmation = await DialogHost.Show(new ConfirmationDialog(
            release.LinuxIncompatible ? $"SPT {release.SPTVersion} is not Linux compatible" : $"SPT {release.SPTVersion} is a legacy release",
            release.LinuxIncompatible
                ? "It is missing some or all native Linux binaries, so the install may not be usable."
                  + (release.Legacy ? $"\n\n{legacyText}" : "")
                : legacyText,
            $"Use {release.SPTVersion}", "Go back"));

        if (true.Equals(confirmation))
        {
            _acceptedVersions.Add(release);

            if (release.Legacy)
            {
                Log.Warning("LEGACY RELEASE {version} picked by the user, it is unsupported", release.SPTVersion);
            }

            return;
        }

        if (SelectedVersion == release)
        {
            _revertingVersion = true;
            SelectedVersion = previous;
            _revertingVersion = false;
        }
    }

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
                ApplyRuntimeChecks(value.Release);
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

    private void ApplyRuntimeChecks(ReleaseInfo release)
    {
        var installer = ServiceHelper.Get<InstallController?>();
        var requirements = release.RequiredRuntimes.Count > 0 ? release.RequiredRuntimes : [.. RuntimePreCheck.Defaults];

        if (installer == null || !installer.SetRuntimeRequirements(requirements))
        {
            return;
        }

        PreChecks.Clear();

        foreach (var check in installer.PreChecks.Cast<PreCheckBase>())
        {
            PreChecks.Add(check);
        }

        HasPreCheckSelected = PreChecks.Any(check => check.IsSelected);
    }

    private bool _channelsLoaded;

    public override InstallerStep Step => InstallerStep.Checks;

    private string _headerTitle = "Running checks";

    public string HeaderTitle
    {
        get => _headerTitle;
        set => this.RaiseAndSetIfChanged(ref _headerTitle, value);
    }

    private string? _releaseText;

    public string? ReleaseText
    {
        get => _releaseText;
        set => this.RaiseAndSetIfChanged(ref _releaseText, value);
    }

    public ICommand OpenCacheFolderCommand { get; } = ReactiveCommand.Create(() =>
    {
        Directory.CreateDirectory(DownloadCacheHelper.CachePath);
        Process.Start(new ProcessStartInfo { FileName = DownloadCacheHelper.CachePath + Path.DirectorySeparatorChar, UseShellExecute = true });
    });

    public ICommand ShowCacheInfoCommand { get; } = ReactiveCommand.CreateFromTask(async () =>
    {
        await DialogHost.Show(new WhyCacheThoughDialog());
    });

    public ICommand ClearCacheCommand { get; set; }

    // Only useful when the install folder is on another drive than the cache
    public bool CanDownloadToInstallFolder => !string.IsNullOrEmpty(InstallPath)
        && !string.Equals(FileHelper.DriveRoot(InstallPath), FileHelper.DriveRoot(DownloadCacheHelper.CachePath),
            StringComparison.OrdinalIgnoreCase);

    public bool DownloadToInstallFolder
    {
        get => ServiceHelper.Get<InternalData>()!.DownloadToInstallFolder;
        set
        {
            ServiceHelper.Get<InternalData>()!.DownloadToInstallFolder = value;
            this.RaisePropertyChanged();
            ApplyDownloadPath();

            var installer = ServiceHelper.Get<InstallController?>();

            if (installer != null && _channelsLoaded)
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

    private void ApplyDownloadPath()
    {
        DownloadCacheHelper.DownloadPath = DownloadToInstallFolder && CanDownloadToInstallFolder
            ? DownloadCacheHelper.InstallFolderDownloads(InstallPath)
            : DownloadCacheHelper.CachePath;
    }

    private void RefreshCacheSize()
    {
        var size = DownloadCacheHelper.GetCacheSize();
        CacheInfoText = DownloadCacheHelper.CacheSizeText(size);
        HasCache = size != 0;
    }

    private bool _hasCache;

    public bool HasCache
    {
        get => _hasCache;
        set => this.RaiseAndSetIfChanged(ref _hasCache, value);
    }

    private bool _showVersions;

    public bool ShowVersions
    {
        get => _showVersions;
        set => this.RaiseAndSetIfChanged(ref _showVersions, value);
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
        set
        {
            this.RaiseAndSetIfChanged(ref _installPath, value);
            this.RaisePropertyChanged(nameof(InstallPathShort));
        }
    }

    // The drive and the last folders say where it is, the middle is what gets long
    public string InstallPathShort
    {
        get
        {
            const int maxLength = 34;

            if (string.IsNullOrEmpty(InstallPath) || InstallPath.Length <= maxLength)
            {
                return InstallPath;
            }

            var root = Path.GetPathRoot(InstallPath) ?? "";
            var parts = InstallPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            for (var keep = Math.Min(2, parts.Length - 1); keep >= 1; keep--)
            {
                var shortened = Path.Join(root, "…", Path.Join(parts[^keep..]));

                if (shortened.Length <= maxLength || keep == 1)
                {
                    return shortened;
                }
            }

            return InstallPath;
        }
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
    
    private StatusSpinner.SpinnerState _installButtonCheckState;
    
    public StatusSpinner.SpinnerState InstallButtonCheckState
    {
        get => _installButtonCheckState;
        set
        {
            this.RaiseAndSetIfChanged(ref _installButtonCheckState, value);
            this.RaisePropertyChanged(nameof(ShowInstallSpinner));
        }
    }

    public bool ShowInstallSpinner => InstallButtonCheckState != StatusSpinner.SpinnerState.OK;
    
    /// <summary>
    /// Every published release crossed with its mirrors, so a new release or mirror shows up without an
    /// installer change. The newest release is preselected.
    /// </summary>
    private async Task LoadChannelsAsync()
    {
        // A list that does not parse, for example one caught mid-upload, is fetched once more before giving up
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var releaseJson = await DownloadCacheHelper.GetMetadataAsync("release.json", DownloadCacheHelper.ReleaseUrls);

                if (releaseJson == null)
                {
                    Log.Warning("Could not fetch release info for the version list");
                    return;
                }

                var manifest = JsonSerializer.Deserialize<ReleaseManifest>(releaseJson, JsonOptions.Default);

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
                    _channels.AddRange(channels);

                    foreach (var release in channels.Select(channel => channel.Release).Distinct())
                    {
                        Versions.Add(release);
                    }

                    ShowVersions = Versions.Count > 1;

                    // Coming back from Change keeps the version picked before, the list itself is fetched fresh
                    var previous = ServiceHelper.Get<InternalData>()?.SelectedChannel?.Release?.SPTVersion;
                    SelectedVersion = Versions.FirstOrDefault(release => release.SPTVersion == previous && release.Selectable)
                                      ?? Versions.FirstOrDefault(release => !release.LinuxIncompatible)
                                      ?? Versions.FirstOrDefault(release => release.Selectable);
                });

                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not build the version list");
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

        ReleaseText = $"SPT {_installButtonRelease.SPTVersion}";
        HeaderTitle = checksPassed ? "Ready to install" : "Fix the failed checks";
        InstallButtonText = $"Install SPT {_installButtonRelease.SPTVersion}";
        InstallButtonCheckState = StatusSpinner.SpinnerState.OK;
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
        ApplyDownloadPath();
        
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
        
        StartInstallCommand = ReactiveCommand.Create(() =>
        {
            NavigateTo(new InstallViewModel(HostScreen));
        });

        ChangeInstallPathCommand = ReactiveCommand.Create(() =>
        {
            installer.RecheckRequested -= ReCheckRequested;
            NavigateTo(new InstallPathSelectionViewModel(HostScreen, InstallPath, autoAdvance: false));
        });

        ClearCacheCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var confirmation = await DialogHost.Show(new ConfirmationDialog(
                $"Delete the {DownloadCacheHelper.GetCacheSizeText()} download cache?",
                "Patches and the SPT download are fetched again on the next install.", "Delete cache"));

            if (!true.Equals(confirmation))
            {
                return;
            }

            DownloadCacheHelper.ClearCache();
            RefreshCacheSize();

            // The free space check counts what still has to be downloaded, which just changed
            var result = await installer.RunPreChecks();
            UpdateInstallButton(result.Succeeded);
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
                HeaderTitle = "Can't install";
                InstallButtonText = Versions.Count > 0 ? "No Linux compatible release" : "Could not get release metadata";
                InstallButtonCheckState = StatusSpinner.SpinnerState.Error;
                return;
            }

            UpdateInstallButton(result.Succeeded);
        });
        
        CacheInfoText = "Checking size ...";
        Task.Run(RefreshCacheSize);
    }
}