using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SPTInstaller.Helpers;

public static class DownloadCacheHelper
{
    // Declared before Routes because static fields initialise in order
    internal static readonly ProductInfoHeaderValue UserAgent =
        new("SPTInstaller", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown");

    internal static readonly (string Name, HttpClient Client)[] Routes =
    [
        ("public DNS", CreateClient(new SocketsHttpHandler { UseProxy = false, ConnectCallback = PublicDns.ConnectAsync })),
        ("system proxy", CreateClient(new SocketsHttpHandler())),
        ("proxy bypassed", CreateClient(new SocketsHttpHandler { UseProxy = false })),
    ];

    internal static HttpClient CreateClient(SocketsHttpHandler handler, TimeSpan? timeout = null)
    {
        var client = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
        return client;
    }

    private const string VersionMarkerFileName = ".installer-version";

    private static readonly TimeSpan ResumableLifetime = TimeSpan.FromDays(7);

    public static string CachePath = OperatingSystem.IsWindows()
        ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "spt-installer", "cache")
        : Path.Join(LinuxCacheFolder(), "spt-installer");

    private static string LinuxCacheFolder()
    {
        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");

        return string.IsNullOrWhiteSpace(cacheHome)
            ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
            : cacheHome;
    }

    // Where patches and the SPT zip are saved. The free space check moves it into the install folder when the cache drive is short.
    public static string DownloadPath = CachePath;

    public static bool DownloadsInInstallFolder => !string.Equals(DownloadPath, CachePath, StringComparison.OrdinalIgnoreCase);

    public static string InstallFolderDownloads(string installPath) => Path.Join(installPath, ".installer-downloads");

