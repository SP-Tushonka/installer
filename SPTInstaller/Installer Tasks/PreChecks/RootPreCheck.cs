using System.Threading.Tasks;
using SPTInstaller.Models;

namespace SPTInstaller.Installer_Tasks.PreChecks;

/// <summary>
///     Stop an install as root on Linux, which would leave SPT's files owned by root
/// </summary>
public class RootPreCheck : PreCheckBase
{
    public static bool IsRoot => !OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess;

    public RootPreCheck() : base("Not Running As Root", true)
    {
    }

    public override Task<PreCheckResult> CheckOperation()
    {
        return Task.FromResult(IsRoot
            ? PreCheckResult.FromError(
                "The installer is running as root or with sudo.\n\n"
                + "Close it and run it as your normal user. Otherwise SPT's files end up owned by root and your game may not be found.")
            : PreCheckResult.FromSuccess("The installer is running as a normal user"));
    }
}
