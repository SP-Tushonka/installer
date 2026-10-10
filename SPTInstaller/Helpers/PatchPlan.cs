using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;
using SPTInstaller.Models;
using SPTInstaller.Models.Mirrors;

namespace SPTInstaller.Helpers;

public static class PatchPlan
{
    /// <summary>
    /// The patches the selected release needs on the installed game, worked out before the install starts
    /// </summary>
    /// <returns>An empty list when no patch is needed, or null when it cannot be worked out</returns>
    public static async Task<List<PatchInfo>?> ForSelectedReleaseAsync(InternalData data)
    {
        if (data.OriginalGamePath == null || !int.TryParse(data.SelectedChannel?.Release?.ClientVersion, out var releaseClient))
        {
            return null;
        }

        var detected = PreCheckHelper.DetectOriginalGameVersion(data.OriginalGamePath);

        if (!detected.Succeeded || !int.TryParse(detected.Message, out var gameClient))
        {
            return null;
        }

        if (gameClient == releaseClient)
        {
            return [];
        }

        try
        {
            var manifestFile = await DownloadCacheHelper.GetOrDownloadFileAsync("mirrors.json",
                DownloadCacheHelper.PatchManifestUrls, null, DownloadCacheHelper.SuggestedTtl);

            if (manifestFile == null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(manifestFile.FullName), JsonOptions.Default)
                ?.FindPath(gameClient, releaseClient);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not work out the patches for the selected release");
            return null;
        }
    }
}