    /// <summary>
    /// Deletes downloads kept in the install folder. They are only kept there until the install finishes.
    /// </summary>
    public static void RemoveInstallFolderDownloads()
    {
        if (!DownloadsInInstallFolder || !Directory.Exists(DownloadPath))
        {
            return;
        }

        try
        {
            Directory.Delete(DownloadPath, true);
            Log.Information("Removed downloads from the install folder: {path}", DownloadPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not remove downloads from the install folder: {path}", DownloadPath);
        }
    }

    /// <summary>
    /// The size a host reports for a file without downloading it, tried over each route in turn
    /// </summary>
    public static async Task<long?> GetRemoteSizeAsync(string url)
    {
        foreach (var (mode, client) in Routes)
        {
            try
            {
                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await client.SendAsync(request, timeout.Token);

                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
                {
                    return response.Content.Headers.ContentLength.Value;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not get the size of {url} ({mode})", url, mode);
            }
        }

        return null;
    }

    public static string PatchDocumentFileName(int sourceClientVersion, int targetClientVersion)
        => $"patch-{sourceClientVersion}-{targetClientVersion}.json";

    private const string PrimaryHost = "https://patcher.sp-tushonka.com";
    private const string FallbackHost = "https://mirror.sp-tushonka.com";

    public static readonly string[] ReleaseUrls = Endpoints("release.json", "SPT_RELEASE_URL");
    public static readonly string[] PatchManifestUrls = Endpoints("mirrors.json", "SPT_MIRRORS_URL");
    public static string InstallerFileName => OperatingSystem.IsWindows() ? "SPTInstaller.exe" : "SPTInstaller";
    public static readonly string[] InstallerUrls = Endpoints(InstallerFileName, "SPT_INSTALLER_URL");
    public static readonly string[] InstallerInfoUrls = Endpoints("installer.json", "SPT_INSTALLER_INFO_URL");

    private static readonly Dictionary<string, Task<FileInfo?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    private static string[] Endpoints(string fileName, string? overrideVariable = null)
    {
        var custom = overrideVariable is null ? null : Environment.GetEnvironmentVariable(overrideVariable);
        return string.IsNullOrWhiteSpace(custom)
            ? [$"{PrimaryHost}/{fileName}", $"{FallbackHost}/{fileName}"]
            : [custom];
    }
    
    /// <summary>
    ///     Get the size of the cached downloads
    /// </summary>
    /// <returns>Size in bytes, -1 when the cache can't be read</returns>
    public static long GetCacheSize()
    {
        if (!Directory.Exists(CachePath))
        {
            return 0;
        }

        var cacheSize = DirectorySizeHelper.GetSizeOfDirectory(new DirectoryInfo(CachePath));

        if (cacheSize == -1)
        {
            Log.Error("An error occurred while getting the cache size");
            return -1;
        }

        // The version marker is always there, so it doesn't count as cached downloads
        var marker = new FileInfo(Path.Join(CachePath, VersionMarkerFileName));
        return marker.Exists ? cacheSize - marker.Length : cacheSize;
    }

    public static string CacheSizeText(long size) => size switch
    {
        -1 => "Size unknown",
        0 => "Empty",
        _ => DirectorySizeHelper.SizeSuffix(size)
    };

    public static string GetCacheSizeText() => CacheSizeText(GetCacheSize());

    public static void ClearCacheOnVersionChange(string version)
    {
        try
        {
            Directory.CreateDirectory(CachePath);

            var marker = new FileInfo(Path.Join(CachePath, VersionMarkerFileName));

            if (marker.Exists && File.ReadAllText(marker.FullName).Trim() == version)
            {
                return;
            }

            Log.Information("Cache was written by a different installer version, clearing it");

            Directory.Delete(CachePath, true);
            Directory.CreateDirectory(CachePath);

            File.WriteAllText(Path.Join(CachePath, VersionMarkerFileName), version);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not reconcile the cache with the installer version");
        }
    }

    /// <summary>
    /// Removes scratch files left behind by downloads that were killed mid-flight, and resumable ones gone stale
    /// </summary>
    public static void ClearPartialDownloads()
    {
        if (!Directory.Exists(CachePath))
        {
            return;
        }

        var cache = new DirectoryInfo(CachePath);

        // Resumable parts outlive a version switch so switching back loses nothing, an abandoned one is dropped after a week
        var stale = cache.GetFiles("*.tmp", SearchOption.TopDirectoryOnly)
            .Concat(cache.GetFiles("*.part", SearchOption.TopDirectoryOnly)
                .Where(file => DateTime.Now - file.LastWriteTime > ResumableLifetime));

        foreach (var file in stale)
        {
            try
            {
                var size = file.Length;

                file.Delete();

                Log.Information("Removed partial download: {name} ({size})", file.Name,
                    DirectorySizeHelper.SizeSuffix(size));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not remove partial download: {name}", file.Name);
            }
        }
    }

    /// <summary>
    /// Deletes every download and metadata file, keeping only the marker that ties the cache to this installer version
    /// </summary>
    /// <returns>False when something could not be removed, for example a file still open elsewhere</returns>
    public static bool ClearCache()
    {
        if (!Directory.Exists(CachePath))
        {
            return true;
        }

        var allDeleted = true;
        var cache = new DirectoryInfo(CachePath);

        foreach (var entry in cache.GetFileSystemInfos().Where(entry => entry.Name != VersionMarkerFileName))
        {
            try
            {
                if (entry is DirectoryInfo directory)
                {
                    directory.Delete(true);
                }
                else
                {
                    entry.Delete();
                }
            }
            catch (Exception ex)
            {
                allDeleted = false;
                Log.Warning(ex, "Could not remove {name} from the cache", entry.Name);
            }
        }

        Log.Information("Cache cleared{result}", allDeleted ? "" : ", some files were in use");

        return allDeleted;
    }

    /// <summary>
    /// Check if a file in the cache already exists
    /// </summary>
    /// <param name="fileName">The name of the file to check for</param>
    /// <param name="spec">The hash the file in the cache must match</param>
    /// <param name="cachedFile">The file found in the cache; null if no file is found</param>
    /// <returns>True if the file is in the cache and its hash matches the expected hash, otherwise false</returns>
    public static bool CheckCacheHash(string fileName, DownloadSpec spec, out FileInfo cachedFile)
    {
        if (CheckCacheHash(new FileInfo(Path.Join(DownloadPath, fileName)), spec, out cachedFile))
        {
            return true;
        }

        // A copy left in the shared cache by an earlier install is still usable
        return DownloadsInInstallFolder && CheckCacheHash(new FileInfo(Path.Join(CachePath, fileName)), spec, out cachedFile);
    }
    
    private static bool CheckCacheHash(FileInfo cacheFile, DownloadSpec spec, out FileInfo fileInCache)
    {
        fileInCache = cacheFile;
        
        try
        {
            cacheFile.Refresh();
            Directory.CreateDirectory(CachePath);
            
            if (!cacheFile.Exists)
            {
                Log.Information($"{cacheFile.Name} NOT in cache");
                Log.Information($"Expected {spec.Algorithm.Name} hash: {Convert.ToHexStringLower(spec.Digest)}");
                return false;
            }
            
            if (FileHashHelper.CheckHash(cacheFile, spec.Algorithm, spec.Digest))
            {
                fileInCache = cacheFile;
                Log.Information("Hashes MATCH");
                return true;
            }
            
            Log.Warning("Hashes DO NOT MATCH");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Something went wrong during hashing");
            return false;
        }
    }

    /// <summary>
    /// Download a file to the cache folder
    /// </summary>
    /// <param name="outputFileName">The file name to save the file as</param>
    /// <param name="targetLinks">The urls to download the file from, tried in order</param>
    /// <param name="progress">A provider for progress updates</param>
    /// <returns>A <see cref="FileInfo"/> object of the cached file</returns>
    /// <remarks>The cached file is only replaced once the download completes, so a failure leaves it intact</remarks>
    public static async Task<FileInfo?> DownloadFileAsync(string outputFileName, IReadOnlyList<string> targetLinks,
        IProgress<double>? progress)
    {
        Task<FileInfo?> download;

        // Some callers ask for the same file, use locking here
        lock (_inFlight)
        {
            if (!_inFlight.TryGetValue(outputFileName, out var existing))
            {
                existing = DownloadCoreAsync(outputFileName, targetLinks, progress);
                _inFlight[outputFileName] = existing;
            }

            download = existing;
        }

        try
        {
            return await download;
        }
        finally
        {
            lock (_inFlight)
            {
                // Only if it is still ours, so a download started since is left to finish
                if (_inFlight.TryGetValue(outputFileName, out var current) && current == download)
                {
                    _inFlight.Remove(outputFileName);
                }
            }
        }
    }

    private static async Task<FileInfo?> DownloadCoreAsync(string outputFileName, IReadOnlyList<string> targetLinks,
        IProgress<double> progress)
    {
        Directory.CreateDirectory(CachePath);

        var outputFile = new FileInfo(Path.Join(CachePath, outputFileName));

        var tempFile = new FileInfo($"{outputFile.FullName}.{Guid.NewGuid():N}.tmp");

        List<(string Url, string Mode, HttpClient Client)> attempts = [];

        foreach (var link in targetLinks)
        {
            foreach (var (mode, client) in Routes)
            {
                attempts.Add((link, mode, client));
            }
        }

        try
        {
            for (var attempt = 0; attempt < attempts.Count; attempt++)
            {
                var (url, mode, client) = attempts[attempt];

                try
                {
                    using (var file = tempFile.Open(FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        if (!await client.DownloadDataAsync(url, file, progress))
                        {
                            Log.Error("Download incomplete ({mode}): {url}", mode, url);
                            continue;
                        }
                    }

                    tempFile.Refresh();

                    if (tempFile.Length == 0)
                    {
                        Log.Error("Download was empty ({mode}): {url}", mode, url);
                        continue;
                    }

                    File.Move(tempFile.FullName, outputFile.FullName, true);
                    outputFile.Refresh();

                    if (!outputFile.Exists)
                    {
                        Log.Error("Failed to download file from url: {name} :: {url}", outputFileName, url);
                        return null;
                    }

                    return outputFile;
                }
                catch (Exception ex) when (attempt < attempts.Count - 1 && IsTransportFailure(ex))
                {
                    Log.Warning(ex, "Download failed ({mode}), trying the next route: {url}", mode, url);
                }
            }

            Log.Error("Failed to download file from any url: {name} :: {urls}", outputFileName,
                string.Join(", ", targetLinks));
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download file from url: {name} :: {urls}", outputFileName,
                string.Join(", ", targetLinks));
            return null;
        }
        finally
        {
            try
            {
                tempFile.Refresh();

                if (tempFile.Exists)
                {
                    tempFile.Delete();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not clean up partial download: {name}", tempFile.Name);
            }
        }
    }

    // Only the HTTP stack's own failures are worth retrying over another route. A local file error is not.
    // HttpIOException is a connection dropped mid-body, and a TLS record failing to decrypt is an IOException over a Win32Exception.
    private static bool IsTransportFailure(Exception exception)
        => exception is HttpRequestException or HttpIOException or TaskCanceledException
           || (exception is IOException && exception.InnerException is System.ComponentModel.Win32Exception);

    /// <summary>
    /// Fetches a metadata file such as release.json, tried over each url and route in turn. It is never cached,
    /// because a regenerated patch changes it and a stale copy fails every hash.
    /// </summary>
    public static async Task<string?> GetMetadataAsync(string fileName, IReadOnlyList<string> targetLinks)
    {
        foreach (var url in targetLinks)
        {
            foreach (var (mode, client) in Routes)
            {
                try
                {
                    using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var text = await client.GetStringAsync(url, timeout.Token);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }

                    Log.Warning("{name} was empty ({mode}): {url}", fileName, mode, url);
                }
                catch (Exception ex)
                {
                    Log.Warning("Could not fetch {name} ({mode}): {url} :: {message}", fileName, mode, url, ex.Message);
                }
            }
        }

        Log.Error("Could not fetch {name} from any url: {urls}", fileName, string.Join(", ", targetLinks));
        return null;
    }

    /// <summary>
    /// Get the file from cache or download and verify it
    /// </summary>
    /// <param name="fileName">The name of the file to check for in the cache</param>
    /// <param name="targetLinks">The urls to download from if the file doesn't exist in the cache</param>
    /// <param name="spec">The hashes the file must match</param>
    /// <param name="progress">A provider for progress updates</param>
    /// <param name="status">Receives what the download is doing, for the status line</param>
    public static async Task<DownloadResult> GetOrDownloadFileAsync(string fileName, IReadOnlyList<string> targetLinks,
        DownloadSpec spec, IProgress<double> progress, Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (CheckCacheHash(fileName, spec, out var cacheFile))
        {
            return new DownloadResult(DownloadOutcome.Downloaded, cacheFile);
        }

        Log.Information($"Downloading File: {targetLinks[0]}");
        return await VerifiedDownloader.DownloadAsync(fileName, targetLinks, spec, progress, status, cancellationToken);
    }
}