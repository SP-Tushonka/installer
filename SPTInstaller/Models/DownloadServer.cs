namespace SPTInstaller.Models;

public class DownloadServer
{
    public required string Name { get; init; }

    // Null for the automatic choice
    public InstallChannel? Channel { get; init; }

    public override string ToString() => Name;
}
