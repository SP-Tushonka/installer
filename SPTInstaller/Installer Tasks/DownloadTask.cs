using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        // Every mirror is passed on, DownloadSources narrows it per chunk to the user's choice or the fastest
        var mirrors = await HostReachability.KeepReachableAsync(patch.Mirrors, mirror => mirror.Document);

        foreach (var mirror in mirrors)
        {
            DownloadSources.Name(mirror.Document, mirror.Name);
        }

        if (mirrors.Count == 0)
        {
            return (null, Result.FromError("No patch mirror is published for this release."));
        }

        return (mirrors, null);
    }

    /// <summary>
    /// Turns a blob's percentage into the bytes it adds to the patch total
    /// </summary>
    private sealed class BlobShare(long size, Action<long> count, Action<long> skip) : IProgress<double>
    {
        private long _done;

        /// <summary>
        ///     Count what a new percentage adds. Verifying restarts the percentage and a failed chunk lowers it, so only a rise counts.
        /// </summary>
        /// <param name="percent">How much of the blob is done</param>
        public void Report(double percent)
        {
            var now = (long)(size * percent / 100);
            long done;

            do
            {
                done = Interlocked.Read(ref _done);

                if (now <= done)
                {
                    return;
                }
            } while (Interlocked.CompareExchange(ref _done, now, done) != done);

            count(now - done);
        }

        /// <summary>
        ///     Count the rest of a finished blob as skipped, since anything still uncounted came from the cache
        /// </summary>
        public void Finish() => skip(size - Interlocked.Exchange(ref _done, size));
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

    private static async Task<IResult> PatchDownloadFailed(DownloadResult download, string what, IReadOnlyList<string> urls)
    {
        if (download.Outcome == DownloadOutcome.Corrupted)
        {
            return Result.FromRetryableError(
                $"{what} downloaded but kept failing verification, so it was discarded.\n\n" +
                "This usually means the download was damaged on this PC. " +
                "If redownloading keeps failing, try pausing your antivirus or disabling a RAM overclock (XMP/EXPO).",
                "Redownload patch");
        }

        return await DownloadFailed(what, urls);
    }

    /// <summary>
    /// The patch document, then every blob it lists that is not in the cache yet
    /// </summary>
    private async Task<IResult> DownloadPatchAsync(PatchInfo patch, int step, int steps, IProgress<double> progress)
    {
        var title = steps > 1 ? $"Downloading Patch {step}/{steps} ({patch.Route})" : $"Downloading Patch ({patch.Route})";
        var (mirrors, error) = await SelectMirrors(patch);

        if (error != null)
        {
            return error;
        }

        SetStatus(title, "Fetching the patch list ...", progressStyle: ProgressStyle.Indeterminate);

        var documentLinks = PatchDocuments.DocumentLinks(mirrors!);
        var fetched = await PatchDocuments.GetAsync(patch, documentLinks, progress, status => SetStatus(null, status, noLog: true));
        var document = fetched.Document;

        if (document == null)
        {
            return await PatchDownloadFailed(fetched.Download, "the patch list", documentLinks);
        }

        var blobs = document.Entries.Where(entry => entry.Blob != null).DistinctBy(entry => entry.Blob).ToList();
        var files = new ConcurrentDictionary<string, FileInfo>(StringComparer.Ordinal);
        var completed = 0;
        IResult? failure = null;
        using var stop = new CancellationTokenSource();

        SetStatus(title, "", 0);

        // One bar and one speed line for the whole patch, every blob feeds in the bytes it received
        var meter = new VerifiedDownloader.TransferMeter(blobs.Sum(entry => entry.Size ?? 0), 0, null, progress,
            status => SetStatus(null, $"{Volatile.Read(ref completed)}/{blobs.Count} files \u00b7 {status}", noLog: true));

        try
        {
            await Parallel.ForEachAsync(blobs,
                new ParallelOptions { MaxDegreeOfParallelism = VerifiedDownloader.Parallelism, CancellationToken = stop.Token },
                async (entry, token) =>
                {
                    var share = new BlobShare(entry.Size ?? 0, meter.Add, meter.Skip);
                    var urls = PatchDocuments.BlobLinks(mirrors!, entry.Blob!);
                    var spec = DownloadSpec.From(entry.Blob, "", entry.Size, entry.ChunkSize, entry.ChunkHashes);

                    var download = await DownloadCacheHelper.GetOrDownloadFileAsync(entry.Blob!, urls, spec, share,
                        cancellationToken: token);

                    if (download.Outcome != DownloadOutcome.Downloaded)
                    {
                        var name = entry.Path == null ? "small files" : entry.Path[(entry.Path.LastIndexOf('/') + 1)..];
                        var error = await PatchDownloadFailed(download, $"the patch ({name})", urls);

                        Interlocked.CompareExchange(ref failure, error, null);
                        await stop.CancelAsync();
                        return;
                    }

                    files[entry.Blob!] = download.File!;
                    share.Finish();
                    Interlocked.Increment(ref completed);
                });
        }
        catch (OperationCanceledException) when (failure != null)
        {
        }

        if (failure != null)
        {
            return failure;
        }

        _data.Patches.Add(new PatchDownload(patch, document, files));

        return Result.FromSuccess();
    }

    private async Task<IResult> DownloadSPTFromMirrors(IProgress<double> progress)
    {
        var reachable = await HostReachability.KeepReachableAsync(_data.ReleaseInfo.Mirrors, mirror => mirror.DownloadUrl);

        foreach (var mirror in reachable)
        {
            DownloadSources.Name(mirror.DownloadUrl, mirror.Name);
        }

        // The release is a single download, so it comes from the chosen mirror or the one the patches found fastest
        var mirrors = DownloadSources.Order(reachable, mirror => mirror.DownloadUrl);

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

        _data.Patches.Clear();

        for (var i = 0; i < _data.PatchChain.Count; i++)
        {
            var result = await DownloadPatchAsync(_data.PatchChain[i], i + 1, _data.PatchChain.Count, progress);

            if (!result.Succeeded)
            {
                return result;
            }
        }

        return await DownloadSPTFromMirrors(progress);
    }
}
