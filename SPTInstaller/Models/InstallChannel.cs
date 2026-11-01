namespace SPTInstaller.Models;

public class InstallChannel
{
    public ReleaseInfo.ReleaseInfo Release { get; set; }
    public int MirrorIndex { get; set; }
    public string MirrorName { get; set; }

    public bool IsLegacy => Release?.Legacy == true;
}
