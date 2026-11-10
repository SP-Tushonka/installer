using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using SPTInstaller.Helpers;

namespace SPTInstaller.Installer_Tasks;

public class CopyClientTask : InstallerTaskBase
{
    private InternalData _data;

    public CopyClientTask(InternalData data) : base("Copy Client Files")
    {
        _data = data;
    }

    public override async Task<IResult> TaskOperation()
    {
        SetStatus("Copying Client Files", "", 0);

        var originalGameDirInfo = new DirectoryInfo(_data.OriginalGamePath);
        var targetInstallDirInfo = new DirectoryInfo(_data.TargetInstallPath);

        var skipped = SkippedGameFiles.Find(_data.Patches.Select(SkippedGameFiles.WithBundled));
        Log.Information("Not copying {count} game files the patches delete or replace", skipped.Count);

        return FileHelper.CopyDirectoryWithProgress(originalGameDirInfo, targetInstallDirInfo,
            (message, progress) => { SetStatus(null, message, progress, null, true); },
            path => skipped.Contains(path) || path.StartsWith("Logs/", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bak"));
    }
}
