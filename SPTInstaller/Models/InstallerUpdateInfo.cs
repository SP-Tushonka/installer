using ReactiveUI;
using Serilog;
using SPTInstaller.Helpers;
using System.Threading.Tasks;
using System.Text.Json;

namespace SPTInstaller.Models;

public class InstallerUpdateInfo : ReactiveObject
{
    private Version? _newVersion;
    
    public Version? NewVersion
    {
        get => _newVersion;
        set
        {
            this.RaiseAndSetIfChanged(ref _newVersion, value);
            this.RaisePropertyChanged(nameof(NewVersionText));
        }
    }

    public string? NewVersionText => NewVersion == null ? null : $"v{NewVersion.ToString(3)}";
    
    private string _changeLog;
    
    public string ChangeLog
    {
        get => _changeLog;
        set => this.RaiseAndSetIfChanged(ref _changeLog, value);
    }
    
    private string _updateInfoText = "";
    
    public string UpdateInfoText
    {
        get => _updateInfoText;
        set => this.RaiseAndSetIfChanged(ref _updateInfoText, value);
    }
    
    private bool _updating = false;
    
    public bool Updating
    {
        get => _updating;
        set => this.RaiseAndSetIfChanged(ref _updating, value);
    }
    
    private bool _updateAvailable = false;
    
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set => this.RaiseAndSetIfChanged(ref _updateAvailable, value);
    }
    
    private bool _checkingForUpdates = false;
    
    public bool CheckingForUpdates
    {
        get => _checkingForUpdates;
        set => this.RaiseAndSetIfChanged(ref _checkingForUpdates, value);
    }
    
    private int _downloadProgress;
    
    public int DownloadProgress
    {
        get => _downloadProgress;
        set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
    }
    
    private string? _expectedSha256;

    public async Task UpdateInstaller()
    {
        Updating = true;
        UpdateAvailable = false;

        var newInstaller = await DownloadNewInstaller();

        if (newInstaller == null)
        {
            return;
        }

        UpdateInfoText = "Restarting the installer ...";

        var error = await SelfUpdater.ReplaceAndRestart(newInstaller);

        if (error != null)
        {
            UpdateInfoText = error;
        }
    }

    private async Task<FileInfo?> DownloadNewInstaller()
    {
        UpdateInfoText = $"Downloading installer v{NewVersion}";

        var progress = new Progress<double>(x => DownloadProgress = (int)x);

        var file = await DownloadCacheHelper.DownloadFileAsync(DownloadCacheHelper.InstallerFileName, DownloadCacheHelper.InstallerUrls,
            progress);

        if (file == null || !file.Exists)
        {
            UpdateInfoText = "Failed to download new installer :(";
            return null;
        }

        if (_expectedSha256 == null)
        {
            Log.Warning("installer.json has no hash for the new installer, it is used unchecked");
        }
        else if (!FileHashHelper.CheckHash(file, System.Security.Cryptography.HashAlgorithmName.SHA256,
                     Convert.FromHexString(_expectedSha256)))
        {
            Log.Error("The downloaded installer does not match the published hash");
            file.Delete();
            UpdateInfoText = "The new installer was damaged while downloading. Restart the installer to try again.";
            return null;
        }

        return file;
    }
    
    private void EndCheck(string infoText, bool updateAvailable, bool log = true)
    {
        if (log)
        {
            Log.Information(infoText);
        }
        
        UpdateInfoText = infoText;
        CheckingForUpdates = false;
        UpdateAvailable = updateAvailable;
    }
    
    public async Task CheckForUpdates(Version? currentVersion)
    {
        if (currentVersion == null)
            return;
        
        UpdateInfoText = "Checking for installer updates";
        CheckingForUpdates = true;
        
        try
        {
            var installerInfoJson =
                await DownloadCacheHelper.GetMetadataAsync("installer.json", DownloadCacheHelper.InstallerInfoUrls);

            if (installerInfoJson == null)
            {
                EndCheck("Failed to download installer info", false);
                return;
            }
            
            var installerInfo =
                JsonSerializer.Deserialize<InstallerInfo>(installerInfoJson, JsonOptions.Default);
            
            if (installerInfo == null)
            {
                EndCheck("Failed to parse installer info json", false);
                return;
            }
            
            var latestVersion = new Version(installerInfo.LatestVersion);
            
            if (latestVersion <= currentVersion)
            {
                EndCheck("No updates available", false);
                return;
            }
            
            NewVersion = latestVersion;
            var sha256 = OperatingSystem.IsWindows() ? installerInfo.Sha256 : installerInfo.Sha256Linux;
            _expectedSha256 = string.IsNullOrWhiteSpace(sha256) ? null : sha256;
            
            foreach (var change in installerInfo.Changes)
            {
                ChangeLog += $"{change}\n";
            }
            
            EndCheck($"Update Installer: v{latestVersion}", true);
            
            return;
        }
        catch (JsonException ex)
        {
            EndCheck("Could not read the installer info, it will be fetched again next launch", false, false);
            Log.Error(ex, "Discarded unreadable installer info");
        }
        catch (Exception ex)
        {
            EndCheck(ex.Message, false, false);
            Log.Error(ex, "Failed to check for updates");
        }
        
        return;
    }
}