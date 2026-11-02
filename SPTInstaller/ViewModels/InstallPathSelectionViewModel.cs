using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ReactiveUI;
using Serilog;
using SPTInstaller.Helpers;
using SPTInstaller.Models;

namespace SPTInstaller.ViewModels;

public class InstallPathSelectionViewModel : ViewModelBase
{
    private InternalData _data;

    // Detected once, validation runs on every keystroke and detection goes through the registry
    private string? _gamePath;

    public string? GamePath
    {
        get => _gamePath;
        set
        {
            this.RaiseAndSetIfChanged(ref _gamePath, value);
            this.RaisePropertyChanged(nameof(GameButtonText));
            this.RaisePropertyChanged(nameof(CanContinue));
        }
    }

    public string GameButtonText => GamePath == null ? "Choose" : "Change";

    private string? _gameError;

    public string? GameError
    {
        get => _gameError;
        set => this.RaiseAndSetIfChanged(ref _gameError, value);
    }

#if TEST
    public bool CanContinue => ValidPath;
#else
    public bool CanContinue => ValidPath && GamePath != null;
#endif

    private string _freeSpaceText = "";

    public string FreeSpaceText
    {
        get => _freeSpaceText;
        set => this.RaiseAndSetIfChanged(ref _freeSpaceText, value);
    }

    public override InstallerStep Step => InstallerStep.Folder;

    private static string HomeFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string LinuxDefaultPath => Path.Join(HomeFolder, "SPT");

    public string PathPlaceholder { get; } = OperatingSystem.IsWindows() ? @"D:\SPT" : LinuxDefaultPath;

    public string ThingsToNote { get; } = OperatingSystem.IsWindows()
        ? "Pick an empty folder outside the game\nAvoid Downloads, Desktop and Program Files\nDownloads are cached in %appdata%\\spt-installer, so a reinstall doesn't fetch them again"
        : $"Pick an empty folder outside the game\nAvoid Downloads and Desktop\nDownloads are cached in {Path.GetDirectoryName(DownloadCacheHelper.CachePath)!.Replace(HomeFolder, "~")}, so a reinstall doesn't fetch them again";

    // A drive root can't be written to on Linux, so suggestions there point at the home folder
    private string SuggestedPath => OperatingSystem.IsWindows() ? $"{Path.GetPathRoot(SelectedPath)}SPT" : LinuxDefaultPath;

    private string _selectedPath;
    
    public string SelectedPath
    {
        get => _selectedPath;
        set => this.RaiseAndSetIfChanged(ref _selectedPath, value);
    }
    
