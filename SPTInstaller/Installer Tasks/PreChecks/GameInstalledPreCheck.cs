using System.Linq;
using System.Threading.Tasks;
using SPTInstaller.Helpers;
using SPTInstaller.Models;

namespace SPTInstaller.Installer_Tasks.PreChecks;

public class GameInstalledPreCheck : PreCheckBase
{
    private InternalData _internalData;

    public GameInstalledPreCheck(InternalData data) : base("Game Installed", true)
    {
        _internalData = data;
    }

    public override async Task<PreCheckResult> CheckOperation()
    {
        Summary = null;

        if (_internalData.OriginalGamePath is null || !Directory.Exists(_internalData.OriginalGamePath) || !File.Exists(Path.Join(_internalData.OriginalGamePath, "EscapeFromTarkov.exe")))
        {
            return PreCheckResult.FromError("Your game installation could not be found, try running the game's launcher and ensure the game is installed on your computer", "Retry", RequestReevaluation);
        }

        var gameFolder = $"Game folder:\n{_internalData.OriginalGamePath}";
        var release = _internalData.SelectedChannel?.Release;

        if (release == null)
        {
            return PreCheckResult.FromSuccess(gameFolder);
        }

        var route = await PatchPlan.ForSelectedReleaseAsync(_internalData);

        if (route == null)
        {
            return PreCheckResult.FromWarning(
                $"Could not check whether this game can be patched to SPT {release.SPTVersion}. This is checked again when installing.\n\n{gameFolder}");
        }

        if (route.Patches == null)
        {
            return PreCheckResult.FromError(
                $"SPT {release.SPTVersion} needs client {route.ReleaseClient}, this game is client {route.GameClient}.\n\n{route.Problem}",
                "Retry", RequestReevaluation);
        }

        if (route.Patches.Count == 0)
        {
            Summary = "No patch needed";
            return PreCheckResult.FromSuccess(
                $"The game is already client {route.GameClient}, which SPT {release.SPTVersion} uses.\n\n{gameFolder}");
        }

        var steps = string.Join(" → ", route.Patches.Select(patch => patch.SourceClientVersion).Append(route.ReleaseClient));
        Summary = route.Patches.Count == 1 ? "1 patch" : $"{route.Patches.Count} patches";

        return PreCheckResult.FromSuccess(
            $"The game is client {route.GameClient}. It is patched down to {route.ReleaseClient} for SPT {release.SPTVersion}:\n{steps}\n\n{gameFolder}");
    }
}
