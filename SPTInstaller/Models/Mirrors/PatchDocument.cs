using System.Collections.Generic;

namespace SPTInstaller.Models.Mirrors;

/// <summary>
/// One patch as published: which blob rebuilds each entry of the patch folder
/// </summary>
public class PatchDocument
{
    public int SourceClientVersion { get; set; }
    public int TargetClientVersion { get; set; }
    public string? SPTVersion { get; set; }
    public List<PatchDocumentEntry> Entries { get; set; } = [];
}

public static class PatchDocumentKind
{
    public const string Delta = "delta";

    /// <summary>The blob is the zstd-compressed file</summary>
    public const string New = "new";

    public const string Del = "del";

    /// <summary>A zip of every small delta and new file, each named like the patch folder entry it is</summary>
    public const string Bundle = "bundle";
}

public class PatchDocumentEntry
{
    /// <summary>Relative to the game folder, separated by '/'. The bundle has none.</summary>
    public string? Path { get; set; }
    public string Kind { get; set; } = "";

    /// <summary>The blob's SHA-256 in lowercase hex, which is also its name on the mirrors. Deletions have none.</summary>
    public string? Blob { get; set; }
    public long? Size { get; set; }
    public int? ChunkSize { get; set; }
    public List<string>? ChunkHashes { get; set; }
}
