using System.Collections.Generic;
using System.Linq;
using SharpCompress.Archives;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

public static class ZipHelper
{
    private sealed record ExtractionPlan(
        string Key,
        string Destination,
        bool IsDirectory,
        int UnixMode,
        long Size);

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

            var extractionPlan = BuildExtractionPlan(archiveFile, outputRoot, comparison);
            if (!extractionPlan.Succeeded)
                return Result.FromError(extractionPlan.Error!);

            using var archive = ArchiveFactory.OpenArchive(archiveFile);
            long extractedBytes = 0;

            void ExtractFile(ExtractionPlan plan, Action<Stream> writeEntry)
            {
                if (plan.IsDirectory)
                {
                    Directory.CreateDirectory(plan.Destination);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(plan.Destination)!);
                using (var output = new FileStream(
                           plan.Destination,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    writeEntry(output);
                }

                if (OperatingSystem.IsLinux() && (plan.UnixMode & 0x49) != 0)
                    File.SetUnixFileMode(plan.Destination, (UnixFileMode)(plan.UnixMode & 0x1FF));

                extractedBytes += plan.Size;
                progress?.Report(extractionPlan.TotalBytes == 0
                    ? 100
                    : extractedBytes * 100d / extractionPlan.TotalBytes);
            }

            if (archive.IsSolid)
            {
                using var reader = archive.ExtractAllEntries();
                var planIndex = 0;

                while (reader.MoveToNextEntry())
                {
                    if (planIndex >= extractionPlan.Entries.Count)
                        return Result.FromError("Archive changed between validation and extraction.");

                    var plan = extractionPlan.Entries[planIndex++];
                    if (!string.Equals(reader.Entry.Key, plan.Key, StringComparison.Ordinal))
                        return Result.FromError("Archive changed between validation and extraction.");

                    ExtractFile(plan, reader.WriteEntryTo);
                }

                if (planIndex != extractionPlan.Entries.Count)
                    return Result.FromError("Archive changed between validation and extraction.");
            }
            else
            {
                var archiveEntries = archive.Entries.ToList();
                if (archiveEntries.Count != extractionPlan.Entries.Count)
                    return Result.FromError("Archive changed between validation and extraction.");

                for (var index = 0; index < archiveEntries.Count; index++)
                {
                    var entry = archiveEntries[index];
                    var plan = extractionPlan.Entries[index];
                    if (!string.Equals(entry.Key, plan.Key, StringComparison.Ordinal))
                        return Result.FromError("Archive changed between validation and extraction.");

                    ExtractFile(plan, output =>
                    {
                        using var input = entry.OpenEntryStream();
                        input.CopyTo(output);
                    });
                }
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

    private static (bool Succeeded, string? Error, List<ExtractionPlan> Entries, long TotalBytes)
        BuildExtractionPlan(
            FileInfo archiveFile,
            string outputRoot,
            StringComparison comparison)
    {
        using var archive = ArchiveFactory.OpenArchive(archiveFile);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ExtractionPlan>();
        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            var key = entry.Key;
            if (string.IsNullOrWhiteSpace(key))
                return (false, "Archive contains an entry without a path.", entries, totalBytes);

            var normalizedName = key
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(outputRoot, normalizedName));

            if (!destination.StartsWith(outputRoot, comparison))
                return (false, $"Archive entry escapes the install folder: {key}", entries, totalBytes);

            if (!destinations.Add(destination))
                return (false,
                    $"Archive contains a case-colliding or duplicate path: {key}",
                    entries,
                    totalBytes);

            var attributes = entry.Attrib.GetValueOrDefault();
            var unixMode = (attributes >> 16) & 0xFFFF;
            if (!string.IsNullOrWhiteSpace(entry.LinkTarget) ||
                (unixMode & 0xF000) == 0xA000 ||
                (attributes & (int)FileAttributes.ReparsePoint) != 0)
            {
                return (false, $"Archive contains an unsupported symbolic link: {key}",
                    entries, totalBytes);
            }

            entries.Add(new ExtractionPlan(key, destination, entry.IsDirectory, unixMode, entry.Size));
            totalBytes += entry.Size;
        }

        return (true, null, entries, totalBytes);
    }
}