    private bool _validPath;
    public bool ValidPath
    {
        get => _validPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _validPath, value);
            this.RaisePropertyChanged(nameof(CanContinue));
        }
    }
    
    private string _errorMessage;
    
    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }
    
    /// <param name="autoAdvance">Skip straight to the prechecks when <paramref name="installPath"/> is valid</param>
    public InstallPathSelectionViewModel(IScreen host, string installPath, bool autoAdvance = true) : base(host)
    {
        _data = ServiceHelper.Get<InternalData?>() ?? throw new Exception("Failed to get internal data");
        // Keeps a folder picked by hand when coming back from the pre-checks
        GamePath = _data.OriginalGamePath ?? PreCheckHelper.DetectOriginalGamePath();
        GameError = GamePath == null ? "Choose the folder that has EscapeFromTarkov.exe" : null;
        SelectedPath = OperatingSystem.IsWindows() ? Environment.CurrentDirectory : LinuxDefaultPath;
        ValidPath = false;

        if (!string.IsNullOrEmpty(installPath))
        {
            SelectedPath = installPath;
            ValidatePath();

            if (ValidPath)
            {
                if (autoAdvance && CanContinue)
                {
                    Log.Information("Install Path was provided by parameter and seems valid");
                    Task.Run(NextCommand);
                }

                return;
            }
        }
        
        AdjustInstallPath();
    }
    
    public async Task SelectFolderCommand()
    {
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow == null)
            {
                return;
            }
            
            var startingFolderPath = Directory.Exists(SelectedPath) ? SelectedPath : Environment.CurrentDirectory;
            
            var suggestedFolder = await desktop.MainWindow.StorageProvider.TryGetFolderFromPathAsync(startingFolderPath);
                
            var selections = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions()
                {
                    AllowMultiple = false,
                    SuggestedStartLocation = suggestedFolder, 
                    Title = "Select a folder to install into"
                });
            
            var selected = selections.FirstOrDefault();
            if (selected != null)
            {
                SelectedPath = selected.Path.LocalPath;
            }
        }
    }

    public async Task SelectGameFolderCommand()
    {
        if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
        {
            return;
        }

        var storage = desktop.MainWindow.StorageProvider;
        var selections = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            SuggestedStartLocation = GamePath == null ? null : await storage.TryGetFolderFromPathAsync(GamePath),
            Title = "Select the folder that has EscapeFromTarkov.exe"
        });

        var folder = selections.FirstOrDefault()?.Path.LocalPath;
        if (folder == null)
        {
            return;
        }

        // Steam keeps the game in a build folder under the one people tend to pick
        if (!HasGame(folder) && HasGame(Path.Join(folder, "build")))
        {
            folder = Path.Join(folder, "build");
        }

        if (!HasGame(folder))
        {
            GameError = "EscapeFromTarkov.exe isn't in that folder";
            return;
        }

        GameError = null;
        GamePath = Path.TrimEndingDirectorySeparator(folder);
        ValidatePath();
    }

    private static bool HasGame(string folder) => File.Exists(Path.Join(folder, "EscapeFromTarkov.exe"));
    
    public void ValidatePath()
    {
        // Only a shell expands this, and Linux users expect to be able to type it
        if (!OperatingSystem.IsWindows() && SelectedPath != null && (SelectedPath == "~" || SelectedPath.StartsWith("~/")))
        {
            SelectedPath = Path.Join(HomeFolder, SelectedPath[1..]);
        }

        var root = string.IsNullOrEmpty(SelectedPath) ? null : FileHelper.DriveRoot(SelectedPath);
        var drive = root == null ? null : DriveInfo.GetDrives().FirstOrDefault(d => string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase));
        FreeSpaceText = drive == null || !drive.IsReady
            ? "Drive not found"
            : $"{DirectorySizeHelper.SizeSuffix(drive.AvailableFreeSpace, 0)} free on {drive.Name}";

        if (String.IsNullOrEmpty(SelectedPath) || SelectedPath.Length < 4)
        {
            ErrorMessage = "Please provide an install path";
            ValidPath = false;
            return;
        }
        
        var match = OperatingSystem.IsWindows()
            ? Regex.Match(SelectedPath[2..], @"[\/:*?""<>|!@#$%^&*+=,[\]{}`~;']|\\\\")
            : Regex.Match(SelectedPath, @"[\\:*?""<>|!@#$%^&*+=,[\]{}`~;']|//");
        if (match.Success)
        {
            ErrorMessage = $"Path cannot contain symbols other than ( ) {Path.DirectorySeparatorChar} - _ .";
            ValidPath = false;
            return;
        }

        var selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SelectedPath));
        var game = GamePath == null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(GamePath));

        if (game != null && string.Equals(selected, game, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = $"This is the game's own folder. Choose an empty folder instead, like {SuggestedPath}";
            ValidPath = false;
            return;
        }

        // The launcher's file check and updates can delete or overwrite an install that lives inside the game
        if (game != null && selected.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = $"This is inside the game's folder. Choose an empty folder outside it, like {SuggestedPath}";
            ValidPath = false;
            return;
        }

        if (FileHelper.CheckPathForProblemLocations(SelectedPath, out var failedCheck))
        {
            if (failedCheck.CheckType == PathCheckType.EndsWith)
            {
                ErrorMessage = $"You can install in {failedCheck.Target}, but only in a subdirectory. Example: {Path.Join("..", failedCheck.Target, "SPT")}";
                ValidPath = false;
                return;
            }
            
            if (failedCheck.CheckAction == PathCheckAction.Deny)
            {
                ErrorMessage = $"Sorry, you cannot install in {failedCheck.Target}";
                ValidPath = false;
                return;
            }
        }

        if (!FileHelper.CanInstallTo(SelectedPath))
        {
            ErrorMessage = $"The installer can't write to this folder. Choose one you own, like {SuggestedPath}";
            ValidPath = false;
            return;
        }

        ValidPath = true;
    }
    
    private void AdjustInstallPath()
    {
        // The home folder default is already the fallback, and a drive root can't be written to
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (FileHelper.CheckPathForProblemLocations(SelectedPath, out var failedCheck))
        {
            switch (failedCheck.CheckType)
            {
                case PathCheckType.EndsWith:
                    SelectedPath = Path.Join(Environment.CurrentDirectory, "SPT");
                    break;
                
                case PathCheckType.Contains:
                case PathCheckType.DriveRoot:
                    SelectedPath = Path.Join(Directory.GetDirectoryRoot(Environment.CurrentDirectory), "SPT");
                    break;
                
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
    
    public void NextCommand()
    {
        if (!CanContinue)
        {
            return;
        }

        if (FileHelper.CheckPathForProblemLocations(SelectedPath, out var failedCheck) && failedCheck.CheckAction == PathCheckAction.Deny)
        {
            return;
        }
        
        _data.TargetInstallPath = SelectedPath;
        
        _data.OriginalGamePath = GamePath;

        NavigateTo(new PreChecksViewModel(HostScreen));
    }
}