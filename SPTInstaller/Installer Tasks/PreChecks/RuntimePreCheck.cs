using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;
using SPTInstaller.Helpers;
using SPTInstaller.Models;
using SPTInstaller.Models.ReleaseInfo;

namespace SPTInstaller.Installer_Tasks.PreChecks;

public class RuntimePreCheck(RuntimeRequirement requirement) : PreCheckBase(requirement.DisplayName, true)
{
    // Used for releases that do not list their runtimes
    public static readonly RuntimeRequirement[] Defaults =
    [
        new()
        {
            DisplayName = ".Net 10 Desktop Runtime",
            Identifier = "Microsoft.WindowsDesktop.App",
            MinimumVersion = "10.0.9",
            DownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.9-windows-x64-installer",
        },
        new()
        {
            DisplayName = "ASP.Net Core 10 Runtime",
            Identifier = "Microsoft.AspNetCore.App",
            MinimumVersion = "10.0.9",
            DownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-aspnetcore-10.0.9-windows-x64-installer",
        },
    ];

    public RuntimeRequirement Requirement => requirement;

    /// <summary>
    ///     Get what a listed runtime means on this platform. The Linux binaries use the base runtime instead of the desktop one.
    /// </summary>
    /// <param name="requirement">Runtime as listed by the release, for the Windows binaries</param>
    /// <returns>The runtime to check for</returns>
    public static RuntimeRequirement ForThisPlatform(RuntimeRequirement requirement)
    {
        if (OperatingSystem.IsWindows() || requirement.Identifier != "Microsoft.WindowsDesktop.App")
        {
            return requirement;
        }

        return new RuntimeRequirement
        {
            DisplayName = requirement.DisplayName.Replace(" Desktop", ""),
            Identifier = "Microsoft.NETCore.App",
            MinimumVersion = requirement.MinimumVersion,
            DownloadUrl = requirement.DownloadUrl,
        };
    }

    /// <summary>
    ///     Download page for the runtime. Releases only list the Windows installers.
    /// </summary>
    private string DownloadUrl => OperatingSystem.IsWindows()
        ? requirement.DownloadUrl
        : $"https://dotnet.microsoft.com/en-us/download/dotnet/{ParseVersion(requirement.MinimumVersion).Major}.0";

    private static string DotnetPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return $@"{Environment.ExpandEnvironmentVariables("%ProgramW6432%")}\dotnet\dotnet.exe";
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        return string.IsNullOrEmpty(dotnetRoot) ? "dotnet" : Path.Join(dotnetRoot, "dotnet");
    }

    public override async Task<PreCheckResult> CheckOperation()
    {
        string[] output;

        try
        {
            var result = ProcessHelper.RunAndReadProcessOutputs(DotnetPath(), "--list-runtimes");

            if (!result.Succeeded)
            {
                return PreCheckResult.FromError(
                    $"{requirement.DisplayName} could not be detected.\n\nYou most likely don't have it installed.",
                    $"Download {requirement.DisplayName}",
                    () => ProcessHelper.OpenUrl(DownloadUrl)
                );
            }

            output = result.StdOut.Split(Environment.NewLine);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"PreCheck::{Name}::Exception");
            return PreCheckResult.FromException(ex);
        }

        var minimum = ParseVersion(requirement.MinimumVersion);
        var highest = HighestInstalled(output, requirement.Identifier, minimum.Major);

        if (highest >= minimum)
        {
            return PreCheckResult.FromSuccess($"{requirement.DisplayName} {highest} is installed");
        }

        return PreCheckResult.FromError(
            $"{requirement.DisplayName} {minimum} or higher is required.\n\n"
                + $"Highest Version Found: {(highest > new Version("0.0.0") ? highest.ToString() : "Not Found")}\n\n"
                + $"Identifier: {requirement.Identifier}",
            $"Download {requirement.DisplayName}",
            () => ProcessHelper.OpenUrl(DownloadUrl)
        );
    }

    private static Version HighestInstalled(string[] output, string identifier, int requiredMajor)
    {
        var highest = new Version("0.0.0");

        foreach (var line in output)
        {
            var match = Regex.Match(line, $@"{Regex.Escape(identifier)} (\d+\.\d+\.\d+)");

            if (!match.Success)
            {
                continue;
            }

            var found = ParseVersion(match.Groups[1].Value);

            if (found.Major == requiredMajor && found > highest)
            {
                highest = found;
            }
        }

        return highest;
    }

    private static Version ParseVersion(string value)
    {
        return Version.TryParse(value, out var version) ? version : new Version("0.0.0");
    }
}
