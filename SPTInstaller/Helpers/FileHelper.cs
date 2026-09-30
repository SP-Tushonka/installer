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
        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? path : path.Replace(home, "~", StringComparison.Ordinal);
        }

        var nameMatched = Regex.Match(path, @".:\\[uU]sers\\(?<NAME>[^\\]+)");
        
        if (nameMatched.Success)
        {
            var name = nameMatched.Groups["NAME"].Value;
            return path.Replace(name, "-REDACTED-");
        }
        
        return path;
    }
    
    public static Result CopyDirectoryWithProgress(DirectoryInfo sourceDir, DirectoryInfo targetDir,
        IProgress<double> progress = null, string[] exclusions = null) =>
        CopyDirectoryWithProgress(sourceDir, targetDir, (msg, prog) => progress?.Report(prog), exclusions);
    
    public static Result CopyDirectoryWithProgress(DirectoryInfo sourceDir, DirectoryInfo targetDir,
        Action<string, int> updateCallback = null, string[] exclusions = null)
    {
        try
        {
            var allFiles = sourceDir.GetFiles("*", SearchOption.AllDirectories);
            var fileCopies = new List<CopyInfo>();
            var normalizedExclusions = (exclusions ?? [])
                .Select(exclusion => exclusion.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar))
                .ToArray();
            int count = 0;

            if (allFiles.Length == 0)
            {
                updateCallback?.Invoke("No client files to copy", 100);
                return Result.FromSuccess();
            }
            
            // filter files before starting copy
            foreach (var file in allFiles)
            {
                count++;
                updateCallback?.Invoke("getting list of files to copy", (int)Math.Floor((double)count / allFiles.Length * 100));
                
                var currentFileRelativePath = Path.GetRelativePath(sourceDir.FullName, file.FullName);

                var excluded = normalizedExclusions.Any(exclusion =>
                    currentFileRelativePath.Equals(exclusion, StringComparison.OrdinalIgnoreCase) ||
                    currentFileRelativePath.StartsWith(exclusion + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase));
                if (excluded)
                {
                    Log.Debug("EXCLUSION FOUND :: FILE\nPath: '{Path}'", currentFileRelativePath);
                    continue;
                }

                // don't copy .bak files
                if (currentFileRelativePath.EndsWith(".bak"))
                {
                    Log.Debug($"EXCLUDING BAK FILE :: {currentFileRelativePath}");
                    continue;
                }
                
                fileCopies.Add(new CopyInfo(file.FullName,
                    Path.Combine(targetDir.FullName, currentFileRelativePath)));
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
            new("windows", PathCheckType.Contains, PathCheckAction.Deny),
            new("Drive Root", PathCheckType.DriveRoot, PathCheckAction.Deny)
        };
        
        foreach (var check in problemPaths)
        {
            switch (check.CheckType)
            {
                case PathCheckType.EndsWith:
                    if (path.EndsWith(check.Target, StringComparison.OrdinalIgnoreCase))
                    {
                        failedCheck = check;
                        return true;
                    }
                    
                    break;
                case PathCheckType.Contains:
                    if (path.Contains(check.Target, StringComparison.OrdinalIgnoreCase))
                    {
                        failedCheck = check;
                        return true;
                    }
                    
                    break;
                case PathCheckType.DriveRoot:
                    var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                    var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath) ?? "");
                    var pathComparison = OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;
                    if (fullPath.Equals(root, pathComparison))
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
