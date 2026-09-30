using System.IO.Compression;
using SPTInstaller.Helpers;
using Xunit;

namespace SPTInstaller.Tests;

public sealed class ZipHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spt-installer-tests-{Guid.NewGuid():N}");

    public ZipHelperTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void DecompressExtractsFilesAndReportsCompletion()
    {
        var archivePath = CreateArchive(("SPT_Runtime/readme.txt", "ready"));
        var output = new DirectoryInfo(Path.Combine(_root, "output"));
        var reported = new List<double>();

        var result = ZipHelper.Decompress(new FileInfo(archivePath), output,
            new Progress<double>(value => reported.Add(value)));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("ready", File.ReadAllText(Path.Combine(output.FullName, "SPT_Runtime", "readme.txt")));
        Assert.Contains(reported, value => Math.Abs(value - 100) < 0.01);
    }

    [Fact]
    public void DecompressExtractsSevenZipArchives()
    {
        var archivePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "sample.7z");
        var output = new DirectoryInfo(Path.Combine(_root, "seven-zip-output"));

        var result = ZipHelper.Decompress(new FileInfo(archivePath), output);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(
            "ready\n",
            File.ReadAllText(Path.Combine(output.FullName, "SPT_Runtime", "readme.txt")));
    }

    [Fact]
    public void DecompressRejectsTraversalBeforeWritingAnyFiles()
    {
        var archivePath = CreateArchive(
            ("safe.txt", "must not be written"),
            ("../escaped.txt", "blocked"));
        var output = new DirectoryInfo(Path.Combine(_root, "output"));

        var result = ZipHelper.Decompress(new FileInfo(archivePath), output);

        Assert.False(result.Succeeded);
        Assert.Contains("escapes", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(output.FullName, "safe.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    [Fact]
    public void DecompressRejectsCaseCollisionsBeforeWritingAnyFiles()
    {
        var archivePath = CreateArchive(
            ("BepInEx/plugins/mod.dll", "one"),
            ("BepInEx/Plugins/MOD.dll", "two"));
        var output = new DirectoryInfo(Path.Combine(_root, "output"));

        var result = ZipHelper.Decompress(new FileInfo(archivePath), output);

        Assert.False(result.Succeeded);
        Assert.Contains("case-colliding", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(output.EnumerateFiles("*", SearchOption.AllDirectories));
    }

    [Fact]
    public void DecompressRejectsSymbolicLinks()
    {
        var archivePath = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using (var stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("link");
            entry.ExternalAttributes = unchecked((int)((0xA000 | 0x1FF) << 16));
            using var writer = new StreamWriter(entry.Open());
            writer.Write("target");
        }

        var result = ZipHelper.Decompress(new FileInfo(archivePath),
            new DirectoryInfo(Path.Combine(_root, "output")));

        Assert.False(result.Succeeded);
        Assert.Contains("symbolic link", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecompressPreservesExecutablePermissionOnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;

        var archivePath = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using (var stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("SPT_Runtime/SPT.Server.Linux");
            entry.ExternalAttributes = unchecked((int)((0x8000 | 0x1ED) << 16));
            using var writer = new StreamWriter(entry.Open());
            writer.Write("launcher");
        }

        var output = new DirectoryInfo(Path.Combine(_root, "output"));
        var result = ZipHelper.Decompress(new FileInfo(archivePath), output);
        var mode = File.GetUnixFileMode(Path.Combine(output.FullName, "SPT_Runtime", "SPT.Server.Linux"));

        Assert.True(result.Succeeded, result.Message);
        Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
        Assert.True(mode.HasFlag(UnixFileMode.GroupExecute));
        Assert.True(mode.HasFlag(UnixFileMode.OtherExecute));
    }

    private string CreateArchive(params (string Path, string Contents)[] entries)
    {
        var archivePath = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (path, contents) in entries)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(contents);
        }

        return archivePath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
