using System.Threading.Tasks;
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
        if (_internalData.OriginalGamePath is null || !Directory.Exists(_internalData.OriginalGamePath) || !File.Exists(Path.Join(_internalData.OriginalGamePath, "Escapefromtarkov.exe")))
        {
            return PreCheckResult.FromError("Your game installation could not be found, try running the game's launcher and ensure the game is installed on your computer", "Retry", RequestReevaluation);
        }
        
        return PreCheckResult.FromSuccess($"Game install folder found. Game Path:\n\n{_internalData.OriginalGamePath}");
    }
}