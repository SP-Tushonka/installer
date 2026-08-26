using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

public static class ZipHelper
{
    public static Result Decompress(FileInfo archiveFile, DirectoryInfo outputDirectory,
        IProgress<double>? progress = null)
    {
        try
        {
            outputDirectory.Create();

            var outputRoot = Path.GetFullPath(outputDirectory.FullName)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            using var archiveStream = archiveFile.OpenRead();
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var extractionPlan = new List<(ZipArchiveEntry Entry, string Destination, int UnixMode)>();
            var totalBytes = archive.Entries.Sum(entry => entry.Length);
            long extractedBytes = 0;

            foreach (var entry in archive.Entries)
            {
                var normalizedName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var destination = Path.GetFullPath(Path.Combine(outputRoot, normalizedName));

                if (!destination.StartsWith(outputRoot, comparison))
                {
                    return Result.FromError($"Archive entry escapes the install folder: {entry.FullName}");
                }

                if (!destinations.Add(destination))
                {
                    return Result.FromError($"Archive contains a case-colliding or duplicate path: {entry.FullName}");
                }

                var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
                if ((unixMode & 0xF000) == 0xA000)
                {
                    return Result.FromError($"Archive contains an unsupported symbolic link: {entry.FullName}");
                }

                extractionPlan.Add((entry, destination, unixMode));
            }

            foreach (var (entry, destination, unixMode) in extractionPlan)
            {

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using (var input = entry.Open())
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }

                if (OperatingSystem.IsLinux() && (unixMode & 0x49) != 0)
                {
                    File.SetUnixFileMode(destination, (UnixFileMode)(unixMode & 0x1FF));
                }

                extractedBytes += entry.Length;
                progress?.Report(totalBytes == 0 ? 100 : extractedBytes * 100d / totalBytes);
            }

            outputDirectory.Refresh();

            return outputDirectory.Exists
                ? Result.FromSuccess()
                : Result.FromError($"Failed to extract files: {archiveFile.Name}");
        }
        catch (Exception ex)
        {
            return Result.FromError(ex.Message);
        }
    }
}
