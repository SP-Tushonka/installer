using System.Collections.Generic;
using System.Linq;

namespace SPTInstaller.Models.Mirrors;

public class PatchManifest
{
    public List<PatchInfo> Patches { get; set; } = new();

    /// <summary>
    /// The fewest patches that turn the source client into the target client, applied in order
    /// </summary>
    /// <returns>The patches to apply, or null if no published patches connect the two</returns>
    public List<PatchInfo>? FindPath(int sourceClientVersion, int targetClientVersion)
    {
        var cameFrom = new Dictionary<int, PatchInfo>();
        var queue = new Queue<int>([sourceClientVersion]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current == targetClientVersion)
            {
                List<PatchInfo> path = [];

                while (current != sourceClientVersion)
                {
                    var patch = cameFrom[current];
                    path.Insert(0, patch);
                    current = patch.SourceClientVersion;
                }

                return path;
            }

            foreach (var patch in Patches.Where(patch => patch.SourceClientVersion == current))
            {
                if (patch.TargetClientVersion != sourceClientVersion && cameFrom.TryAdd(patch.TargetClientVersion, patch))
                {
                    queue.Enqueue(patch.TargetClientVersion);
                }
            }
        }

        return null;
    }
}
