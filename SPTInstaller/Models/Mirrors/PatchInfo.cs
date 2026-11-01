using System.Collections.Generic;

namespace SPTInstaller.Models.Mirrors;

public class PatchInfo
{
    public int SourceClientVersion { get; set; }
    public int TargetClientVersion { get; set; }
    /// <summary>SHA-256 of the patch document, hex</summary>
    public string? Document { get; set; }
    public List<PatchInfoMirror> Mirrors { get; set; }

    public string Route => $"{SourceClientVersion} → {TargetClientVersion}";
}
