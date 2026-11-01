namespace SPTInstaller.Models;

public enum InstallerStep
{
    Start,
    Folder,
    Checks,
    Install,
}

/// <summary>
/// One link in the title bar's step navigation
/// </summary>
public record StepItem(string Name, bool IsCurrent, bool IsDone, bool IsFailed);
