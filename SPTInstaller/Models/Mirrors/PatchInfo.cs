using System.Collections.Generic;

namespace SPTInstaller.Models.Mirrors;

public class PatchInfo
{
    public int SourceClientVersion { get; set; }
    public int TargetClientVersion { get; set; }
    public string? Sha256 { get; set; }
    public long? Size { get; set; }
    public int? ChunkSize { get; set; }
    public List<string>? ChunkHashes { get; set; }
    public List<PatchInfoMirror> Mirrors { get; set; }
}
