using AsmResolver.PE;
using AsmResolver.PE.Win32Resources.Version;
using System.Diagnostics;
using System.Linq;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

public static class PreCheckHelper
{
    public static string? DetectOriginalGamePath() => PlatformOperations.Current.DetectOriginalGamePath();

    public static Result DetectOriginalGameVersion(string gamePath)
    {
        return DetectOriginalGameVersion(
            gamePath,
            path => FileVersionInfo.GetVersionInfo(path).ProductVersion);
    }

    internal static Result DetectOriginalGameVersion(
        string gamePath,
        Func<string, string?> platformVersionReader)
    {
        try
        {
            var executablePath = Path.Combine(gamePath, "EscapeFromTarkov.exe");
            var productVersion = platformVersionReader(executablePath);
            if (string.IsNullOrWhiteSpace(productVersion))
                productVersion = ReadNativePeProductVersion(executablePath);

            if (string.IsNullOrWhiteSpace(productVersion))
                return Result.FromError("EscapeFromTarkov.exe has no product version.");

            var parts = productVersion.Replace('-', '.').Split('.');
            if (parts.Length < 2)
                return Result.FromError($"Could not parse the installed game version: {productVersion}");

            return Result.FromSuccess(parts[^2]);
        }
        catch (Exception ex)
        {
            return Result.FromError($"Could not read EscapeFromTarkov.exe: {ex.Message}");
        }
    }

    private static string? ReadNativePeProductVersion(string executablePath)
    {
        var image = PEImage.FromFile(executablePath);
        if (image.Resources is null) return null;

        foreach (var versionInfo in VersionInfoResource.FindAllFromDirectory(image.Resources))
        {
            var stringFileInfo = versionInfo?
                .GetChildren()
                .OfType<StringFileInfo>()
                .FirstOrDefault();
            if (stringFileInfo is null) continue;

            foreach (var table in stringFileInfo.Tables)
            {
                if (table.TryGetValue(StringTable.ProductVersionKey, out var productVersion) &&
                    !string.IsNullOrWhiteSpace(productVersion))
                {
                    return productVersion;
                }
            }
        }

        return null;
    }
}
