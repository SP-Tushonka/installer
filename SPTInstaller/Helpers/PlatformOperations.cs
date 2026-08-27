using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

/// <summary>
/// Keeps operating-system-specific installer behavior behind one interface.
/// </summary>
public sealed class PlatformOperations
{
    private readonly IPlatformAdapter _adapter;

    private PlatformOperations(IPlatformAdapter adapter)
    {
        _adapter = adapter;
    }

    public static PlatformOperations Current { get; } = new(CreateAdapter());

    public string InstallerAssetName => _adapter.InstallerAssetName;
    public string DotnetExecutable => _adapter.DotnetExecutable;
    public string DownloadsPath => _adapter.DownloadsPath;

    public string? DetectOriginalGamePath() => _adapter.DetectOriginalGamePath();
    public bool IsRuntimeRequired(string identifier) => _adapter.IsRuntimeRequired(identifier);
    public Result RunPatcher(FileInfo executable, DirectoryInfo workingDirectory) =>
        _adapter.RunPatcher(executable, workingDirectory);
    public Result CreateShortcuts(string installPath, string runtimePath, bool desktop) =>
        _adapter.CreateShortcuts(installPath, runtimePath, desktop);
    public Result ConfigureLauncher(string runtimePath, string? originalGamePath) =>
        _adapter.ConfigureLauncher(runtimePath, originalGamePath);
    public Result OpenDirectory(string path) => _adapter.OpenDirectory(path);
    public void EnsureExecutable(string path) => _adapter.EnsureExecutable(path);

    private static IPlatformAdapter CreateAdapter()
    {
        if (OperatingSystem.IsWindows()) return new WindowsPlatformAdapter();
        if (OperatingSystem.IsLinux()) return new LinuxPlatformAdapter();
        throw new PlatformNotSupportedException("SPT Installer supports Windows and Linux.");
    }

    private interface IPlatformAdapter
    {
        string InstallerAssetName { get; }
        string DotnetExecutable { get; }
        string DownloadsPath { get; }
        string? DetectOriginalGamePath();
        bool IsRuntimeRequired(string identifier);
        Result RunPatcher(FileInfo executable, DirectoryInfo workingDirectory);
        Result CreateShortcuts(string installPath, string runtimePath, bool desktop);
        Result ConfigureLauncher(string runtimePath, string? originalGamePath);
        Result OpenDirectory(string path);
        void EnsureExecutable(string path);
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsPlatformAdapter : IPlatformAdapter
    {
        private const string RegistryInstall =
            @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov";
        private const string SteamPathRegistryKey = @"Software\Wow6432Node\Valve\Steam";

        public string InstallerAssetName => "SPTInstaller.exe";
        public string DotnetExecutable => Path.Combine(
            Environment.ExpandEnvironmentVariables("%ProgramW6432%"), "dotnet", "dotnet.exe");
        public string DownloadsPath => KnownFolders.GetPath(KnownFolder.Downloads);

        public string? DetectOriginalGamePath()
        {
            var configured = ValidateGamePath(Environment.GetEnvironmentVariable("SPT_GAME_PATH"));
            if (configured is not null) return configured;

            using var steamReg = Registry.LocalMachine.OpenSubKey(SteamPathRegistryKey, false);
            var steamPath = steamReg?.GetValue("InstallPath")?.ToString();
            var steamGamePath = FindSteamGame(steamPath is null ? [] : [steamPath]);
            if (steamGamePath is not null) return steamGamePath;

            var installLocation = Registry.LocalMachine.OpenSubKey(RegistryInstall, false)
                ?.GetValue("InstallLocation") as string;
            return ValidateGamePath(installLocation);
        }

        public bool IsRuntimeRequired(string identifier) => true;

        public Result RunPatcher(FileInfo executable, DirectoryInfo workingDirectory) =>
            RunPatcherProcess(executable.FullName, workingDirectory.FullName, ["autoclose"]);

        public Result CreateShortcuts(string installPath, string runtimePath, bool desktop) =>
            ProcessHelper.RunEmbeddedScript(desktop ? "desktop_shortcuts.ps1" : "add_shortcuts.ps1",
                desktop ? [runtimePath] : [installPath, runtimePath]);

        public Result ConfigureLauncher(string runtimePath, string? originalGamePath) => Result.FromSuccess();

        public Result OpenDirectory(string path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });
            return Result.FromSuccess();
        }

