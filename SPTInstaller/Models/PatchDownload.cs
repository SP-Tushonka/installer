using System.Collections.Generic;
using SPTInstaller.Models.Mirrors;

namespace SPTInstaller.Models;

/// <summary>
/// A downloaded patch: its document and every blob it references, by hash, in the cache
/// </summary>
public record PatchDownload(PatchInfo Patch, PatchDocument Document, IReadOnlyDictionary<string, FileInfo> Blobs);
