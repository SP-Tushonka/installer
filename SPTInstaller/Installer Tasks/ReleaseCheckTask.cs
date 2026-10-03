using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Threading.Tasks;
using SPTInstaller.Helpers;
using System.Linq;
using SPTInstaller.Models.Mirrors;
using SPTInstaller.Models.ReleaseInfo;
using System.Text.Json;
using Serilog;

namespace SPTInstaller.Installer_Tasks;

public class ReleaseCheckTask : InstallerTaskBase
{
    private InternalData _data;
    
    public ReleaseCheckTask(InternalData data) : base("Release Checks")
    {
        _data = data;
    }
    
    public override async Task<IResult> TaskOperation()
    {
        try
        {
            SetStatus("Checking releases", "", null, ProgressStyle.Indeterminate);
            
            var progress = new Progress<double>((d) => { SetStatus(null, null, (int)Math.Floor(d)); });
            
            ReleaseInfo? sptReleaseInfo = null;
            PatchManifest? patchManifest = null;
            
            int retries = 1;

            while (retries >= 0)
            {
                retries--;
                
                try
                {
                    var sptReleaseInfoFile =
                        await DownloadCacheHelper.GetOrDownloadFileAsync("release.json", DownloadCacheHelper.ReleaseUrls,
                            progress, DownloadCacheHelper.SuggestedTtl);
            
                    if (sptReleaseInfoFile == null)
                    {
                        return Result.FromError("Failed to download release metadata, try clicking the 'Whats this' button below followed by the 'Clear Metadata cache' button");
                    }
            
                    SetStatus("Checking for Patches", "", null, ProgressStyle.Indeterminate);
            
                    var sptPatchMirrorsFile =
                        await DownloadCacheHelper.GetOrDownloadFileAsync("mirrors.json", DownloadCacheHelper.PatchManifestUrls,
                            progress, DownloadCacheHelper.SuggestedTtl);
            
                    if (sptPatchMirrorsFile == null)
                    {
                        return Result.FromError("Failed to download patch mirror data, try clicking the 'Whats this' button below followed by the 'Clear Metadata cache' button");
                    }
                    
                    var releaseManifest =
                        JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(sptReleaseInfoFile.FullName), JsonOptions.Default);

                    patchManifest =
                        JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(sptPatchMirrorsFile.FullName), JsonOptions.Default);

                    // Nothing chosen yet means the first published release, which is the newest.
                    sptReleaseInfo = _data.SelectedChannel?.Release ?? releaseManifest?.Releases?.FirstOrDefault();

                    break;
                }
                catch (Exception ex)
                {
                    if (retries >= 0)
                    {
                        SetStatus("Clearing cache and retrying ...", "", null, ProgressStyle.Indeterminate);
                        await Task.Delay(1000);
                        DownloadCacheHelper.ClearMetadataCache();
                        continue;
                    }
                    
                    return Result.FromError(
                        $"An error occurred while deserializing release or patch data.\n\nMost likely we are uploading a new patch.\nPlease wait and try again in an hour\n\nERROR: {ex.Message}");
                }
            }

            if (sptReleaseInfo == null || patchManifest == null)
            {
                return Result.FromError(
                    "Release or mirror info was null. If you are seeing this report it. This should never be hit");
            }

            _data.ReleaseInfo = sptReleaseInfo;
            int intSPTVersion = int.Parse(sptReleaseInfo.ClientVersion);
            int intGameVersion = int.Parse(_data.OriginalGameVersion);
            
            if (intGameVersion < intSPTVersion)
            {
                return Result.FromError("Your live game is out of date. Please update it using the game's launcher and try running the installer again");
            }

            _data.PatchChain = intGameVersion == intSPTVersion
                ? []
                : patchManifest.FindPath(intGameVersion, intSPTVersion) ?? [];

            if (intGameVersion != intSPTVersion && _data.PatchChain.Count == 0)
            {
                // Patches start from the newest live client, so one starting past the user's means their game is behind.
                if (patchManifest.Patches.Any(patch => patch.SourceClientVersion > intGameVersion))
                {
                    return Result.FromError("Your live game is out of date. Please update it using the game's launcher or Steam, then run the installer again.");
                }

                return Result.FromError(
                    "The game has updated. The patcher needs to be updated before you can install." +
                    "\n* There is no time frame provided as to when this will occur. It usually happens within 24 hours." +
                    "\n* The installer will automatically use it when the patcher gets updated." +
                    "\n* This does not mean a new version is being released." +
                    "\n* The patcher is only for turning game files into the older version this install needs.");
            }

            if (_data.PatchChain.Count > 1)
            {
                Log.Information("Patching through {chain}",
                    string.Join(" -> ", _data.PatchChain.Select(patch => patch.SourceClientVersion).Append(intSPTVersion)));
            }
            
            string status =
                $"Current Release: {sptReleaseInfo.ClientVersion} - {(_data.PatchNeeded ? "Patch Available" : "No Patch Needed")}";
            
            SetStatus(null, status);
            
            return Result.FromSuccess(status);
        }
        catch (Exception ex)
        {
            //request failed
            return Result.FromError($"Request Failed:\n{ex.Message}");
        }
    }
}
