namespace SPTInstaller.Models.Mirrors;

public class PatchInfoMirror
{
    public string Name { get; set; }
    /// <summary>The patch document. Blobs are at blobs/&lt;hash&gt; next to it.</summary>
    public string Document { get; set; }
}
