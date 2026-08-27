using SPTInstaller.Helpers;
using Xunit;

namespace SPTInstaller.Tests;

public sealed class PreCheckHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"spt-installer-version-{Guid.NewGuid():N}");

    [Fact]
    public void DetectOriginalGameVersionFallsBackToNativePeVersionResource()
    {
        Directory.CreateDirectory(_root);
        File.Copy(
            typeof(PreCheckHelperTests).Assembly.Location,
            Path.Combine(_root, "EscapeFromTarkov.exe"));

        var result = PreCheckHelper.DetectOriginalGameVersion(
            _root,
            _ => null);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("40743", result.Message);
    }

    [Fact]
    public void DetectOriginalGameVersionKeepsPlatformVersionFastPath()
    {
        var result = PreCheckHelper.DetectOriginalGameVersion(
            _root,
            _ => "0.16.9.5-40743-f137e819");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("40743", result.Message);
    }

    [Fact]
    public void DetectOriginalGameVersionReportsUnreadableExecutable()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "EscapeFromTarkov.exe"),
            "not a portable executable");

        var result = PreCheckHelper.DetectOriginalGameVersion(
            _root,
            _ => null);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Could not read EscapeFromTarkov.exe:", result.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
