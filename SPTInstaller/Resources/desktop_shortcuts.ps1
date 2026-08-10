param (
    [string]$sptPath
)

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)

if ([string]::IsNullOrWhiteSpace($desktop) -or -not (Test-Path $desktop)) {
    $desktop = Join-Path $env:USERPROFILE "Desktop"
}

if (-not (Test-Path $desktop)) {
    Write-Error "Could not find the desktop folder"
    exit 1
}

$launcherExe = gci $sptPath | where {$_.Name -like "*.Launcher.exe"} | select -First 1 -ExpandProperty FullName
$serverExe = gci $sptPath | where {$_.Name -like "*.Server.exe"} | select -First 1 -ExpandProperty FullName

if (-not $launcherExe -or -not $serverExe) {
    Write-Error "Could not find the launcher or server executable in $sptPath"
    exit 1
}

$launcherShortcut = Join-Path $desktop "SPT.Launcher.lnk"
$serverShortcut = Join-Path $desktop "SPT.Server.lnk"

$WshShell = New-Object -comObject WScript.Shell

$launcher = $WshShell.CreateShortcut($launcherShortcut)
$launcher.TargetPath = $launcherExe
$launcher.WorkingDirectory = $sptPath
$launcher.Save()

$server = $WshShell.CreateShortcut($serverShortcut)
$server.TargetPath = $serverExe
$server.WorkingDirectory = $sptPath
$server.Save()
