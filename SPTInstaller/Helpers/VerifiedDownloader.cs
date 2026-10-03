using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace SPTInstaller.Helpers;

public sealed record DownloadSpec(
    HashAlgorithmName Algorithm,
    byte[] Digest,
    long? Size = null,
    int? ChunkSize = null,
    IReadOnlyList<byte[]>? ChunkDigests = null)
{
    public bool IsChunked => Size > 0 && ChunkSize > 0
                             && ChunkDigests?.Count == (int)((Size.Value + ChunkSize.Value - 1) / ChunkSize.Value);

    /// <param name="sha256">Hex SHA-256 of the whole file, preferred when published</param>
    /// <param name="md5">Base64 MD5, which older manifests carry alone</param>
    /// <param name="chunkHashes">Hex SHA-256 of each <paramref name="chunkSize"/> slice, the last one may be shorter</param>
    public static DownloadSpec From(string? sha256, string md5, long? size = null, int? chunkSize = null,
        IReadOnlyList<string>? chunkHashes = null)
    {
        if (string.IsNullOrWhiteSpace(sha256))
        {
            return new DownloadSpec(HashAlgorithmName.MD5, Convert.FromBase64String(md5));
        }

        return new DownloadSpec(HashAlgorithmName.SHA256, Convert.FromHexString(sha256), size, chunkSize,
            chunkHashes?.Select(Convert.FromHexString).ToList());
    }
}

public enum DownloadOutcome
{
    Downloaded,
    Corrupted,
    Failed,
}

public record DownloadResult(DownloadOutcome Outcome, FileInfo? File = null);

/// <summary>
/// Downloads large files into the cache, resuming across runs and verifying what lands on disk
/// </summary>
public static class VerifiedDownloader
{
    private const int Parallelism = 4;
    private const int AttemptsPerChunk = 6;
    private const int VerifyRounds = 3;
    private const int BufferSize = 1024 * 1024;

    private static int _preferredRoute;

    private sealed class ChunkCorruptedException(int index) : Exception($"Chunk {index} kept failing its hash");

    /// <summary>
    /// Reports progress on every byte count and a "3.2 GB of 7.6 GB · 18.4 MB/s · about 4 min left" line at most once a second
    /// </summary>
    private sealed class TransferMeter(long total, long done, string? prefix, IProgress<double>? progress, Action<string>? status)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly long _startDone = done;
        private long _done = done;
        private long _lastReport;

        public void Add(long bytes)
        {
            var now = Interlocked.Add(ref _done, bytes);
            progress?.Report(100.0 * now / total);

            var elapsed = _clock.ElapsedMilliseconds;
            var last = Interlocked.Read(ref _lastReport);

            if (status == null || elapsed - last < 1000 || Interlocked.CompareExchange(ref _lastReport, elapsed, last) != last)
            {
                return;
            }

            var speed = (now - _startDone) / (elapsed / 1000.0);
            var text = $"{prefix}{DirectorySizeHelper.SizeSuffix(now)} of {DirectorySizeHelper.SizeSuffix(total)}";

            if (speed > 0)
            {
                text += $" · {DirectorySizeHelper.SizeSuffix((long)speed)}/s · {TimeLeft((total - now) / speed)}";
            }

            status(text);
        }

