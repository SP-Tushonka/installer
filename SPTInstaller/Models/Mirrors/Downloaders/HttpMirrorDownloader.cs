using SPTInstaller.Helpers;
using System.Threading.Tasks;

namespace SPTInstaller.Models.Mirrors.Downloaders;

public class HttpMirrorDownloader : MirrorDownloaderBase
{
    public HttpMirrorDownloader(PatchInfoMirror mirror) : base(mirror)
    {
    }
    
    public override Task<FileInfo?> Download(IProgress<double> progress)
        => DownloadCacheHelper.DownloadFileAsync("patcher", [MirrorInfo.Link], progress);
}