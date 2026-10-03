using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Linq;
using System.Threading.Tasks;
using SPTInstaller.Helpers;

namespace SPTInstaller.Installer_Tasks;

public class SetupClientTask : InstallerTaskBase
{
    private InternalData _data;
    
    public SetupClientTask(InternalData data) : base("Setup Client")
    {
        _data = data;
    }
    
    public override async Task<IResult> TaskOperation()
    {
        var targetInstallDirInfo = new DirectoryInfo(_data.TargetInstallPath);
        
        var patcherOutputDir = new DirectoryInfo(Path.Join(_data.TargetInstallPath, "patcher"));
        
        var patcherEXE = new FileInfo(Path.Join(_data.TargetInstallPath, "patcher.exe"));

        var patchesDir = new DirectoryInfo(Path.Join(_data.TargetInstallPath, "SPT_Patches"));

        var patcherLog = Path.Join(_data.TargetInstallPath, "patcher.log");

        foreach (var staleLog in Directory.GetFiles(_data.TargetInstallPath, "patcher-*.log"))
        {
            File.Delete(staleLog);
        }
        
        var progress = new Progress<double>((d) => { SetStatus(null, null, (int)Math.Floor(d)); });
        
        SetStatus("Preparing 7z", "", null, ProgressStyle.Indeterminate);
        
        if (!FileHelper.StreamAssemblyResourceOut("7z.dll", Path.Join(DownloadCacheHelper.CachePath, "7z.dll")))
        {
            return Result.FromError("Failed to prepare 7z");
        }
        
        for (var i = 0; i < _data.PatcherZips.Count; i++)
        {
            var step = _data.PatcherZips.Count > 1 ? $" {i + 1}/{_data.PatcherZips.Count}" : "";

            SetStatus($"Extracting Patcher{step}", "", 0);

            if (patcherOutputDir.Exists)
            {
                patcherOutputDir.Delete(true);
            }
            
            var extractPatcherResult = ZipHelper.Decompress(_data.PatcherZips[i], patcherOutputDir, progress);
            
            if (!extractPatcherResult.Succeeded)
            {
                return extractPatcherResult;
            }
            
            SetStatus($"Copying Patcher{step}", "", 0);
            
            var patcherDirInfo =
                patcherOutputDir.GetDirectories("Patcher*", SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? patcherOutputDir;
            
            var copyPatcherResult =
                FileHelper.CopyDirectoryWithProgress(patcherDirInfo, targetInstallDirInfo, progress);
            
            if (!copyPatcherResult.Succeeded)
            {
                return copyPatcherResult;
            }
            
            SetStatus($"Running Patcher{step}", "", null, ProgressStyle.Indeterminate);
            
            var patchingResult = ProcessHelper.PatchClientFiles(patcherEXE, targetInstallDirInfo);
            
            if (!patchingResult.Succeeded)
            {
                return patchingResult;
            }

            // The next step's patcher must only find its own patches
            patchesDir.Refresh();

            if (patchesDir.Exists)
            {
                patchesDir.Delete(true);
            }

            // Each patcher writes patcher.log, so an earlier step's log is kept under its step number
            if (i < _data.PatcherZips.Count - 1 && File.Exists(patcherLog))
            {
                File.Move(patcherLog, Path.Join(_data.TargetInstallPath, $"patcher-{i + 1}.log"), true);
            }
        }
        
        // extract release files
        SetStatus("Extracting Release", "", 0);
        
        var extractReleaseResult = ZipHelper.Decompress(_data.SPTZipInfo, targetInstallDirInfo, progress);
        
        if (!extractReleaseResult.Succeeded)
        {
            return extractReleaseResult;
        }

        SetStatus("Creating Shortcuts", "", 0);

        var sptPath = $"{Path.Join(_data.TargetInstallPath, _data.ReleaseInfo?.RuntimeFolderName ?? "SPT_Runtime")}";
        var shortcutResult = ProcessHelper.RunEmbeddedScript("add_shortcuts.ps1", _data.TargetInstallPath, sptPath);
        if (!shortcutResult.Succeeded)
        {
            return shortcutResult;
        }

        // cleanup temp files
        SetStatus("Cleanup", "almost done :)", null, ProgressStyle.Indeterminate);
        
        if (_data.PatchNeeded)
        {
            if (patcherOutputDir.Exists)
            {
                patcherOutputDir.Delete(true);
            }

            if (patcherEXE.Exists)
            {
                patcherEXE.Delete();
            }
        }
        
        return Result.FromSuccess("Setup complete. Happy playing!");
    }
}