        private static string TimeLeft(double seconds) => seconds switch
        {
            < 60 => "under a minute left",
            < 3600 => $"about {Math.Ceiling(seconds / 60)} min left",
            _ => $"about {(int)(seconds / 3600)} h {(int)(seconds % 3600 / 60)} min left",
        };
    }

    public static async Task<DownloadResult> DownloadAsync(string fileName, IReadOnlyList<string> urls, DownloadSpec spec,
        IProgress<double>? progress, Action<string>? status = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DownloadCacheHelper.CachePath);

        var output = new FileInfo(Path.Join(DownloadCacheHelper.CachePath, fileName));

        // The hash is in the name so a part file is never resumed against a different upload
        var part = new FileInfo($"{output.FullName}.{Convert.ToHexString(spec.Digest)[..12]}.part");

        foreach (var stale in output.Directory!.GetFiles($"{output.Name}.*.part").Where(file => file.Name != part.Name))
        {
            stale.Delete();
        }

        try
        {
            var outcome = spec.IsChunked
                ? await DownloadChunkedAsync(part, urls, spec, progress, status, cancellationToken)
                : await DownloadStreamAsync(part, urls, spec, progress, status, cancellationToken);

            if (outcome == DownloadOutcome.Corrupted)
            {
                part.Delete();
            }

            if (outcome != DownloadOutcome.Downloaded)
            {
                return new DownloadResult(outcome);
            }

            File.Move(part.FullName, output.FullName, true);
            output.Refresh();

            return new DownloadResult(DownloadOutcome.Downloaded, output);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download {name} :: {urls}", fileName, string.Join(", ", urls));
            return new DownloadResult(DownloadOutcome.Failed);
        }
    }

    private static async Task<DownloadOutcome> DownloadChunkedAsync(FileInfo part, IReadOnlyList<string> urls,
        DownloadSpec spec, IProgress<double>? progress, Action<string>? status, CancellationToken cancellationToken)
    {
        var size = spec.Size!.Value;
        var resuming = part.Exists && part.Length == size;

        using var handle = File.OpenHandle(part.FullName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            FileOptions.Asynchronous);

        List<int> pending;

        if (resuming)
        {
            status?.Invoke("Checking the partial download ...");
            pending = await FindBadChunksAsync(handle, spec, null, progress, cancellationToken);
            Log.Information("Resuming {name}, {done} of {count} chunks already downloaded", part.Name,
                spec.ChunkDigests!.Count - pending.Count, spec.ChunkDigests.Count);
            status?.Invoke($"Resuming: {spec.ChunkDigests.Count - pending.Count} of {spec.ChunkDigests.Count} chunks already downloaded");
        }
        else
        {
            RandomAccess.SetLength(handle, size);
            pending = [.. Enumerable.Range(0, spec.ChunkDigests!.Count)];
        }

        for (var round = 1;; round++)
        {
            if (pending.Count > 0)
            {
                var prefix = round > 1 ? $"Re-fetching {pending.Count} damaged chunk(s): " : null;

                try
                {
                    await FetchChunksAsync(handle, urls, spec, pending, progress, status, prefix, cancellationToken);
                }
                catch (ChunkCorruptedException ex)
                {
                    Log.Error(ex.Message);
                    return DownloadOutcome.Corrupted;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Log.Error(ex, "Download failed, the partial file is kept to resume later");
                    return DownloadOutcome.Failed;
                }
            }

            // Reading back from disk catches damage that happened after a chunk was checked in memory
            status?.Invoke("Verifying download ...");
            using var whole = IncrementalHash.CreateHash(spec.Algorithm);
            pending = await FindBadChunksAsync(handle, spec, whole, progress, cancellationToken);

            if (pending.Count == 0)
            {
                if (whole.GetHashAndReset().AsSpan().SequenceEqual(spec.Digest))
                {
                    return DownloadOutcome.Downloaded;
                }

                Log.Error("Every chunk matched but the whole file did not, the published hashes disagree");
                return DownloadOutcome.Corrupted;
            }

            Log.Warning("{count} chunk(s) were damaged on disk after downloading (round {round})", pending.Count, round);

            if (round == VerifyRounds)
            {
                return DownloadOutcome.Corrupted;
            }
        }
    }

    private static async Task<List<int>> FindBadChunksAsync(SafeFileHandle handle, DownloadSpec spec, IncrementalHash? whole,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var size = spec.Size!.Value;
        var chunkSize = spec.ChunkSize!.Value;
        var buffer = new byte[BufferSize];
        using var chunkHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        List<int> bad = [];

        for (var index = 0; index < spec.ChunkDigests!.Count; index++)
        {
            var offset = (long)index * chunkSize;
            var end = Math.Min(offset + chunkSize, size);

            while (offset < end)
            {
                var read = await RandomAccess.ReadAsync(handle, buffer.AsMemory(0, (int)Math.Min(BufferSize, end - offset)),
                    offset, cancellationToken);

                if (read == 0)
                {
                    break;
                }

                chunkHash.AppendData(buffer, 0, read);
                whole?.AppendData(buffer, 0, read);
                offset += read;
            }

            if (!chunkHash.GetHashAndReset().AsSpan().SequenceEqual(spec.ChunkDigests[index]))
            {
                bad.Add(index);
            }

            progress?.Report(100.0 * end / size);
        }

        return bad;
    }

    private static async Task FetchChunksAsync(SafeFileHandle handle, IReadOnlyList<string> urls, DownloadSpec spec,
        List<int> pending, IProgress<double>? progress, Action<string>? status, string? prefix,
        CancellationToken cancellationToken)
    {
        var size = spec.Size!.Value;
        var chunkSize = spec.ChunkSize!.Value;
        var received = size - pending.Sum(index => Math.Min(chunkSize, size - (long)index * chunkSize));
        var meter = new TransferMeter(size, received, prefix, progress, status);

        await Parallel.ForEachAsync(pending,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            async (index, token) => await FetchChunkAsync(handle, urls, spec, index, meter.Add, token));
    }

    private static async Task FetchChunkAsync(SafeFileHandle handle, IReadOnlyList<string> urls, DownloadSpec spec,
        int index, Action<long> count, CancellationToken cancellationToken)
    {
        var routes = DownloadCacheHelper.Routes;
        var start = (long)index * spec.ChunkSize!.Value;
        var length = Math.Min(spec.ChunkSize.Value, spec.Size!.Value - start);
        var buffer = new byte[BufferSize];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        for (var attempt = 0;; attempt++)
        {
            var routeIndex = (Volatile.Read(ref _preferredRoute) + attempt) % (urls.Count * routes.Length);
            var url = urls[routeIndex / routes.Length];
            var (mode, client) = routes[routeIndex % routes.Length];
            long written = 0;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(start, start + length - 1);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != start)
                {
                    throw new HttpRequestException($"{url} did not honour the range request");
                }

                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);

                while (written < length)
                {
                    var read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(BufferSize, length - written)),
                        cancellationToken);

                    if (read == 0)
                    {
                        throw new IOException($"Chunk {index} ended after {written} of {length} bytes");
                    }

                    hash.AppendData(buffer, 0, read);
                    await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), start + written, cancellationToken);
                    written += read;
                    count(read);
                }

                if (hash.GetHashAndReset().AsSpan().SequenceEqual(spec.ChunkDigests![index]))
                {
                    Volatile.Write(ref _preferredRoute, routeIndex);
                    return;
                }

                Log.Warning("Chunk {index} failed its hash ({mode}): {url}", index, mode, url);
                count(-written);

                if (attempt == AttemptsPerChunk - 1)
                {
                    throw new ChunkCorruptedException(index);
                }
            }
            catch (Exception ex) when (ex is not ChunkCorruptedException && attempt < AttemptsPerChunk - 1
                                       && !cancellationToken.IsCancellationRequested)
            {
                Log.Warning("Chunk {index} failed ({mode}), retrying: {message}", index, mode, ex.Message);
                hash.GetHashAndReset();
                count(-written);
            }
        }
    }

    private static async Task<DownloadOutcome> DownloadStreamAsync(FileInfo part, IReadOnlyList<string> urls,
        DownloadSpec spec, IProgress<double>? progress, Action<string>? status, CancellationToken cancellationToken)
    {
        var routes = DownloadCacheHelper.Routes;
        var attempts = urls.SelectMany(url => routes.Select(route => (Url: url, route.Name, route.Client))).ToList();
        var buffer = new byte[BufferSize];

        for (var attempt = 0; attempt < attempts.Count; attempt++)
        {
            var (url, mode, client) = attempts[attempt];

            try
            {
                await using var file = new FileStream(part.FullName, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, BufferSize, true);
                using var hash = IncrementalHash.CreateHash(spec.Algorithm);

                // A resumed download is still hashed end to end, so the bytes already on disk go in first
                int read;
                while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (file.Length > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(file.Length, null);
                }

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    file.SetLength(0);
                    throw new IOException("The partial download is longer than the file, starting over");
                }

                response.EnsureSuccessStatusCode();

                if (file.Length > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    Log.Information("{url} does not resume downloads, starting over", url);
                    file.SetLength(0);
                    hash.GetHashAndReset();
                }

                var existing = file.Length;

                if (existing > 0)
                {
                    Log.Information("Resuming {name} from {size}", part.Name, DirectorySizeHelper.SizeSuffix(existing));
                }

                var remaining = response.Content.Headers.ContentLength;
                var meter = remaining.HasValue ? new TransferMeter(existing + remaining.Value, existing, null, progress, status) : null;
                long copied = 0;

                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);

                while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    copied += read;
                    meter?.Add(read);
                }

                if (remaining.HasValue && copied != remaining)
                {
                    throw new IOException($"Download ended after {copied} of {remaining} bytes");
                }

                if (hash.GetHashAndReset().AsSpan().SequenceEqual(spec.Digest))
                {
                    return DownloadOutcome.Downloaded;
                }

                Log.Error("{name} failed verification ({mode}): {url}", part.Name, mode, url);
                return DownloadOutcome.Corrupted;
            }
            catch (Exception ex) when (attempt < attempts.Count - 1 && !cancellationToken.IsCancellationRequested)
            {
                Log.Warning("Download failed ({mode}), resuming over the next route: {message}", mode, ex.Message);
            }
        }

        return DownloadOutcome.Failed;
    }
}
