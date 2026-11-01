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
            
            ReleaseInfo? sptReleaseInfo = null;
            PatchManifest? patchManifest = null;
            
            int retries = 1;

            while (retries >= 0)
            {
                retries--;
                
                try
                {
                    var releaseJson = await DownloadCacheHelper.GetMetadataAsync("release.json", DownloadCacheHelper.ReleaseUrls);

                    if (releaseJson == null)
                    {
                        return Result.FromError("Failed to download the release list.\nCheck your connection and try again.");
                    }
            
                    SetStatus("Checking for Patches", "", null, ProgressStyle.Indeterminate);
            
                    var mirrorsJson = await DownloadCacheHelper.GetMetadataAsync("mirrors.json", DownloadCacheHelper.PatchManifestUrls);

                    if (mirrorsJson == null)
                    {
                        return Result.FromError("Failed to download the patch list.\nCheck your connection and try again.");
                    }

                    var releaseManifest = JsonSerializer.Deserialize<ReleaseManifest>(releaseJson, JsonOptions.Default);

                    patchManifest = JsonSerializer.Deserialize<PatchManifest>(mirrorsJson, JsonOptions.Default);

                    // Nothing chosen yet means the first published release, which is the newest.
                    sptReleaseInfo = _data.SelectedChannel?.Release ?? releaseManifest?.Releases?.FirstOrDefault();

                    break;
                }
                catch (Exception ex)
                {
                    if (retries >= 0)
                    {
                        SetStatus("Retrying ...", "", null, ProgressStyle.Indeterminate);
                        await Task.Delay(1000);
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
            
            var route = PatchPlan.Find(patchManifest, intGameVersion, intSPTVersion);

            if (route.Patches == null)
            {
                return Result.FromError(route.Problem ?? PatchPlan.PatcherOutOfDate);
            }

            _data.PatchChain = route.Patches;

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
