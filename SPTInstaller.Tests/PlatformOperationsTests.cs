using SPTInstaller.Helpers;
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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
