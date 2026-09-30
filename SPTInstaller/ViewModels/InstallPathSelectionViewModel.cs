using System.Linq;
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
    
    private string _selectedPath;
    
    public string SelectedPath
    {
        get => _selectedPath;
        set => this.RaiseAndSetIfChanged(ref _selectedPath, value);
    }

    private string _originalGamePath;

    public string OriginalGamePath
    {
        get => _originalGamePath;
        set => this.RaiseAndSetIfChanged(ref _originalGamePath, value);
    }
    
    private bool _validPath;
    public bool ValidPath
    {
        get => _validPath;
        set => this.RaiseAndSetIfChanged(ref _validPath, value);
    }
    
    private string _errorMessage;
    
    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }
    
    public InstallPathSelectionViewModel(IScreen host, string installPath) : base(host)
    {
        _data = ServiceHelper.Get<InternalData?>() ?? throw new Exception("Failed to get internal data");
        SelectedPath = Environment.CurrentDirectory;
        OriginalGamePath = PreCheckHelper.DetectOriginalGamePath() ?? "";
        ValidPath = false;
        
        if (!string.IsNullOrEmpty(installPath))
        {
            SelectedPath = installPath;
            ValidatePath();
            
            if (ValidPath)
            {
                Log.Information("Install Path was provided by parameter and seems valid");
                Task.Run(NextCommand);
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
            
            var selection = selections.FirstOrDefault();
            if (selection is not null) SelectedPath = selection.Path.LocalPath;
        } 
    }


    public async Task SelectGameFolderCommand()
    {
        if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null) return;

        var startingPath = Directory.Exists(OriginalGamePath) ? OriginalGamePath : Environment.CurrentDirectory;
        var suggestedFolder = await desktop.MainWindow.StorageProvider.TryGetFolderFromPathAsync(startingPath);
        var selections = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            SuggestedStartLocation = suggestedFolder,
            Title = "Select the original Escape from Tarkov folder"
        });
        var selection = selections.FirstOrDefault();
        if (selection is not null) OriginalGamePath = selection.Path.LocalPath;
    }
    
    public void ValidatePath()
    {
        if (string.IsNullOrWhiteSpace(OriginalGamePath) ||
            !File.Exists(Path.Combine(OriginalGamePath, "EscapeFromTarkov.exe")))
        {
            ErrorMessage = "Select the original Escape from Tarkov folder containing EscapeFromTarkov.exe";
            ValidPath = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedPath))
        {
            ErrorMessage = "Please provide an install path";
            ValidPath = false;
            return;
        }
        
        if (SelectedPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            ErrorMessage = "The install path contains characters that are invalid on this operating system";
            ValidPath = false;
            return;
        }

        try
        {
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SelectedPath));
            var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(OriginalGamePath));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (target.Equals(source, comparison) ||
                target.StartsWith(source + Path.DirectorySeparatorChar, comparison) ||
                source.StartsWith(target + Path.DirectorySeparatorChar, comparison))
            {
                ErrorMessage = "The SPT folder must be separate from the original game folder";
                ValidPath = false;
                return;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"The install path is invalid: {ex.Message}";
            ValidPath = false;
            return;
        }
        
        if (FileHelper.CheckPathForProblemLocations(SelectedPath, out var failedCheck))
        {
            if (failedCheck.CheckType == PathCheckType.EndsWith)
            {
                ErrorMessage = $"You can install in {failedCheck.Target}, but only in a subdirectory. Example: ..\\{failedCheck.Target}\\SPT";
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
        
        ValidPath = true;
    }
    
    private void AdjustInstallPath()
    {
        if (FileHelper.CheckPathForProblemLocations(SelectedPath, out var failedCheck))
        {
            switch (failedCheck.CheckType)
            {
                case PathCheckType.EndsWith:
                    SelectedPath = Path.Join(Environment.CurrentDirectory, "SPT");
                    break;
                
                case PathCheckType.Contains:
                case PathCheckType.DriveRoot:
                    SelectedPath = OperatingSystem.IsLinux()
                        ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SPT")
                        : Path.Join(Directory.GetDirectoryRoot(Environment.CurrentDirectory), "SPT");
                    break;
                
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
    
    public void NextCommand()
    {
        if (FileHelper.CheckPathForProblemLocations(SelectedPath, out var failedCheck) && failedCheck.CheckAction == PathCheckAction.Deny)
        {
            return;
        }
        
        _data.TargetInstallPath = SelectedPath;
        _data.OriginalGamePath = OriginalGamePath;
        
#if !TEST
        if (_data.OriginalGamePath == null)
        {
            NavigateTo(new MessageViewModel(HostScreen,
                Result.FromError("Could not find where you installed the game.\n\nDo you own and have the game installed?")));
            return;
        }
#endif
        
        NavigateTo(new PreChecksViewModel(HostScreen));
    }
}
