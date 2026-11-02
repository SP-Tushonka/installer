using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using ReactiveUI;
using System.Threading;
using System.Threading.Tasks;
using PatchClient.Models;
using PatcherUtils;
using PatcherUtils.Model;
using Serilog;
using SPTInstaller.Helpers;

namespace SPTInstaller.Installer_Tasks;

public class SetupClientTask : InstallerTaskBase
{
    private InternalData _data;

    public SetupClientTask(InternalData data) : base("Setup Client")
    {
        _data = data;
    }

    // The same counters patcher.exe shows, for the step being patched
    public ObservableCollection<PatchRow> PatchRows { get; } = [];

    private bool _showPatchRows;

    public bool ShowPatchRows
    {
        get => _showPatchRows;
        set => this.RaiseAndSetIfChanged(ref _showPatchRows, value);
    }

    public override async Task<IResult> TaskOperation()
    {
        var targetInstallDirInfo = new DirectoryInfo(_data.TargetInstallPath);

        var patcherOutputDir = new DirectoryInfo(Path.Join(_data.TargetInstallPath, "patcher"));

        // The patcher library writes Patcher.log by default, the installer keeps one log instead
        PatchLogger.Redirect = (level, message) => Log.Information("[Patcher {level}] {message}", level, message);

        var progress = new Progress<double>((d) => { SetStatus(null, null, (int)Math.Floor(d)); });

        SetStatus("Preparing 7z", "", null, ProgressStyle.Indeterminate);

        if (!ZipHelper.PrepareSevenZip())
        {
            return Result.FromError("Failed to prepare 7z");
        }

        for (var i = 0; i < _data.Patches.Count; i++)
        {
            var patch = _data.Patches[i];
            var step = _data.Patches.Count > 1 ? $" {i + 1}/{_data.Patches.Count} ({patch.Patch.Route})" : $" ({patch.Patch.Route})";

            SetStatus($"Preparing Patch{step}", "", 0);

            var buildResult = PatchFolder.Build(patch, patcherOutputDir, progress);

            if (!buildResult.Succeeded)
            {
                return buildResult;
            }

            SetStatus($"Patching{step}", "", 0);

            var patchingResult = ApplyPatches(targetInstallDirInfo, patcherOutputDir);
            await Dispatcher.UIThread.InvokeAsync(() => ShowPatchRows = false);

            if (!patchingResult.Succeeded)
            {
                return patchingResult;
            }

            patcherOutputDir.Delete(true);
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

        // Archives built on Windows have no exec bits to restore
        if (!OperatingSystem.IsWindows() && Directory.Exists(sptPath))
        {
            foreach (var program in Directory.GetFiles(sptPath, "*.Linux"))
            {
                FileHelper.MakeExecutable(program);
            }
        }

        // SPT works without them, so a failure is logged instead of stopping the install
        ShortcutHelper.CreateSptShortcuts(_data.TargetInstallPath, sptPath);

        if (!OperatingSystem.IsWindows())
        {
            ShortcutHelper.CreateLinuxMenuEntries(sptPath);
        }

        // cleanup temp files
        SetStatus("Cleanup", "almost done :)", null, ProgressStyle.Indeterminate);

        patcherOutputDir.Refresh();

        if (patcherOutputDir.Exists)
        {
            patcherOutputDir.Delete(true);
        }

        return Result.FromSuccess("Setup complete. Happy playing!");
    }

    private Result ApplyPatches(DirectoryInfo gameDir, DirectoryInfo patchesDir)
    {
        if (!File.Exists(Path.Join(gameDir.FullName, "EscapeFromTarkov.exe")))
        {
            return Result.FromError("The game executable is missing from the install path");
        }

        var patcher = new PatchHelper(gameDir.FullName, null, patchesDir.FullName);
        var shownPercent = -1;

        Dispatcher.UIThread.Invoke(() =>
        {
            PatchRows.Clear();
            ShowPatchRows = true;
        });

        // It reports after every file, thousands per step, so the screen only follows whole percents.
        // The first report comes before any file is done, with the full counts the rows take as totals.
        patcher.ProgressChanged += (_, done, total, percent, _, counters) =>
        {
            if (Interlocked.Exchange(ref shownPercent, percent) == percent && done != total)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (PatchRows.Count == 0)
                {
                    foreach (var counter in counters)
                    {
                        PatchRows.Add(new PatchRow(counter.ItemText, counter.ItemValue));
                    }
                }

                foreach (var counter in counters)
                {
                    PatchRows.FirstOrDefault(row => row.Name == counter.ItemText)?.Update(counter.ItemValue);
                }

                SetStatus(null, $"{done:N0} of {total:N0} files", percent, noLog: true);
            });
        };

        var result = patcher.ApplyPatches();

        return result.ExitCode switch
        {
            PatcherExitCode.Success => Result.FromSuccess(),
            PatcherExitCode.MissingFile => Result.FromError(
                "Vital game files were not found. The installer is unable to continue. Please reinstall the game and try again."),
            _ => Result.FromError($"A patch failed to apply. {result.Message}"),
        };
    }
}
