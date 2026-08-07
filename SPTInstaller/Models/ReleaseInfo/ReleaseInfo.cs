using System.Collections.Generic;

namespace SPTInstaller.Models.ReleaseInfo;

public class ReleaseInfo
{
    public string SPTVersion { get; set; }
    public string ClientVersion { get; set; }
    public string RuntimeFolderName { get; set; }
    public List<RuntimeRequirement> RequiredRuntimes { get; set; } = new();
    public List<ReleaseInfoMirror> Mirrors { get; set; }
}