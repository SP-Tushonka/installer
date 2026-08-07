namespace SPTInstaller.Models;

public class InstallChannel
{
    public ReleaseInfo.ReleaseInfo Release { get; set; }
    public int MirrorIndex { get; set; }
    public string MirrorName { get; set; }

    public bool IsDefault => MirrorIndex == 0;
    public string DisplayName => IsDefault ? Release?.SPTVersion : $"{Release?.SPTVersion} ({MirrorName})";

    public override string ToString() => DisplayName;
}
