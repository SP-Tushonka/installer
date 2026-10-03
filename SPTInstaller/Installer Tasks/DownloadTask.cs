using System;
using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SPTInstaller.Helpers;
using SPTInstaller.Models.Mirrors;

namespace SPTInstaller.Installer_Tasks;

public class DownloadTask : InstallerTaskBase
{
    private InternalData _data;
    
    public DownloadTask(InternalData data) : base("Download Files")
    {
        _data = data;
    }
    
    private async Task<(List<PatchInfoMirror>? Mirrors, IResult? Error)> SelectMirrors(PatchInfo patch)
    {
        var mirrors = patch.Mirrors;
        var selectedName = _data.SelectedChannel?.MirrorName;

        // A chosen mirror is honoured exactly, so a failure is reported rather than quietly served
        // from somewhere the user did not pick.
        if (!string.IsNullOrWhiteSpace(selectedName))
        {
            mirrors = mirrors.Where(mirror =>
                string.Equals(mirror.Name, selectedName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (mirrors.Count == 0)
            {
                return (null, Result.FromError($"No patch mirror named '{selectedName}' is published for this release."));
            }
        }

        return (await HostReachability.KeepReachableAsync(mirrors, mirror => mirror.Link), null);
    }

    private static string HostOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static async Task<IResult> DownloadFailed(string what, IReadOnlyList<string> urls)
    {
        var hosts = string.Join(" or ", urls.Select(HostOf).Distinct(StringComparer.OrdinalIgnoreCase));

        foreach (var url in urls)
        {
            if (await HostReachability.IsReachableAsync(url))
            {
                return Result.FromError($"Failed to download {what} from {hosts}");
            }
        }

        return Result.FromError(
            $"Could not reach {hosts} to download {what}.\n\n" +
            "If you chose a specific option in the version list, try another one. Otherwise check your internet connection, VPN or firewall.");
    }

    private async Task<IResult> DownloadPatcherAsync(PatchInfo patch, int step, int steps, IProgress<double> progress)
    {
        var title = steps > 1 ? $"Downloading Patcher {step}/{steps}" : "Downloading Patcher";
        var (mirrors, error) = await SelectMirrors(patch);

        if (error != null)
        {
            return error;
        }

        var links = mirrors!.Select(mirror => mirror.Link).ToList();
        var spec = DownloadSpec.From(patch.Sha256, mirrors[0].Hash, patch.Size, patch.ChunkSize, patch.ChunkHashes);

        SetStatus(title, "Verifying cached patcher ...", progressStyle: ProgressStyle.Indeterminate);

        var download = await DownloadCacheHelper.GetOrDownloadFileAsync(
            DownloadCacheHelper.PatcherFileName(patch.SourceClientVersion, patch.TargetClientVersion), links, spec, progress,
            status => SetStatus(null, status, noLog: true));

        switch (download.Outcome)
        {
            case DownloadOutcome.Downloaded:
                _data.PatcherZips.Add(download.File!);
                return Result.FromSuccess();
            case DownloadOutcome.Corrupted:
                // A patch regenerated after mirrors.json was cached fails every hash, so the retry must fetch fresh metadata
                DownloadCacheHelper.ClearMetadataCache();
                return Result.FromRetryableError(
                    "The patcher downloaded but kept failing verification, so it was discarded.\n\n" +
                    "This usually means the download was damaged on this PC. " +
                    "If redownloading keeps failing, try pausing your antivirus or disabling a RAM overclock (XMP/EXPO).",
                    "Redownload patcher");
            default:
                return await DownloadFailed("the patcher", links);
        }
    }
    
    private async Task<IResult> DownloadSPTFromMirrors(IProgress<double> progress)
    {
        var mirrors = await HostReachability.KeepReachableAsync(_data.ReleaseInfo.Mirrors, mirror => mirror.DownloadUrl);

        foreach (var mirror in mirrors)
        {
            SetStatus("Downloading", mirror.DownloadUrl, progressStyle: ProgressStyle.Indeterminate);

            var download = await DownloadCacheHelper.GetOrDownloadFileAsync("SPT", [mirror.DownloadUrl],
                DownloadSpec.From(mirror.Sha256, mirror.Hash), progress, status => SetStatus(null, status, noLog: true));

            if (download.Outcome == DownloadOutcome.Downloaded)
            {
                _data.SPTZipInfo = download.File!;
                return Result.FromSuccess();
            }
        }

        return await DownloadFailed("the release files", mirrors.Select(mirror => mirror.DownloadUrl).ToList());
    }
    
    public override async Task<IResult> TaskOperation()
    {
        var progress = new Progress<double>((d) => { SetStatus(null, null, (int)Math.Floor(d)); });

        _data.PatcherZips.Clear();

        for (var i = 0; i < _data.PatchChain.Count; i++)
        {
            var result = await DownloadPatcherAsync(_data.PatchChain[i], i + 1, _data.PatchChain.Count, progress);

            if (!result.Succeeded)
            {
                return result;
            }
        }
        
        return await DownloadSPTFromMirrors(progress);
    }
}
