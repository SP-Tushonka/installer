using SPTInstaller.Helpers;
using Xunit;

namespace SPTInstaller.Tests;

public sealed class FileHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spt-copy-tests-{Guid.NewGuid():N}");

    public FileHelperTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CopyDirectoryHonorsDirectoryExclusionsOnCurrentPlatform()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source"));
        var target = new DirectoryInfo(Path.Combine(_root, "target"));
        Directory.CreateDirectory(Path.Combine(source.FullName, "Logs"));
        File.WriteAllText(Path.Combine(source.FullName, "EscapeFromTarkov.exe"), "client");
        File.WriteAllText(Path.Combine(source.FullName, "Logs", "session.log"), "excluded");

        var result = FileHelper.CopyDirectoryWithProgress(
            source,
            target,
            progress: (IProgress<double>?)null,
            exclusions: ["Logs"]);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(Path.Combine(target.FullName, "EscapeFromTarkov.exe")));
        Assert.False(File.Exists(Path.Combine(target.FullName, "Logs", "session.log")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
