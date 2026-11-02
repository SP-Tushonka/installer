using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Serilog;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

public static class FileHelper
{
    public static string GetRedactedPath(string path)
    {
        var nameMatched = Regex.Match(path, @"(.:\\[uU]sers\\|/home/)(?<NAME>[^\\/]+)");
        
        if (nameMatched.Success)
        {
            var name = nameMatched.Groups["NAME"].Value;
            return path.Replace(name, "-REDACTED-");
        }
        
        return path;
    }
    
    public static Result CopyDirectoryWithProgress(DirectoryInfo sourceDir, DirectoryInfo targetDir,
        Action<string, int> updateCallback = null, string[] exclusions = null)
    {
        try
        {
            var allFiles = sourceDir.GetFiles("*", SearchOption.AllDirectories);
            var fileCopies = new List<CopyInfo>();
            int count = 0;
            
            // filter files before starting copy
            foreach (var file in allFiles)
            {
                count++;
                updateCallback?.Invoke("getting list of files to copy", (int)Math.Floor((double)count / allFiles.Length * 100));
                
                var currentFileRelativePath = file.FullName.Replace(sourceDir.FullName, "");

                if (exclusions != null)
                {
                    // check exclusions
                    foreach (var exclusion in exclusions)
                    {
                        if (currentFileRelativePath.StartsWith(exclusion) || currentFileRelativePath == exclusion)
                        {
                            Log.Debug(
                                $"EXCLUSION FOUND :: FILE\nExclusion: '{exclusion}'\nPath: '{currentFileRelativePath}'");
                            break;
                        }
                    }
                }

                // don't copy .bak files
                if (currentFileRelativePath.EndsWith(".bak"))
                {
                    Log.Debug($"EXCLUDING BAK FILE :: {currentFileRelativePath}");
                    continue;
                }
                
                fileCopies.Add(new CopyInfo(file.FullName, file.FullName.Replace(sourceDir.FullName, targetDir.FullName)));
            }

            count = 0;
            
            // process copy info for files that need to be copied
            foreach (var copyInfo in fileCopies)
            {
                count++;
                updateCallback?.Invoke(copyInfo.FileName, (int)Math.Floor((double)count / fileCopies.Count * 100));
                
                var result = copyInfo.Copy();
                
                if (!result.Succeeded)
                {
                    return result;
                }
            }
            
            return Result.FromSuccess();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during directory copy");
            return Result.FromError(ex.Message);
        }
    }
    
