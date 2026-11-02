using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SPTInstaller.Models;

namespace SPTInstaller.Installer_Tasks.PreChecks;

/// <summary>
///     Warn about tools the SPT launcher needs to start the game on Linux. The install doesn't use them, so this never stops it.
/// </summary>
public class LinuxToolsPreCheck : PreCheckBase
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string[] ProtonFolders =
    [
        Path.Join(Home, ".steam", "steam", "compatibilitytools.d"),
        Path.Join(Home, ".local", "share", "Steam", "compatibilitytools.d"),
        Path.Join(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "compatibilitytools.d"),
        "/usr/share/steam/compatibilitytools.d",
    ];

    public LinuxToolsPreCheck() : base("Launch Tools", false)
    {
    }

    public override Task<PreCheckResult> CheckOperation()
    {
        List<string> missing = [];

        if (!HasUmu())
        {
            missing.Add("• umu-run");
        }

        if (!HasProton())
        {
            missing.Add("• A Proton build, such as GE-Proton");
        }

        return Task.FromResult(missing.Count == 0
            ? PreCheckResult.FromSuccess("umu-run and a Proton build were found. The SPT launcher asks for them and your Wine prefix when it first opens.")
            : PreCheckResult.FromWarning(
                $"SPT needs these to start the game:\n\n{string.Join('\n', missing)}\n\n"
                + "You can still install SPT. If you have them somewhere else, you can set their paths in the launcher."));
    }

    private static bool HasUmu()
    {
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];

        return path.Append(Path.Join(Home, ".local", "bin")).Any(folder => File.Exists(Path.Join(folder, "umu-run")));
    }

    private static bool HasProton() => ProtonFolders.Where(Directory.Exists)
        .SelectMany(Directory.EnumerateDirectories)
        .Any(folder => File.Exists(Path.Join(folder, "compatibilitytool.vdf")));
}
