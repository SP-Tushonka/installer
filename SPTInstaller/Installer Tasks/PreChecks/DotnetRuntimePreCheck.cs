using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;
using SPTInstaller.Helpers;
using SPTInstaller.Models;
using SPTInstaller.Models.ReleaseInfo;

namespace SPTInstaller.Installer_Tasks.PreChecks;

public class DotnetRuntimePreCheck : PreCheckBase
{
    private const string DesktopRuntime = "Microsoft.WindowsDesktop.App";
    private const string AspNetCoreRuntime = "Microsoft.AspNetCore.App";

    private static readonly RuntimeRequirement[] _fallback =
    [
        new RuntimeRequirement
        {
            DisplayName = ".Net 10 Desktop Runtime",
            Identifier = DesktopRuntime,
            MinimumVersion = "10.0.9",
            DownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.9-windows-x64-installer",
        },
        new RuntimeRequirement
        {
            DisplayName = "ASP.Net Core 10 Runtime",
            Identifier = AspNetCoreRuntime,
            MinimumVersion = "10.0.9",
            DownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-aspnetcore-10.0.9-windows-x64-installer",
        },
    ];

    public DotnetRuntimePreCheck()
        : base(".Net Runtimes", true) { }

    public override async Task<PreCheckResult> CheckOperation()
    {
        var requirements = ServiceHelper.Get<InternalData?>()?.SelectedChannel?.Release?.RequiredRuntimes;

        if (requirements == null || requirements.Count == 0)
        {
            requirements = new List<RuntimeRequirement>(_fallback);
        }

        string[] output;

        try
        {
            var programFiles = Environment.ExpandEnvironmentVariables("%ProgramW6432%");
            var result = ProcessHelper.RunAndReadProcessOutputs($@"{programFiles}\dotnet\dotnet.exe", "--list-runtimes");

            if (!result.Succeeded)
            {
                return PreCheckResult.FromError(
                    result.Message + "\n\nYou most likely don't have the .Net runtimes installed",
                    "Download .Net",
                    OpenDownload(requirements[0].DownloadUrl)
                );
            }

            output = result.StdOut.Split("\r\n");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"PreCheck::{Name}::Exception");
            return PreCheckResult.FromException(ex);
        }

        var satisfied = new List<string>();

        foreach (var requirement in requirements)
        {
            var minimum = ParseVersion(requirement.MinimumVersion);
            var highest = HighestInstalled(output, requirement.Identifier, minimum.Major);

            if (highest >= minimum)
            {
                satisfied.Add($"{requirement.DisplayName}: {highest}");
                continue;
            }

            return PreCheckResult.FromError(
                $"{requirement.DisplayName} {minimum} or higher is required.\n\n"
                    + $"Highest Version Found: {(highest > new Version("0.0.0") ? highest.ToString() : "Not Found")}",
                $"Download {requirement.DisplayName}",
                OpenDownload(requirement.DownloadUrl)
            );
        }

        return PreCheckResult.FromSuccess(string.Join("\n", satisfied));
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

    private static Action OpenDownload(string url) =>
        () =>
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ArgumentList = { "/C", "start", url },
                }
            );
        };
}
