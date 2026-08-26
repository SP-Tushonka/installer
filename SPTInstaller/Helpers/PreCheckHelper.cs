using System.Diagnostics;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

public static class PreCheckHelper
{
    public static string? DetectOriginalGamePath() => PlatformOperations.Current.DetectOriginalGamePath();

    public static Result DetectOriginalGameVersion(string gamePath)
    {
        try
        {
            var productVersion = FileVersionInfo
                .GetVersionInfo(Path.Combine(gamePath, "EscapeFromTarkov.exe"))
                .ProductVersion;
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
}
