using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using SPTInstaller.Models;
using SPTInstaller.Models.Mirrors;

namespace SPTInstaller.Helpers;

public static class SkippedGameFiles
{
    /// <summary>
    /// Game files the route deletes or replaces before any step reads them, so they never need copying.
    /// Paths are relative to the game folder and separated by '/'.
    /// </summary>
    public static HashSet<string> Find(IEnumerable<IEnumerable<PatchDocumentEntry>> steps)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            foreach (var entry in step.Where(entry => entry.Path != null))
            {
                if (seen.Add(entry.Path!) && (entry.Kind == PatchDocumentKind.Del || entry.Kind == PatchDocumentKind.New))
                {
                    skipped.Add(entry.Path!);
                }
            }
        }

        return skipped;
    }

    /// <summary>
    /// The patch's entries with the bundle's small files listed as entries of their own
    /// </summary>
    public static IEnumerable<PatchDocumentEntry> WithBundled(PatchDownload patch)
    {
        foreach (var entry in patch.Document.Entries)
        {
            if (entry.Kind != PatchDocumentKind.Bundle)
            {
                yield return entry;
                continue;
            }

            using var zip = ZipFile.OpenRead(patch.Blobs[entry.Blob!].FullName);

            foreach (var file in zip.Entries.Where(file => !file.FullName.EndsWith('/')))
            {
                var name = file.FullName.Replace('\\', '/');
                var extension = Path.GetExtension(name);

                yield return new PatchDocumentEntry { Path = name[..^extension.Length], Kind = extension.TrimStart('.') };
            }
        }
    }
}
