using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
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
            var installPath = _internalData.TargetInstallPath;
            var installRoot = FileHelper.DriveRoot(installPath);
            var cacheRoot = FileHelper.DriveRoot(DownloadCacheHelper.CachePath);

            var gameSize = DirectorySizeHelper.GetSizeOfDirectory(new DirectoryInfo(_internalData.OriginalGamePath));

            if (gameSize == -1)
            {
                return PreCheckResult.FromError("An error occurred while getting the game source directory size. This is most likely because the game is not installed");
            }

            var patches = (await PatchPlan.ForSelectedReleaseAsync(_internalData))?.Patches;
            var documents = patches == null ? null : await DocumentsAsync(patches);
            var sized = documents != null;
            string[] downloadFolders = [DownloadCacheHelper.CachePath, DownloadCacheHelper.InstallFolderDownloads(installPath)];

            // Each step is extracted and then copied into the install folder before it is cleared, so the largest step counts twice
            var patchingSpace = sized ? 2 * documents!.Select(document => document.Entries.Sum(entry => entry.Size ?? 0)).DefaultIfEmpty().Max() : FallbackPatchingSpace;
            var downloadSpace = sized ? documents!.Sum(document => StillToDownload(document, downloadFolders)) : FallbackCacheSpace;

            var releaseSize = await ReleaseSizeAsync();
            var releaseExtract = releaseSize * ReleaseExtractRatio;
            downloadSpace += StillToDownloadRelease(releaseSize, downloadFolders);

            var installNeed = gameSize + patchingSpace + releaseExtract;

            // What stays once the patching space and downloads are cleared
            var installedSize = Size(gameSize + releaseExtract, 0);
            Summary = $"{installedSize} installed";
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

            var inInstallFolder = DownloadCacheHelper.DownloadsInInstallFolder;
            var plan = Plan(inInstallFolder ? installRoot : cacheRoot);

            // Points at the checkbox when the cache drive is what stops or endangers the install and moving the downloads would fix it
            var checkboxHint = "";

            if (!inInstallFolder && !SameRoot(installRoot, cacheRoot) && (plan.Short.Count > 0 || plan.SystemLeft < SystemDriveReserve))
            {
                var local = Plan(installRoot);

                if (local.Short.Count == 0 && (plan.Short.Count > 0 || local.SystemLeft >= SystemDriveReserve))
                {
                    checkboxHint = "\n\nTicking \"Download into the install folder\" under Details fixes this";
                }
            }

            var details = string.Join("\n\n", plan.Drives.Select(drive =>
                $"{drive.Root} ({drive.Role}): "
                + (drive.Need > 0 ? $"{Size(drive.Need)} needed, " : "")
                + $"{Size(drive.Free)} free"
                + (drive.Need > drive.Free ? $", {Size(drive.Need - drive.Free)} short" : "")
                + $"\n{string.Join('\n', drive.Items)}"));

            if (!sized)
            {
                details += "\n\nThe exact patch sizes are not known, so a safe estimate is used";
            }

            // The screen only has room for a short summary, so the breakdown goes to the log for support
            Log.Information("Free space breakdown, downloads in {folder}\n{Details}", DownloadCacheHelper.DownloadPath, details);

            if (plan.Short.Count > 0)
            {
                var lines = plan.Short.Select(drive =>
                    $"{drive.Root} needs {Size(drive.Need, 0)} for the {drive.Role} but only has {Size(drive.Free, 0)} free");

                return PreCheckResult.FromError(
                    $"{string.Join('\n', lines)}\n\nFree up {Size(plan.Short.Max(drive => drive.Need - drive.Free), 0)} or pick a folder on another drive"
                    + checkboxHint);
            }

            if (plan.SystemLeft < SystemDriveReserve)
            {
                return PreCheckResult.FromWarning(
                    $"{systemRoot} will have {Size(Math.Max(0, plan.SystemLeft), 0)} left after installing\n\n"
                    + $"Windows needs about {Size(SystemDriveReserve, 0)} there for the page file, updates and temporary files. You can still install, but freeing some space first is recommended"
                    + checkboxHint);
            }

            return PreCheckResult.FromSuccess($"SPT takes about {installedSize} once installed\n\n"
                + string.Join('\n', plan.Drives.Where(drive => drive.Need > 0).Select(drive =>
                    $"{drive.Root} has {Size(drive.Free, 0)} free, {Size(drive.Need, 0)} is needed while installing")));

            SpacePlan Plan(string downloadRoot)
            {
                List<string> installItems = [$"• Copy of the game: {Size(gameSize)}"];

                if (patchingSpace > 0)
                {
                    installItems.Add($"• Applying the patches, freed afterwards: {Size(patchingSpace)}");
                }

                installItems.Add($"• SPT itself: {Size(releaseExtract)}");

                List<string> downloadItems = [$"• Patch and SPT downloads: {Size(downloadSpace)}"];

                List<DriveNeed> drives = SameRoot(installRoot, downloadRoot)
                    ? [new(installRoot, "install folder and downloads", installNeed + downloadSpace, FreeSpace(installRoot), [.. installItems, .. downloadItems])]
                    :
                    [
                        new(installRoot, "install folder", installNeed, FreeSpace(installRoot), installItems),
                        new(downloadRoot, "download cache", downloadSpace, FreeSpace(downloadRoot), downloadItems),
                    ];

                var shortDrives = drives.Where(drive => drive.Need > drive.Free).ToList();

                if (!OperatingSystem.IsWindows())
                {
                    return new SpacePlan(drives, shortDrives, long.MaxValue);
                }

                // Windows keeps the page file, updates and temp files on its own drive and slows down or fails when it runs low.
                // The reserve is shown but not counted as needed, so it warns instead of blocking.
                var reserveItem = $"• Kept free for Windows (recommended): {Size(SystemDriveReserve)}";
                var system = drives.Find(drive => SameRoot(drive.Root, systemRoot));

                if (system != null)
                {
                    system.Items.Add(reserveItem);
                }
                else
                {
                    drives.Add(new(systemRoot, "Windows", 0, FreeSpace(systemRoot), [reserveItem]));
                }

                var systemLeft = FreeSpace(systemRoot) - (system?.Need ?? 0);

                return new SpacePlan(drives, shortDrives, systemLeft);
            }
        }
        catch (Exception ex)
        {
            return PreCheckResult.FromException(ex);
        }
    }

    private record DriveNeed(string Root, string Role, long Need, long Free, List<string> Items);

    private record SpacePlan(List<DriveNeed> Drives, List<DriveNeed> Short, long SystemLeft);

    private static bool SameRoot(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<FileInfo> FilesIn(string[] folders, string pattern) => folders
        .Select(folder => new DirectoryInfo(folder))
        .Where(folder => folder.Exists)
        .SelectMany(folder => folder.GetFiles(pattern));

    /// <summary>
    /// Only what is not on disk yet. A finished or chunked part file already holds the full size, a streamed one what it got so far.
    /// </summary>
    private static long StillToDownload(PatchDocument document, string[] folders)
    {
        return document.Entries.Where(entry => entry.Blob != null).DistinctBy(entry => entry.Blob).Sum(entry =>
        {
            var onDisk = FilesIn(folders, $"{entry.Blob}*").Select(file => file.Length).DefaultIfEmpty().Max();

            return Math.Max(0, (entry.Size ?? 0) - onDisk);
        });
    }

    /// <summary>
    /// The documents of the patches the install needs, or null when one could not be fetched
    /// </summary>
    private static async Task<List<PatchDocument>?> DocumentsAsync(List<PatchInfo> patches)
    {
        List<PatchDocument> documents = [];

        foreach (var patch in patches)
        {
            var document = (await PatchDocuments.GetAsync(patch, PatchDocuments.DocumentLinks(patch.Mirrors), new Progress<double>())).Document;

            if (document == null)
            {
                return null;
            }

            documents.Add(document);
        }

        return documents;
    }

    private async Task<long> ReleaseSizeAsync()
    {
        var channel = _internalData.SelectedChannel;
        var url = channel?.Release?.Mirrors?.ElementAtOrDefault(channel.MirrorIndex)?.DownloadUrl;

        return url == null ? FallbackReleaseSize : await DownloadCacheHelper.GetRemoteSizeAsync(url) ?? FallbackReleaseSize;
    }

    // The release is cached as "SPT" whatever its version, so only a file of exactly this size counts as this one
    private static long StillToDownloadRelease(long releaseSize, string[] folders)
    {
        if (FilesIn(folders, "SPT").Any(file => file.Length == releaseSize))
        {
            return 0;
        }

        var partial = FilesIn(folders, "SPT.*.part").Select(file => file.Length).Where(length => length <= releaseSize)
            .DefaultIfEmpty().Max();

        return releaseSize - partial;
    }

    private static long FreeSpace(string root) => DriveInfo.GetDrives()
        .FirstOrDefault(drive => string.Equals(drive.Name, root, StringComparison.OrdinalIgnoreCase))
        ?.AvailableFreeSpace ?? 0;

    private static string Size(long bytes, int decimals = 2) => DirectorySizeHelper.SizeSuffix(bytes, decimals);
}
