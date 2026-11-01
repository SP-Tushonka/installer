using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;
using SPTInstaller.Models.Mirrors;

namespace SPTInstaller.Helpers;

/// <param name="Document">Null when the download failed or the file does not parse</param>
public record PatchDocumentResult(PatchDocument? Document, DownloadResult Download);

public static class PatchDocuments
{
    public static List<string> DocumentLinks(IEnumerable<PatchInfoMirror> mirrors)
        => mirrors.Select(mirror => mirror.Document).ToList();

    public static List<string> BlobLinks(IEnumerable<PatchInfoMirror> mirrors, string hash)
        => mirrors.Select(mirror => $"{mirror.Document[..(mirror.Document.LastIndexOf('/') + 1)]}blobs/{hash}").ToList();

    /// <summary>
    /// The patch document, from the cache when its hash still matches
    /// </summary>
    public static async Task<PatchDocumentResult> GetAsync(PatchInfo patch,
        IReadOnlyList<string> links, IProgress<double> progress, Action<string>? status = null)
    {
        var spec = DownloadSpec.From(patch.Document, "");
        var fileName = DownloadCacheHelper.PatchDocumentFileName(patch.SourceClientVersion, patch.TargetClientVersion);
        var download = await DownloadCacheHelper.GetOrDownloadFileAsync(fileName, links, spec, progress, status);

        if (download.Outcome != DownloadOutcome.Downloaded)
        {
            return new PatchDocumentResult(null, download);
        }

        try
        {
            await using var stream = download.File!.OpenRead();

            return new PatchDocumentResult(await JsonSerializer.DeserializeAsync<PatchDocument>(stream, JsonOptions.Default), download);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The patch document {name} does not parse", fileName);
            return new PatchDocumentResult(null, new DownloadResult(DownloadOutcome.Corrupted));
        }
    }
}
