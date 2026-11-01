using System.Collections.Generic;
using SPTInstaller.Helpers;

namespace SPTInstaller.Models.ReleaseInfo;

public class ReleaseInfo
{
    public string SPTVersion { get; set; }
    public string ClientVersion { get; set; }
    public string RuntimeFolderName { get; set; }
    public List<RuntimeRequirement> RequiredRuntimes { get; set; } = new();

    // An older release that is offered without support
    public bool Legacy { get; set; }

    /// <summary>
    ///     Whether the release has all its native Linux binaries
    /// </summary>
    public bool LinuxCompatible { get; set; }

    /// <summary>
    ///     Whether the release is missing native Linux binaries it needs here. Always false on Windows.
    /// </summary>
    public bool LinuxIncompatible => !OperatingSystem.IsWindows() && !LinuxCompatible;

    /// <summary>
    ///     Whether the release can be picked. Debug mode can pick Linux incompatible releases for testing.
    /// </summary>
    public bool Selectable => !LinuxIncompatible || ServiceHelper.Get<InternalData>()?.DebugMode == true;

    public List<ReleaseInfoMirror> Mirrors { get; set; }
}