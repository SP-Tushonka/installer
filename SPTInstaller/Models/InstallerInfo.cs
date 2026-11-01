using System.Collections.Generic;

namespace SPTInstaller.Models;

public class InstallerInfo
{
    public string LatestVersion { get; set; }
    public List<string> Changes { get; set; }
    public string? Sha256 { get; set; }
    public string? Sha256Linux { get; set; }
}