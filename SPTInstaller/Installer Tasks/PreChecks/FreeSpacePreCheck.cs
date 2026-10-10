using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SPTInstaller.Helpers;
using SPTInstaller.Models;
using SPTInstaller.Models.Mirrors;

namespace SPTInstaller.Installer_Tasks.PreChecks;

public class FreeSpacePreCheck : PreCheckBase
{
    private const long GigaByte = 1024L * 1024 * 1024;

    // Used when the patches cannot be worked out, or the manifest predates published sizes
    private const long FallbackCacheSpace = 15 * GigaByte;
    private const long FallbackPatchingSpace = 10 * GigaByte;

    private const long SystemDriveReserve = 30 * GigaByte;

    // Used when the host does not report the release zip's size
    private const long FallbackReleaseSize = 2 * GigaByte;

    private const int ReleaseExtractRatio = 6;

    private readonly InternalData _internalData;

    public FreeSpacePreCheck(InternalData internalData) : base("Free Space", true)
    {
        _internalData = internalData;
    }

    public override async Task<PreCheckResult> CheckOperation()
    {
        if (_internalData.OriginalGamePath is null)
            return PreCheckResult.FromError("Could not find the game path");

        if (_internalData.TargetInstallPath is null)
            return PreCheckResult.FromError("Could not find install target path");

        try
        {
            var installRoot = new DirectoryInfo(_internalData.TargetInstallPath).Root.Name;
            var cacheRoot = new DirectoryInfo(DownloadCacheHelper.CachePath).Root.Name;

            var gameSize = DirectorySizeHelper.GetSizeOfDirectory(new DirectoryInfo(_internalData.OriginalGamePath));

            if (gameSize == -1)
            {
                return PreCheckResult.FromError("An error occurred while getting the game source directory size. This is most likely because the game is not installed");
            }

            var patches = await PatchPlan.ForSelectedReleaseAsync(_internalData);
            var sized = patches != null && patches.All(patch => patch.Size > 0);

            // Each step is extracted and then copied into the install folder before it is cleared, so the largest step counts twice
            var patchingSpace = sized ? 2 * patches!.Select(patch => patch.Size!.Value).DefaultIfEmpty().Max() : FallbackPatchingSpace;
            var downloadSpace = sized ? patches!.Sum(StillToDownload) : FallbackCacheSpace;

            var releaseSize = await ReleaseSizeAsync();
            var releaseExtract = releaseSize * ReleaseExtractRatio;
            downloadSpace += StillToDownloadRelease(releaseSize);

            var installNeed = gameSize + patchingSpace + releaseExtract;
            var sameDrive = string.Equals(installRoot, cacheRoot, StringComparison.OrdinalIgnoreCase);

            List<string> installItems = [$"• Copy of the game: {Size(gameSize)}"];

            if (patchingSpace > 0)
            {
                installItems.Add($"• Applying the patches, freed afterwards: {Size(patchingSpace)}");
            }

            installItems.Add($"• SPT itself: {Size(releaseExtract)}");

            List<string> downloadItems = [$"• Patch and SPT downloads: {Size(downloadSpace)}"];

            List<(string Root, string Role, long Need, long Free, List<string> Items)> drives = sameDrive
                ? [(installRoot, "install folder and download cache", installNeed + downloadSpace, FreeSpace(installRoot), [.. installItems, .. downloadItems])]
                :
                [
                    (installRoot, "install folder", installNeed, FreeSpace(installRoot), installItems),
                    (cacheRoot, "download cache", downloadSpace, FreeSpace(cacheRoot), downloadItems),
                ];

            // Windows keeps the page file, updates and temp files on its own drive and slows down or fails when it runs low.
            // The reserve is shown but not counted as needed, so it warns instead of blocking.
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var reserveItem = $"• Kept free for Windows (recommended): {Size(SystemDriveReserve)}";
            var systemIndex = drives.FindIndex(drive => string.Equals(drive.Root, systemRoot, StringComparison.OrdinalIgnoreCase));

            if (systemIndex >= 0)
            {
                drives[systemIndex].Items.Add(reserveItem);
            }
            else
            {
                drives.Add((systemRoot, "Windows", 0, FreeSpace(systemRoot), [reserveItem]));
            }

            var details = string.Join("\n\n", drives.Select(drive =>
                $"**{drive.Root} ({drive.Role})**: "
                + (drive.Need > 0 ? $"{Size(drive.Need)} needed, " : "")
                + $"{Size(drive.Free)} free"
                + (drive.Need > drive.Free ? $", {Size(drive.Need - drive.Free)} short" : "")
                + $"\n{string.Join('\n', drive.Items)}"));

            if (!sized)
            {
                details += "\n\nThe exact patch sizes are not known, so a safe estimate is used";
            }

            if (drives.Any(drive => drive.Need > drive.Free))
            {
                return PreCheckResult.FromError($"Not enough free space for this install\n\n{details}");
            }

            var systemLeft = FreeSpace(systemRoot) - (systemIndex >= 0 ? drives[systemIndex].Need : 0);

            if (systemLeft < SystemDriveReserve)
            {
                return PreCheckResult.FromWarning(
                    $"There is enough space for this install, but {systemRoot} will only have {Size(Math.Max(0, systemLeft))} left afterwards\n\n"
                    + $"Windows needs room on {systemRoot} for the page file, updates and temporary files. At least {Size(SystemDriveReserve, 0)} free is recommended.\n\n{details}");
            }

            return PreCheckResult.FromSuccess($"There is enough space available for this install\n\n{details}");
        }
        catch (Exception ex)
        {
            return PreCheckResult.FromException(ex);
        }
    }

    /// <summary>
    /// Only what is not on disk yet. A finished or chunked part file already holds the full size, a streamed one what it got so far.
    /// </summary>
    private static long StillToDownload(PatchInfo patch)
    {
        var name = DownloadCacheHelper.PatcherFileName(patch.SourceClientVersion, patch.TargetClientVersion);
        var cache = new DirectoryInfo(DownloadCacheHelper.CachePath);

        var onDisk = cache.Exists
            ? cache.GetFiles($"{name}*").Select(file => file.Length).DefaultIfEmpty().Max()
            : 0;

        return Math.Max(0, patch.Size!.Value - onDisk);
    }

    private async Task<long> ReleaseSizeAsync()
    {
        var channel = _internalData.SelectedChannel;
        var url = channel?.Release?.Mirrors?.ElementAtOrDefault(channel.MirrorIndex)?.DownloadUrl;

        return url == null ? FallbackReleaseSize : await DownloadCacheHelper.GetRemoteSizeAsync(url) ?? FallbackReleaseSize;
    }

    // The release is cached as "SPT" whatever its version, so only a file of exactly this size counts as this one
    private static long StillToDownloadRelease(long releaseSize)
    {
        var cache = new DirectoryInfo(DownloadCacheHelper.CachePath);

        if (!cache.Exists)
        {
            return releaseSize;
        }

        if (cache.GetFiles("SPT").Any(file => file.Length == releaseSize))
        {
            return 0;
        }

        var partial = cache.GetFiles("SPT.*.part").Select(file => file.Length).Where(length => length <= releaseSize)
            .DefaultIfEmpty().Max();

        return releaseSize - partial;
    }

    private static long FreeSpace(string root) => DriveInfo.GetDrives()
        .FirstOrDefault(drive => string.Equals(drive.Name, root, StringComparison.OrdinalIgnoreCase))
        ?.AvailableFreeSpace ?? 0;

    private static string Size(long bytes, int decimals = 2) => DirectorySizeHelper.SizeSuffix(bytes, decimals);
}