        public void EnsureExecutable(string path)
        {
        }
    }

    [SupportedOSPlatform("linux")]
    private sealed class LinuxPlatformAdapter : IPlatformAdapter
    {
        public string InstallerAssetName => "SPTInstaller.Linux";
        public string DotnetExecutable => "dotnet";
        public string DownloadsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        public string? DetectOriginalGamePath()
        {
            var configured = ValidateGamePath(Environment.GetEnvironmentVariable("SPT_GAME_PATH"));
            if (configured is not null) return configured;

            var steamRoots = new[]
            {
                Path.Combine(UserHome(), ".local", "share", "Steam"),
                Path.Combine(UserHome(), ".steam", "steam")
            };
            var steamGamePath = FindSteamGame(steamRoots);
            if (steamGamePath is not null) return steamGamePath;

            var prefixCandidates = new[]
            {
                Environment.GetEnvironmentVariable("WINEPREFIX"),
                AppendIfSet(Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH"), "pfx"),
                Path.Combine(UserHome(), "Games", "tarkov")
            };

            foreach (var prefix in prefixCandidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
            {
                foreach (var relative in new[]
                         {
                             Path.Combine("drive_c", "Battlestate Games", "EFT"),
                             Path.Combine("drive_c", "Program Files (x86)", "Steam", "steamapps", "common",
                                 "Escape from Tarkov", "build")
                         })
                {
                    var gamePath = ValidateGamePath(Path.Combine(prefix!, relative));
                    if (gamePath is not null) return gamePath;
                }
            }

            return null;
        }

        public bool IsRuntimeRequired(string identifier) =>
            !identifier.Equals("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase);

        public Result RunPatcher(FileInfo executable, DirectoryInfo workingDirectory)
        {
            var runner = ResolveRunner();
            if (runner is null)
            {
                return Result.FromError(
                    "A Windows compatibility runner is required for downpatching. Install umu-run or Wine, " +
                    "or set SPT_LINUX_RUNNER to its executable path.");
            }

            var start = new ProcessStartInfo
            {
                FileName = runner,
                WorkingDirectory = workingDirectory.FullName,
                UseShellExecute = false
            };
            start.ArgumentList.Add(executable.FullName);
            start.ArgumentList.Add("autoclose");

            var prefix = Environment.GetEnvironmentVariable("WINEPREFIX") ??
                         DeriveWinePrefix(workingDirectory.FullName);
            if (!string.IsNullOrWhiteSpace(prefix)) start.Environment["WINEPREFIX"] = prefix;

            var proton = Environment.GetEnvironmentVariable("SPT_PROTONPATH");
            if (!string.IsNullOrWhiteSpace(proton)) start.Environment["PROTONPATH"] = proton;

            return RunPatcherProcess(start);
        }

        public Result CreateShortcuts(string installPath, string runtimePath, bool desktop)
        {
            try
            {
                var destination = desktop
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
                    : installPath;
                if (string.IsNullOrWhiteSpace(destination))
                    return Result.FromError("Could not locate the Linux desktop folder.");

                Directory.CreateDirectory(destination);
                var launcher = Path.Combine(runtimePath, "SPT.Launcher.Linux");
                var server = Path.Combine(runtimePath, "SPT.Server.Linux");
                if (!File.Exists(launcher) || !File.Exists(server))
                    return Result.FromError($"Could not find Linux launcher and server executables in {runtimePath}");

                EnsureExecutable(launcher);
                EnsureExecutable(server);
                WriteDesktopFile(Path.Combine(destination, "SPT.Launcher.desktop"), "SPT Launcher", launcher,
                    runtimePath, false);
                WriteDesktopFile(Path.Combine(destination, "SPT.Server.desktop"), "SPT Server", server,
                    runtimePath, true);
                return Result.FromSuccess();
            }
            catch (Exception ex)
            {
                return Result.FromError(ex.Message);
            }
        }

        public Result ConfigureLauncher(string runtimePath, string? originalGamePath)
        {
            try
            {
                var prefix = DeriveWinePrefix(originalGamePath ?? string.Empty) ??
                             Environment.GetEnvironmentVariable("WINEPREFIX");
                if (string.IsNullOrWhiteSpace(prefix) || !Directory.Exists(prefix))
                {
                    return Result.FromError(
                        "Could not derive the Wine prefix from the selected live Tarkov folder. " +
                        "Set WINEPREFIX and run the installer again.");
                }

                var umuPath = ResolveUmuRunner();
                if (umuPath is null)
                {
                    return Result.FromError(
                        "Could not locate umu-run for the SPT Launcher. Install umu-launcher, " +
                        "or set SPT_UMU_PATH to its executable path.");
                }

                var settingsDirectory = Path.Combine(runtimePath, "user", "Launcher");
                var settingsPath = Path.Combine(settingsDirectory, "LauncherSettings.json");
                Directory.CreateDirectory(settingsDirectory);

                JsonObject root;
                if (File.Exists(settingsPath))
                {
                    root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ??
                           throw new InvalidDataException($"Launcher settings are not a JSON object: {settingsPath}");
                }
                else
                {
                    root = new JsonObject();
                }

                var linuxSettings = root["LinuxSettings"] as JsonObject ?? new JsonObject();
                linuxSettings["PrefixPath"] = Path.TrimEndingDirectorySeparator(Path.GetFullPath(prefix));
                linuxSettings["UmuPath"] = Path.GetFullPath(umuPath);
                linuxSettings["ProtonVersion"] =
                    Environment.GetEnvironmentVariable("SPT_PROTONPATH") ?? "GE-Proton";
                linuxSettings["DefaultEnv"] = "WINEDLLOVERRIDES=\"winhttp=n,b\"";
                root["LinuxSettings"] = linuxSettings;
                root["FirstRun"] = false;

                File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return Result.FromSuccess();
            }
            catch (Exception ex)
            {
                return Result.FromError($"Could not configure the Linux SPT Launcher: {ex.Message}");
            }
        }

        public Result OpenDirectory(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("xdg-open")
                {
                    UseShellExecute = false,
                    ArgumentList = { path }
                });
                return Result.FromSuccess();
            }
            catch (Exception ex)
            {
                return Result.FromError(ex.Message);
            }
        }

        public void EnsureExecutable(string path)
        {
            var mode = File.GetUnixFileMode(path);
            mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }

        private static string? ResolveRunner()
        {
            var umuRunner = ResolveUmuRunner();
            if (umuRunner is not null) return umuRunner;

            foreach (var candidate in new[]
                     {
                         Environment.GetEnvironmentVariable("SPT_LINUX_RUNNER"),
                         FindOnPath("wine64"),
                         FindOnPath("wine")
                     })
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static string? ResolveUmuRunner()
        {
            foreach (var candidate in new[]
                     {
                         Environment.GetEnvironmentVariable("SPT_UMU_PATH"),
                         Environment.GetEnvironmentVariable("SPT_LINUX_RUNNER"),
                         Path.Combine(UserHome(), ".local", "bin", "umu-run"),
                         Path.Combine(UserHome(), ".local", "share", "spt-additions", "runtime", "umu-run"),
                         FindOnPath("umu-run")
                     })
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    Path.GetFileName(candidate).Equals("umu-run", StringComparison.Ordinal) &&
                    File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static void WriteDesktopFile(string path, string name, string executable, string workingDirectory,
            bool terminal)
        {
            var contents = new StringBuilder()
                .AppendLine("[Desktop Entry]")
                .AppendLine("Type=Application")
                .AppendLine($"Name={name}")
                .AppendLine($"Exec=\"{EscapeDesktopValue(executable)}\"")
                .AppendLine($"Path={EscapeDesktopValue(workingDirectory)}")
                .AppendLine($"Terminal={terminal.ToString().ToLowerInvariant()}")
                .AppendLine("Categories=Game;")
                .ToString();
            File.WriteAllText(path, contents);
            var mode = File.GetUnixFileMode(path) | UnixFileMode.UserExecute;
            File.SetUnixFileMode(path, mode);
        }

        private static string EscapeDesktopValue(string value) =>
            value.Replace("\\", "\\\\").Replace("`", "\\`").Replace("$", "\\$").Replace("\"", "\\\"");
    }

    private static Result RunPatcherProcess(string executable, string workingDirectory, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return RunPatcherProcess(start);
    }

    private static Result RunPatcherProcess(ProcessStartInfo start)
    {
        try
        {
            using var process = Process.Start(start);
            if (process is null) return Result.FromError("The patcher process did not start.");
            process.WaitForExit();

            return (PatcherExitCode)process.ExitCode switch
            {
                PatcherExitCode.Success => Result.FromSuccess("Patcher finished successfully"),
                PatcherExitCode.ProgramClosed => Result.FromError("Patcher was closed before completing"),
                PatcherExitCode.EftExeNotFound => Result.FromError("The game executable is missing from the install path"),
                PatcherExitCode.NoPatchFolder => Result.FromError("The SPT_Patches folder is missing"),
                PatcherExitCode.MissingFile => Result.FromError("Vital game files were not found"),
                PatcherExitCode.MissingDir => Result.FromError("A vital game directory was not found"),
                PatcherExitCode.PatchFailed => Result.FromError("A patch failed to apply"),
                _ => Result.FromError($"The patcher exited with code {process.ExitCode}")
            };
        }
        catch (Exception ex)
        {
            return Result.FromError(ex.Message);
        }
    }

    private static string? FindSteamGame(IEnumerable<string> steamRoots)
    {
        foreach (var steamRoot in steamRoots.Where(Directory.Exists))
        {
            var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            var libraries = new List<string> { steamRoot };
            libraries.AddRange(ExtractVdfFieldsByName(libraryFile, "path"));

            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var manifest = Path.Combine(library, "steamapps", "appmanifest_3932890.acf");
                var installDir = ExtractVdfFieldsByName(manifest, "installdir").FirstOrDefault();
                if (installDir is null) continue;

                var commonPath = Path.Combine(library, "steamapps", "common", installDir);
                var gamePath = ValidateGamePath(Path.Combine(commonPath, "build")) ?? ValidateGamePath(commonPath);
                if (gamePath is not null) return gamePath;
            }
        }

        return null;
    }

    private static List<string> ExtractVdfFieldsByName(string vdfPath, string fieldName)
    {
        if (!File.Exists(vdfPath)) return [];

        var values = new List<string>();
        var pattern = $@"""{Regex.Escape(fieldName)}""\s+""(.*)""";
        foreach (var line in File.ReadLines(vdfPath))
        {
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (match.Success) values.Add(Regex.Unescape(match.Groups[1].Value));
        }

        return values;
    }

    private static string? ValidateGamePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var fullPath = Path.GetFullPath(path);
        return File.Exists(Path.Combine(fullPath, "EscapeFromTarkov.exe"))
            ? Path.TrimEndingDirectorySeparator(fullPath)
            : null;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        return path.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }

    private static string? DeriveWinePrefix(string path)
    {
        var normalized = path.Replace('\\', '/');
        var marker = normalized.IndexOf("/drive_c/", StringComparison.Ordinal);
        return marker > 0 ? normalized[..marker] : null;
    }

    private static string? AppendIfSet(string? path, string child) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.Combine(path, child);

    private static string UserHome() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
