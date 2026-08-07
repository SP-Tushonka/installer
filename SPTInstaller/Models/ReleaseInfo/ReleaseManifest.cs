using System.Collections.Generic;

namespace SPTInstaller.Models.ReleaseInfo;

public class ReleaseManifest
{
    public List<ReleaseInfo> Releases { get; set; } = new();
}
