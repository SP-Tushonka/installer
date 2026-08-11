using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;
using SPTInstaller.Helpers;
using SPTInstaller.Models;
using SPTInstaller.Models.ReleaseInfo;

namespace SPTInstaller.Installer_Tasks.PreChecks;

public abstract class DotnetRuntimePreCheckBase(string name, string identifier, RuntimeRequirement fallback) : PreCheckBase(name, true)
{
    public override async Task<PreCheckResult> CheckOperation()
    {
        var requirement = ServiceHelper.Get<InternalData?>()?.SelectedChannel?.Release?.RequiredRuntimes
            ?.FirstOrDefault(candidate => string.Equals(candidate.Identifier, identifier, StringComparison.OrdinalIgnoreCase))
            ?? fallback;

        Name = requirement.DisplayName;

        string[] output;

        try
        {
            var programFiles = Environment.ExpandEnvironmentVariables("%ProgramW6432%");
            var result = ProcessHelper.RunAndReadProcessOutputs($@"{programFiles}\dotnet\dotnet.exe", "--list-runtimes");

            if (!result.Succeeded)
            {
                return PreCheckResult.FromError(
                    $"{requirement.DisplayName} could not be detected.\n\nYou most likely don't have it installed.",
                    $"Download {requirement.DisplayName}",
                    () => ProcessHelper.OpenUrl(requirement.DownloadUrl)
                );
            }

            output = result.StdOut.Split("\r\n");
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
            () => ProcessHelper.OpenUrl(requirement.DownloadUrl)
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

public class DesktopRuntimePreCheck() : DotnetRuntimePreCheckBase(
    ".Net Desktop Runtime",
    "Microsoft.WindowsDesktop.App",
    new RuntimeRequirement
    {
        DisplayName = ".Net 10 Desktop Runtime",
        Identifier = "Microsoft.WindowsDesktop.App",
        MinimumVersion = "10.0.9",
        DownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.9-windows-x64-installer",
    });

public class AspNetCoreRuntimePreCheck() : DotnetRuntimePreCheckBase(
    "ASP.Net Core Runtime",
    "Microsoft.AspNetCore.App",
    new RuntimeRequirement
    {
        DisplayName = "ASP.Net Core 10 Runtime",
        Identifier = "Microsoft.AspNetCore.App",
        MinimumVersion = "10.0.9",
        DownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-aspnetcore-10.0.9-windows-x64-installer",
    });
