using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using ReactiveUI;
using Serilog;
using SPTInstaller.Helpers;
using SPTInstaller.Interfaces;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using SPTInstaller.Models;

namespace SPTInstaller.ViewModels;

public class MessageViewModel : ViewModelBase
{
    private bool _HasErrors;
    
    public bool HasErrors
    {
        get => _HasErrors;
        set => this.RaiseAndSetIfChanged(ref _HasErrors, value);
    }
    
    private string _Message;
    
    public string Message
    {
        get => _Message;
        set => this.RaiseAndSetIfChanged(ref _Message, value);
    }
    
    private bool _showCloseButton;
    
    public bool ShowCloseButton
    {
        get => _showCloseButton;
        set => this.RaiseAndSetIfChanged(ref _showCloseButton, value);
    }
    
    private bool _showOptions;
    
    public bool ShowOptions
    {
        get => _showOptions;
        set => this.RaiseAndSetIfChanged(ref _showOptions, value);
    }
    
    public override InstallerStep Step => InstallerStep.Install;

    public override bool StepFailed => HasErrors;

    public string? InstallPath { get; } = ServiceHelper.Get<InternalData>()?.TargetInstallPath;

    public string? ReleaseText { get; } = string.IsNullOrEmpty(ServiceHelper.Get<InternalData>()?.ReleaseInfo?.SPTVersion)
        ? null
        : $"SPT {ServiceHelper.Get<InternalData>()!.ReleaseInfo.SPTVersion}";

    // Those downloads are already deleted after installing, so there is no cache to offer clearing
    public bool DownloadedToInstallFolder { get; } = DownloadCacheHelper.DownloadsInInstallFolder;

    public string HelpText { get; } = ServiceHelper.Get<InternalData>()?.SelectedChannel?.IsLegacy == true
        ? "This is a legacy release, so the SPT support channel can't help with it. Your log may still show what went wrong."
        : "Copy your log and share it in the SPT support channel.";

    public string SuccessTitle => ReleaseText == null ? "SPT is installed" : $"{ReleaseText} is installed";

    public ICommand ClearCacheCommand { get; }

    private string? _retryText;

    public string? RetryText
    {
        get => _retryText;
        set => this.RaiseAndSetIfChanged(ref _retryText, value);
    }

    public ICommand RetryCommand { get; set; } = ReactiveCommand.Create(() =>
    {
        var data = ServiceHelper.Get<InternalData>();
        App.ReLaunch(data.DebugMode, data.TargetInstallPath ?? "");
    });

    private string _cacheInfoText;

    public string CacheInfoText
    {
        get => _cacheInfoText;
        set => this.RaiseAndSetIfChanged(ref _cacheInfoText, value);
    }

    private bool _showCache;

    /// <summary>
    ///     Whether to show the cache line. Downloads in the install folder are deleted, so those hide it like an empty cache.
    /// </summary>
    public bool ShowCache
    {
        get => _showCache;
        set => this.RaiseAndSetIfChanged(ref _showCache, value);
    }

    private void RefreshCache()
    {
        var size = DownloadCacheHelper.GetCacheSize();
        CacheInfoText = DownloadCacheHelper.CacheSizeText(size);
        ShowCache = !DownloadedToInstallFolder && size != 0;
    }
    
    private string _clipCommandText;
    
    public string ClipCommandText
    {
        get => _clipCommandText;
        set => this.RaiseAndSetIfChanged(ref _clipCommandText, value);
    }
    
    private bool _addShortcuts;
    public bool AddShortcuts
    {
        get => _addShortcuts;
        set => this.RaiseAndSetIfChanged(ref _addShortcuts, value);
    }
    
    private bool _openInstallFolder = true;
    public bool OpenInstallFolder
    {
        get => _openInstallFolder;
        set => this.RaiseAndSetIfChanged(ref _openInstallFolder, value);
    }
    
    public ICommand CopyLogFileToClipboard => ReactiveCommand.CreateFromTask(async () =>
    {
        var data = ServiceHelper.Get<InternalData>();
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                if (desktop.MainWindow?.Clipboard == null)
                {
                    ClipCommandText = "Could not get clipboard :(";
                    return;
                }
                
                var filesToCopy = new List<IStorageFile>();
                
                var logFile = await desktop.MainWindow.StorageProvider.TryGetFileFromPathAsync(data.DebugMode ? App.LogDebugPath : App.LogPath);
                    
                if (logFile == null)
                {
                    ClipCommandText = "Could not get log file :(";
                    return;
                }
                
                filesToCopy.Add(logFile);

                await desktop.MainWindow.Clipboard.SetFilesAsync(filesToCopy);
                ClipCommandText = "Copied!";
            }
            catch (Exception ex)
            {
                ClipCommandText = ex.Message;
            }
        } 
    });
    
    public ICommand CloseCommand { get; set; } = ReactiveCommand.Create(() =>
    {
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopApp)
        {
            desktopApp.MainWindow.Close();
        }
    });
    
    public MessageViewModel(IScreen Host, IResult result, bool showCloseButton = true, bool noLog = false) : base(Host)
    {
        ShowCloseButton = showCloseButton;
        Message = result.Message;
        ClipCommandText = "Copy log";
        
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopApp)
        {
            var data = ServiceHelper.Get<InternalData?>();
            
            desktopApp.MainWindow.Closing += (_, _) =>
            {
                if (ShowOptions)
                {
                    if (OpenInstallFolder)
                    {
                        Process.Start(new ProcessStartInfo()
                        {
                           FileName = data.TargetInstallPath,
                           UseShellExecute = true
                        });
                    }
                    
                    if (AddShortcuts)
                    {
                        var sptPath = $"{Path.Join(data.TargetInstallPath, data.ReleaseInfo?.RuntimeFolderName ?? "SPT_Runtime")}";
                        ShortcutHelper.CreateSptShortcuts(ShortcutHelper.DesktopPath, sptPath);
                    }
                }
            };
        }
        
        Task.Run(RefreshCache);

        ClearCacheCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var confirmation = await DialogHostAvalonia.DialogHost.Show(new CustomControls.Dialogs.ConfirmationDialog(
                $"Delete the {DownloadCacheHelper.GetCacheSizeText()} download cache?",
                "A reinstall then downloads the patches and SPT again.", "Delete cache"));

            if (!true.Equals(confirmation))
            {
                return;
            }

            DownloadCacheHelper.ClearCache();
            RefreshCache();
        });
        
        if (result.Succeeded)
        {
            Log.Information(Message);
            ShowOptions = true;
            return;
        }
        
        HasErrors = true;
        RetryText = (result as Result)?.RetryText;

        if (!noLog)
            Log.Error(Message);
    }
}