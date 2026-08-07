using System.Collections.Generic;

namespace SPTInstaller.Models.Mirrors;

public class PatchManifest
{
    public List<PatchInfo> Patches { get; set; } = new();
}