    public static bool StreamAssemblyResourceOut(string resourceName, string outputFilePath)
    {
        try
        {
            Log.Debug($"Starting StreamAssemblyResourceOut, resourcename: {resourceName}, outputFilePath: {outputFilePath}");
            var assembly = Assembly.GetExecutingAssembly();
            
            FileInfo outputFile = new FileInfo(outputFilePath);
            
            if (outputFile.Exists)
            {
                outputFile.Delete();
            }
            
            if (!outputFile.Directory.Exists)
            {
                Directory.CreateDirectory(outputFile.Directory.FullName);
            }
            
            var resName = assembly.GetManifestResourceNames().First(x => x.EndsWith(resourceName));
            
            using (FileStream fs = File.Create(outputFilePath))
            using (Stream s = assembly.GetManifestResourceStream(resName))
            {
                s.CopyTo(fs);
            }
            
            outputFile.Refresh();
            
            return outputFile.Exists;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, $"Failed to stream resource out: {resourceName}");
            return false;
        }
    }

    /// <summary>
    ///     Mark a file as executable. Does nothing on Windows.
    /// </summary>
    /// <param name="path">File to mark</param>
    /// <returns>False when the file could not be marked</returns>
    public static bool MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not mark {path} as executable", path);
            return false;
        }
    }

    /// <summary>
    ///     Check whether a folder can be installed into, by making a folder in the closest parent that exists.
    ///     A file would be the wrong test, as Windows lets users make folders but not files in the root of C:
    /// </summary>
    /// <param name="path">Folder to check, which does not have to exist yet</param>
    /// <returns>False when the folder can't be written to</returns>
    public static bool CanInstallTo(string path)
    {
        try
        {
            var parent = new DirectoryInfo(Path.GetFullPath(path));

            while (parent != null && !parent.Exists)
            {
                parent = parent.Parent;
            }

            if (parent == null)
            {
                return false;
            }

            var probe = Directory.CreateDirectory(Path.Join(parent.FullName, $".spt-write-test-{Guid.NewGuid():N}"));
            probe.Delete();

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "Can't install to {path}", GetRedactedPath(path));
            return false;
        }
    }

    /// <summary>
    ///     Get the drive a path is on, or the mount on Linux. The path does not have to exist yet.
    /// </summary>
    /// <param name="path">Path to look up</param>
    /// <returns>Root of the drive or mount</returns>
    public static string DriveRoot(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DirectoryInfo(path).Root.Name;
        }

        var fullPath = Path.GetFullPath(path);

        return DriveInfo.GetDrives().Select(drive => drive.Name)
            .Where(mount => fullPath == mount || fullPath.StartsWith(mount.TrimEnd('/') + '/', StringComparison.Ordinal))
            .MaxBy(mount => mount.Length) ?? "/";
    }

    /// <summary>
    /// Check if a path is problematic
    /// </summary>
    /// <param name="path">The path the check</param>
    /// <param name="failedCheck">The check that failed</param>
    /// <returns>Returns true if the path is bad, otherwise false</returns>
    public static bool CheckPathForProblemLocations(string path, out PathCheck failedCheck)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        
        failedCheck = new();
        
        var problemPaths = new List<PathCheck>()
        {
            new("SteamApps", PathCheckType.EndsWith, PathCheckAction.Warn),
            new("Documents", PathCheckType.EndsWith, PathCheckAction.Warn),
            new("Desktop", PathCheckType.EndsWith, PathCheckAction.Deny),
            new("Battlestate Games", PathCheckType.Contains, PathCheckAction.Deny),
            new("Desktop", PathCheckType.Contains, PathCheckAction.Warn),
            new("scoped_dir", PathCheckType.Contains, PathCheckAction.Deny),
            new("Downloads", PathCheckType.Contains, PathCheckAction.Deny),
            new("OneDrive", PathCheckType.Contains, PathCheckAction.Deny),
            new("NextCloud", PathCheckType.Contains, PathCheckAction.Deny),
            new("DropBox", PathCheckType.Contains, PathCheckAction.Deny),
            new("Google", PathCheckType.Contains, PathCheckAction.Deny),
            new("Program Files", PathCheckType.Contains, PathCheckAction.Deny),
            new("Program Files (x86", PathCheckType.Contains, PathCheckAction.Deny),
            new(Path.Join("spt-installer", "cache"), PathCheckType.Contains, PathCheckAction.Deny),
            new(DownloadCacheHelper.CachePath, PathCheckType.Contains, PathCheckAction.Deny),
            new("windows", PathCheckType.Contains, PathCheckAction.Deny),
            new("Drive Root", PathCheckType.DriveRoot, PathCheckAction.Deny)
        };
        
        foreach (var check in problemPaths)
        {
            switch (check.CheckType)
            {
                case PathCheckType.EndsWith:
                    if (path.ToLower().EndsWith(check.Target.ToLower()))
                    {
                        failedCheck = check;
                        return true;
                    }
                    
                    break;
                case PathCheckType.Contains:
                    if (path.ToLower().Contains(check.Target.ToLower()))
                    {
                        failedCheck = check;
                        return true;
                    }
                    
                    break;
                case PathCheckType.DriveRoot:
                    if (Regex.Match(path.ToLower(), @"^(\w:(\\|\/)|/)$").Success)
                    {
                        failedCheck = check;
                        return true;
                    }
                    
                    break;
            }
        }
        
        return false;
    }
}