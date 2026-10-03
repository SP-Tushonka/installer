namespace SPTInstaller.Models.ReleaseInfo;

public class ReleaseInfoMirror
{
    public string DownloadUrl { get; set; }
    public string Hash { get; set; }
    public string? Sha256 { get; set; }
    public string Name { get; set; }
}