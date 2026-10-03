using System.Collections.Generic;
using SPTInstaller.Models.Mirrors;
using SPTInstaller.Models.ReleaseInfo;

namespace SPTInstaller.Models;

public class InternalData
{
    public bool DebugMode { get; set; } = false;
    /// <summary>
    /// The folder to install SPT into
    /// </summary>
    public string? TargetInstallPath { get; set; }
    
    /// <summary>
    /// The orginal EFT game path
    /// </summary>
    public string? OriginalGamePath { get; set; }
    
    /// <summary>
    /// The original EFT game version
    /// </summary>
    public string OriginalGameVersion { get; set; }
    
    /// <summary>
    /// Patcher archives, one per step of <see cref="PatchChain"/>
    /// </summary>
    public List<FileInfo> PatcherZips { get; } = [];
    
    /// <summary>
    /// SPT zip file info
    /// </summary>
    public FileInfo SPTZipInfo { get; set; }
    
    /// <summary>
    /// The release information from release.json
    /// </summary>
    public ReleaseInfo.ReleaseInfo ReleaseInfo { get; set; }
    
    /// <summary>
    /// The patches that turn the live client into the release's client, applied in order
    /// </summary>
    public List<PatchInfo> PatchChain { get; set; } = [];

    /// <summary>
    /// Which published release the user picked, and whether to prefer the main download or the mirror.
    /// </summary>
    public InstallChannel SelectedChannel { get; set; }
    
    /// <summary>
    /// Whether or not a patch is needed to downgrade the client files
    /// </summary>
    public bool PatchNeeded => PatchChain.Count > 0;
}