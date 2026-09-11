using SPTInstaller.Helpers;
using System.Text.Json.Nodes;
using Xunit;

namespace SPTInstaller.Tests;

public sealed class PlatformOperationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spt-platform-tests-{Guid.NewGuid():N}");

    public PlatformOperationsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void LinuxAdapterCreatesRunnableDesktopLaunchers()
    {
        if (!OperatingSystem.IsLinux()) return;

        var runtime = Path.Combine(_root, "SPT_Runtime");
        Directory.CreateDirectory(runtime);
        var launcher = Path.Combine(runtime, "SPT.Launcher.Linux");
        var server = Path.Combine(runtime, "SPT.Server.Linux");
        File.WriteAllText(launcher, "launcher");
        File.WriteAllText(server, "server");

        var platform = PlatformOperations.Current;
        var result = platform.CreateShortcuts(_root, runtime, desktop: false);

        Assert.Equal("SPTInstaller.Linux", platform.InstallerAssetName);
        Assert.Equal("dotnet", platform.DotnetExecutable);
        Assert.False(platform.IsRuntimeRequired("Microsoft.WindowsDesktop.App"));
        Assert.True(platform.IsRuntimeRequired("Microsoft.AspNetCore.App"));
        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.GetUnixFileMode(launcher).HasFlag(UnixFileMode.UserExecute));
        Assert.True(File.GetUnixFileMode(server).HasFlag(UnixFileMode.UserExecute));

        var launcherEntry = Path.Combine(_root, "SPT.Launcher.desktop");
        var serverEntry = Path.Combine(_root, "SPT.Server.desktop");
        Assert.Contains($"Exec=\"{launcher}\"", File.ReadAllText(launcherEntry));
        Assert.Contains("Terminal=false", File.ReadAllText(launcherEntry));
        Assert.Contains($"Exec=\"{server}\"", File.ReadAllText(serverEntry));
        Assert.Contains("Terminal=true", File.ReadAllText(serverEntry));
        Assert.True(File.GetUnixFileMode(launcherEntry).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void LinuxAdapterWritesRunnableLauncherSettings()
    {
        if (!OperatingSystem.IsLinux()) return;

        var prefix = Path.Combine(_root, "tarkov");
        var liveGame = Path.Combine(prefix, "drive_c", "Battlestate Games", "Escape from Tarkov");
        var runtime = Path.Combine(_root, "SPT_Runtime");
        var bin = Path.Combine(_root, "bin");
        var umu = Path.Combine(bin, "umu-run");
        Directory.CreateDirectory(liveGame);
        Directory.CreateDirectory(runtime);
        Directory.CreateDirectory(bin);
        File.WriteAllText(umu, "umu");

        var previousRunner = Environment.GetEnvironmentVariable("SPT_UMU_PATH");
        var previousProton = Environment.GetEnvironmentVariable("SPT_PROTONPATH");
        try
        {
            Environment.SetEnvironmentVariable("SPT_UMU_PATH", umu);
            Environment.SetEnvironmentVariable("SPT_PROTONPATH", null);

            var result = PlatformOperations.Current.ConfigureLauncher(runtime, liveGame);

            Assert.True(result.Succeeded, result.Message);
            var settingsPath = Path.Combine(runtime, "user", "Launcher", "LauncherSettings.json");
            var settings = JsonNode.Parse(File.ReadAllText(settingsPath))!;
            Assert.False(settings["FirstRun"]!.GetValue<bool>());
            Assert.Equal(prefix, settings["LinuxSettings"]!["PrefixPath"]!.GetValue<string>());
            Assert.Equal(umu, settings["LinuxSettings"]!["UmuPath"]!.GetValue<string>());
            Assert.Equal("GE-Proton", settings["LinuxSettings"]!["ProtonVersion"]!.GetValue<string>());
            Assert.Equal("WINEDLLOVERRIDES=\"winhttp=n,b\"",
                settings["LinuxSettings"]!["DefaultEnv"]!.GetValue<string>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPT_UMU_PATH", previousRunner);
            Environment.SetEnvironmentVariable("SPT_PROTONPATH", previousProton);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
