using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;
using SPTInstaller.Models;
using SPTInstaller.Models.Mirrors;

namespace SPTInstaller.Helpers;

/// <summary>
///     The patches that take a game client to the client a release needs
/// </summary>
/// <param name="GameClient">Client version of the installed game</param>
/// <param name="ReleaseClient">Client version the release needs</param>
/// <param name="Patches">Empty when no patch is needed, null when no published patches reach the release</param>
/// <param name="Problem">Why the release can't be reached from this game</param>
public record PatchRoute(int GameClient, int ReleaseClient, List<PatchInfo>? Patches, string? Problem);

public static class PatchPlan
{
    public const string GameOutOfDate =
        "Your live game is out of date. Please update it using the game's launcher or Steam, then run the installer again.";

    public const string PatcherOutOfDate =
        "The game has updated. The patcher needs to be updated before you can install." +
        "\n* There is no time frame provided as to when this will occur. It usually happens within 24 hours." +
        "\n* The installer will automatically use it when the patcher gets updated." +
        "\n* This does not mean a new version is being released." +
        "\n* The patcher is only for turning game files into the older version this install needs.";

    /// <summary>
    ///     Find the route from a game client to a release's client
    /// </summary>
    /// <param name="manifest">Published patches</param>
    /// <param name="gameClient">Client version of the installed game</param>
    /// <param name="releaseClient">Client version the release needs</param>
    /// <returns>The route, with the problem filled in when there is none</returns>
    public static PatchRoute Find(PatchManifest manifest, int gameClient, int releaseClient)
    {
        if (gameClient == releaseClient)
        {
            return new PatchRoute(gameClient, releaseClient, [], null);
        }

        // Patches only go back in time
        if (gameClient < releaseClient)
        {
            return new PatchRoute(gameClient, releaseClient, null, GameOutOfDate);
        }

        var patches = manifest.FindPath(gameClient, releaseClient);

        if (patches != null)
        {
            return new PatchRoute(gameClient, releaseClient, patches, null);
        }

        // Patches start from the newest live client, so one starting past the user's means their game is behind
        return new PatchRoute(gameClient, releaseClient, null,
            manifest.Patches.Any(patch => patch.SourceClientVersion > gameClient) ? GameOutOfDate : PatcherOutOfDate);
    }

    /// <summary>
    ///     Get the patches the selected release needs on the installed game, before the install starts
    /// </summary>
    /// <param name="data">Holds the selected release and the game path</param>
    /// <returns>The route, null when the game's version or the patch list can't be read</returns>
    public static async Task<PatchRoute?> ForSelectedReleaseAsync(InternalData data)
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
            return new PatchRoute(gameClient, releaseClient, [], null);
        }

        try
        {
            var manifestJson = await DownloadCacheHelper.GetMetadataAsync("mirrors.json", DownloadCacheHelper.PatchManifestUrls);
            var manifest = manifestJson == null ? null : JsonSerializer.Deserialize<PatchManifest>(manifestJson, JsonOptions.Default);

            return manifest == null ? null : Find(manifest, gameClient, releaseClient);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not work out the patches for the selected release");
            return null;
        }
    }
}
