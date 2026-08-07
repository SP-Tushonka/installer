using System.Diagnostics;
using System.Threading.Tasks;
using Serilog;
using SPTInstaller.Models;

namespace SPTInstaller.Installer_Tasks.PreChecks;

public class GameLauncherPreCheck : PreCheckBase
{
    public GameLauncherPreCheck() : base("Game Launcher Closed", true)
    {
    }
    
    public async override Task<PreCheckResult> CheckOperation()
    {
        var eftLauncherProcs = Process.GetProcessesByName("BsgLauncher");
        
        return eftLauncherProcs.Length == 0
            ? PreCheckResult.FromSuccess("The game's launcher is closed")
            : PreCheckResult.FromError("Your game's launcher is open, please close it",
                "Kill game launcher processes",
                () =>
                {
                    var bsgLauncherProcs = Process.GetProcessesByName("BsgLauncher");
                    
                    foreach (var proc in bsgLauncherProcs)
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit();
                            Log.Information($"Killed Proc: {proc.ProcessName}#{proc.Id}");
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"Failed to kill proc: {proc.ProcessName}#{proc.Id}");
                        }
                    }
                    
                    RequestReevaluation();
                });
    }
}