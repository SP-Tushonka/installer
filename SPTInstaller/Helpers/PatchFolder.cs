using System.Collections.Generic;
using System.IO.Compression;
using Serilog;
using SPTInstaller.Models;
using SPTInstaller.Models.Mirrors;

namespace SPTInstaller.Helpers;

public static class PatchFolder
{
    /// <summary>
    /// Rebuilds the patch folder PatchHelper applies from a downloaded patch's blobs
    /// </summary>
    public static Result Build(PatchDownload patch, DirectoryInfo folder, IProgress<double>? progress = null)
    {
        try
        {
            if (folder.Exists)
            {
                folder.Delete(true);
            }

            folder.Create();

            var entries = patch.Document.Entries;
            var done = 0;

            foreach (var entry in entries)
            {
                var target = entry.Path == null
                    ? null
                    : Path.Join(folder.FullName, entry.Path.Replace('/', Path.DirectorySeparatorChar));

                if (target != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                }

                var blob = entry.Blob == null ? null : patch.Blobs.GetValueOrDefault(entry.Blob);

                if (entry.Kind != PatchDocumentKind.Del && blob == null)
                {
                    return Result.FromError($"The patch is missing the download for {entry.Path ?? "its small files"}");
                }

                switch (entry.Kind)
                {
                    case PatchDocumentKind.Del when target != null:
                        File.Create(target + ".del").Dispose();
                        break;
                    case PatchDocumentKind.Delta when target != null:
                        blob!.CopyTo(target + ".delta", true);
                        break;
                    case PatchDocumentKind.New when target != null:
                        using (var input = blob!.OpenRead())
                        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                        using (var output = File.Create(target + ".new"))
                        {
                            gzip.CopyTo(output);
                        }

                        break;
                    case PatchDocumentKind.Bundle:
                        ZipFile.ExtractToDirectory(blob!.FullName, folder.FullName, true);
                        break;
                    default:
                        return Result.FromError($"The patch has an entry of unknown kind '{entry.Kind}', update the installer");
                }

                progress?.Report(++done * 100.0 / entries.Count);
            }

            return Result.FromSuccess();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not build the patch folder for {route}", patch.Patch.Route);
            return Result.FromError($"Could not prepare the patch: {ex.Message}");
        }
    }
}
