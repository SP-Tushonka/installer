using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Threading.Tasks;
using SPTInstaller.Helpers;

namespace SPTInstaller.Installer_Tasks;

public class InitializationTask : InstallerTaskBase
{
    private InternalData _data;
    
    public InitializationTask(InternalData data) : base("Startup")
    {
        _data = data;
    }
    
    public override async Task<IResult> TaskOperation()
    {
        SetStatus("Initializing", $"Installed Game Path: {FileHelper.GetRedactedPath(_data.OriginalGamePath)}");
        
        var result = PreCheckHelper.DetectOriginalGameVersion(_data.OriginalGamePath);
        
        if (!result.Succeeded)
        {
            return result;
        }
        
        _data.OriginalGameVersion = result.Message;
        
        SetStatus(null, $"Installed Game Version: {_data.OriginalGameVersion}");
        
        if (_data.OriginalGamePath == null)
        {
            return Result.FromError(
                "Unable to find the original game directory, please make sure the game is installed. Please also run the game once");
        }
        
        if (File.Exists(Path.Join(_data.TargetInstallPath, "EscapeFromTarkov.exe")))
        {
            return Result.FromError(
                "Install location is a folder that has existing game files. Please make sure the folder doesn't contain an existing install");
        }
        
        return Result.FromSuccess($"Current Game Version: {_data.OriginalGameVersion}");
    }
}