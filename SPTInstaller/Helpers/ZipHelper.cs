using System.Diagnostics;
using SevenZip;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

public static class ZipHelper
{
    /// <summary>
    ///     7-Zip file for this platform. SevenZipSharp loads its library through Windows, so Linux runs the 7-Zip program instead.
    /// </summary>
    private static string SevenZipFile => OperatingSystem.IsWindows() ? "7z.dll" : "7zz";

    private static string SevenZipPath => Path.Join(DownloadCacheHelper.CachePath, SevenZipFile);

    /// <summary>
    ///     Write 7-Zip to the cache and make it runnable
    /// </summary>
    /// <returns>False when it could not be prepared</returns>
    public static bool PrepareSevenZip() =>
        FileHelper.StreamAssemblyResourceOut(SevenZipFile, SevenZipPath) && FileHelper.MakeExecutable(SevenZipPath);

    public static Result Decompress(FileInfo archiveFile, DirectoryInfo outputDirectory,
        IProgress<double> progress = null)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                ExtractWithLibrary(archiveFile, outputDirectory, progress);
            }
            else
            {
                var exitCode = ExtractWithProgram(archiveFile, outputDirectory, progress);

                if (exitCode != 0)
                {
                    return Result.FromError($"Failed to extract files: {archiveFile.Name}. 7-Zip exited with {exitCode}");
                }
            }

            outputDirectory.Refresh();

            if (!outputDirectory.Exists)
            {
                return Result.FromError($"Failed to extract files: {archiveFile.Name}");
            }

            return Result.FromSuccess();
        }
        catch (Exception ex)
        {
            return Result.FromError(ex.Message);
        }
    }

    private static void ExtractWithLibrary(FileInfo archiveFile, DirectoryInfo outputDirectory, IProgress<double> progress)
    {
        using var archiveStream = archiveFile.OpenRead();

        SevenZipBase.SetLibraryPath(SevenZipPath);

        var extractor = new SevenZipExtractor(archiveStream);

        extractor.Extracting += (_, args) => { progress.Report(args.PercentDone); };

        extractor.ExtractArchive(outputDirectory.FullName);
    }

    private static int ExtractWithProgram(FileInfo archiveFile, DirectoryInfo outputDirectory, IProgress<double> progress)
    {
        var start = new ProcessStartInfo(SevenZipPath) { RedirectStandardOutput = true, CreateNoWindow = true };

        foreach (var argument in new[] { "x", "-y", "-bso0", "-bse1", "-bsp1", $"-o{outputDirectory.FullName}", archiveFile.FullName })
        {
            start.ArgumentList.Add(argument);
        }

        using var sevenZip = Process.Start(start)!;
        var digits = "";
        int read;

        // Progress overwrites itself with backspaces instead of ending its lines, so it is read per character
        while ((read = sevenZip.StandardOutput.Read()) != -1)
        {
            var character = (char)read;

            if (char.IsDigit(character))
            {
                digits += character;
                continue;
            }

            if (character == '%' && int.TryParse(digits, out var percent))
            {
                progress.Report(percent);
            }

            digits = "";
        }

        sevenZip.WaitForExit();

        return sevenZip.ExitCode;
    }
}
