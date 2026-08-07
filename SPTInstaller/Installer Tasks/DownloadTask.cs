using System;
using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SPTInstaller.Helpers;
using SPTInstaller.Models.Mirrors;
using SPTInstaller.Models.Mirrors.Downloaders;
using Serilog;

namespace SPTInstaller.Installer_Tasks;

public class DownloadTask : InstallerTaskBase
{
    private InternalData _data;
    private List<IMirrorDownloader> _mirrors = new List<IMirrorDownloader>();
    private string _expectedPatcherHash = "";
    
    public DownloadTask(InternalData data) : base("Download Files")
    {
        _data = data;
    }
    
    private async Task<IResult> BuildMirrorList()
    {
        var mirrors = _data.PatchInfo.Mirrors;
        var selectedName = _data.SelectedChannel?.MirrorName;

        // A chosen mirror is honoured exactly, so a failure is reported rather than quietly served
        // from somewhere the user did not pick.
        if (!string.IsNullOrWhiteSpace(selectedName))
        {
            mirrors = mirrors.Where(mirror =>
                string.Equals(mirror.Name, selectedName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (mirrors.Count == 0)
            {
                return Result.FromError($"No patch mirror named '{selectedName}' is published for this release.");
            }
        }

        foreach (var mirror in mirrors)
        {
            _expectedPatcherHash = mirror.Hash;

            _mirrors.Add(new HttpMirrorDownloader(mirror));
        }
        
        return Result.FromSuccess("Mirrors list ready");
    }
    
    private async Task<IResult> DownloadPatcherFromMirrors(IProgress<double> progress)
    {
        SetStatus("Downloading Patcher", "Verifying cached patcher ...", progressStyle: ProgressStyle.Indeterminate);
        
        if (DownloadCacheHelper.CheckCacheHash("patcher", _expectedPatcherHash, out var cacheFile))
        {
            _data.PatcherZipInfo = cacheFile;
            Log.Information("Using cached file {fileName} - Hash: {hash}", _data.PatcherZipInfo.Name,
                _expectedPatcherHash);
            return Result.FromSuccess();
        }
        
        foreach (var mirror in _mirrors)
        {
            SetStatus("Downloading Patcher", mirror.MirrorInfo.Link, progressStyle: ProgressStyle.Indeterminate);
            
            _data.PatcherZipInfo = await mirror.Download(progress);
            
            if (_data.PatcherZipInfo != null)
            {
                return Result.FromSuccess();
            }
        }
        
        return Result.FromError("Failed to download Patcher");
    }
    
    private async Task<IResult> DownloadSPTFromMirrors(IProgress<double> progress)
    {
        // Note that GetOrDownloadFileAsync handles the cached file hash check, so we don't need to check it first
        foreach (var mirror in _data.ReleaseInfo.Mirrors)
        {
            SetStatus("Downloading", mirror.DownloadUrl, progressStyle: ProgressStyle.Indeterminate);
            
            _data.SPTZipInfo =
                await DownloadCacheHelper.GetOrDownloadFileAsync("SPT", mirror.DownloadUrl, progress, mirror.Hash);
            
            if (_data.SPTZipInfo != null)
            {
                return Result.FromSuccess();
            }
        }
        
        return Result.FromError("Download failed");
    }
    
    public override async Task<IResult> TaskOperation()
    {
        var progress = new Progress<double>((d) => { SetStatus(null, null, (int)Math.Floor(d)); });
        
        if (_data.PatchNeeded)
        {
            var buildResult = await BuildMirrorList();
            
            if (!buildResult.Succeeded)
            {
                return buildResult;
            }
            
            SetStatus(null, null, 0);
            
            var patcherDownloadRresult = await DownloadPatcherFromMirrors(progress);
            
            if (!patcherDownloadRresult.Succeeded)
            {
                return patcherDownloadRresult;
            }
        }
        
        return await DownloadSPTFromMirrors(progress);
    }
}