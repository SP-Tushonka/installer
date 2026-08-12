using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog;

namespace SPTInstaller.Helpers;

public static class DownloadCacheHelper
{
    internal static readonly HttpClient _httpClient = CreateClient(useProxy: true);

    internal static readonly HttpClient _directHttpClient = CreateClient(useProxy: false);

    private static HttpClient CreateClient(bool useProxy) =>
        new(new SocketsHttpHandler { UseProxy = useProxy }) { Timeout = TimeSpan.FromMinutes(15) };

    private const string VersionMarkerFileName = ".installer-version";

    public static TimeSpan SuggestedTtl = TimeSpan.FromHours(1);
    public static string CachePath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "spt-installer/cache");
    
    private const string PrimaryHost = "https://patcher.sp-tushonka.com";
    private const string FallbackHost = "https://mirror.sp-tushonka.com";

    public static readonly string[] ReleaseUrls = Endpoints("release.json", "SPT_RELEASE_URL");
    public static readonly string[] PatchManifestUrls = Endpoints("mirrors.json", "SPT_MIRRORS_URL");
    public static readonly string[] InstallerUrls = Endpoints("SPTInstaller.exe");
    public static readonly string[] InstallerInfoUrls = Endpoints("installer.json");

    private static readonly Dictionary<string, Task<FileInfo?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    private static string[] Endpoints(string fileName, string? overrideVariable = null)
    {
        var custom = overrideVariable is null ? null : Environment.GetEnvironmentVariable(overrideVariable);
        return string.IsNullOrWhiteSpace(custom)
            ? [$"{PrimaryHost}/{fileName}", $"{FallbackHost}/{fileName}"]
            : [custom];
    }
    
    public static string GetCacheSizeText()
    {
        if (!Directory.Exists(CachePath))
        {
            var message = "No cache folder";
            Log.Information(message);
            return message;
        }
        
        var cacheDir = new DirectoryInfo(CachePath);
        
        var cacheSize = DirectorySizeHelper.GetSizeOfDirectory(cacheDir);
        
        if (cacheSize == -1)
        {
            var message = "An error occurred while getting the cache size :(";
            Log.Error(message);
            return message;
        }
        
        if (cacheSize == 0)
            return "Empty";
        
        return DirectorySizeHelper.SizeSuffix(cacheSize);
    }

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
    /// Removes scratch files left behind by downloads that were killed mid-flight
    /// </summary>
    public static void ClearPartialDownloads()
    {
        if (!Directory.Exists(CachePath))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(CachePath).GetFiles("*.tmp", SearchOption.TopDirectoryOnly))
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

    public static bool ClearMetadataCache()
    {
        if (!Directory.Exists(CachePath))
        {
            return true;
        }

        var metaData = new DirectoryInfo(CachePath).GetFiles("*.json", SearchOption.TopDirectoryOnly);
        var allDeleted = true;

        foreach (var file in metaData)
        {
            file.Delete();
            file.Refresh();

            if (file.Exists)
            {
                allDeleted = false;
            }
        }
        
        return allDeleted;
    }
    
    /// <summary>
    /// Check if a file in the cache already exists
    /// </summary>
    /// <param name="fileName">The name of the file to check for</param>
    /// <param name="expectedHash">The expected hash of the file in the cache</param>
    /// <param name="cachedFile">The file found in the cache; null if no file is found</param>
    /// <returns>True if the file is in the cache and its hash matches the expected hash, otherwise false</returns>
    public static bool CheckCacheHash(string fileName, string expectedHash, out FileInfo cachedFile)
        => CheckCacheHash(new FileInfo(Path.Join(CachePath, fileName)), expectedHash, out cachedFile);
    
    private static bool CheckCacheHash(FileInfo cacheFile, string expectedHash, out FileInfo fileInCache)
    {
        fileInCache = cacheFile;
        
        try
        {
            cacheFile.Refresh();
            Directory.CreateDirectory(CachePath);
            
            if (!cacheFile.Exists || expectedHash == null)
            {
                Log.Information($"{cacheFile.Name} {(cacheFile.Exists ? "is in cache" : "NOT in cache")}");
                Log.Information($"Expected hash: {(expectedHash == null ? "not provided" : expectedHash)}");
                return false;
            }
            
            if (FileHashHelper.CheckHash(cacheFile, expectedHash))
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
    /// Gets a file in the cache based on a time-to-live from its last modified time
    /// </summary>
    /// <param name="fileName">The name of the file to look for in the cache</param>
    /// <param name="ttl">The time-to-live to check against</param>
    /// <param name="cachedFile">The file found in the cache if it exists</param>
    /// <returns>Returns true if the file was found in the cache, otherwise false</returns>
    public static bool CheckCacheTTL(string fileName, TimeSpan ttl, out FileInfo cachedFile) =>
        CheckCacheTTL(new FileInfo(Path.Join(CachePath, fileName)), ttl, out cachedFile);

    private static bool CheckCacheTTL(FileInfo cacheFile, TimeSpan ttl, out FileInfo fileInCache)
    {
        fileInCache = cacheFile;
        
        try
        {
            cacheFile.Refresh();
            Directory.CreateDirectory(CachePath);
            
            if (!cacheFile.Exists)
            {
                Log.Information($"{cacheFile.Name} {(cacheFile.Exists ? "is in cache" : "NOT in cache")}");
                return false;
            }
            
            if (cacheFile.Length == 0)
            {
                Log.Warning($"{cacheFile.Name} is empty, discarding it");
                cacheFile.Delete();

                return false;
            }

            var validTimeToLive = cacheFile.LastWriteTime.Add(ttl) > DateTime.Now;

            Log.Information($"{cacheFile.Name} TTL is {(validTimeToLive ? "OK" : "INVALID")}");
            
            return validTimeToLive;
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

        List<(string Url, HttpClient Client, bool ViaProxy)> attempts = [];

        foreach (var link in targetLinks)
        {
            attempts.Add((link, _httpClient, true));
            attempts.Add((link, _directHttpClient, false));
        }

        try
        {
            for (var attempt = 0; attempt < attempts.Count; attempt++)
            {
                var (url, client, viaProxy) = attempts[attempt];
                var mode = viaProxy ? "system proxy" : "proxy bypassed";

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

    // Only the HTTP stack's own failures are worth a proxy-less retry; a local file error is not
    private static bool IsTransportFailure(Exception exception)
        => exception is HttpRequestException or TaskCanceledException;

    /// <summary>
    /// Get or download a file using a time to live
    /// </summary>
    /// <param name="fileName">The file to get from cache</param>
    /// <param name="targetLinks">The links to use for the download, tried in order</param>
    /// <param name="progress">A progress object for reporting download progress</param>
    /// <param name="timeToLive">The time-to-live to check against in the cache</param>
    /// <returns></returns>
    public static async Task<FileInfo?> GetOrDownloadFileAsync(string fileName, IReadOnlyList<string> targetLinks,
        IProgress<double>? progress, TimeSpan timeToLive)
    {
        try
        {
            if (CheckCacheTTL(fileName, timeToLive, out FileInfo cachedFile))
            {
                return cachedFile;
            }

            Log.Information($"Downloading File: {targetLinks[0]}");
            return await DownloadFileAsync(fileName, targetLinks, progress);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error while getting file: {fileName}");
            return null;
        }
    }
    
    /// <summary>
    /// Get the file from cache or download it
    /// </summary>
    /// <param name="fileName">The name of the file to check for in the cache</param>
    /// <param name="targetLink">The url to download from if the file doesn't exist in the cache</param>
    /// <param name="progress">A provider for progress updates</param>
    /// <param name="expectedHash">The expected hash of the cached file</param>
    /// <returns>A <see cref="FileInfo"/> object of the cached file</returns>
    /// <remarks>Use <see cref="DownloadFileAsync(string, IReadOnlyList{string}, IProgress{double})"/> if you don't have an expected cache file hash</remarks>
    public static async Task<FileInfo?> GetOrDownloadFileAsync(string fileName, string targetLink,
        IProgress<double> progress, string expectedHash)
    {
        try
        {
            if (CheckCacheHash(fileName, expectedHash, out var cacheFile))
                return cacheFile;

            Log.Information($"Downloading File: {targetLink}");
            return await DownloadFileAsync(fileName, [targetLink], progress);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error while getting file: {fileName}");
            return null;
        }
    }
}