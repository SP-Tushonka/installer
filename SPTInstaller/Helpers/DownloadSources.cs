using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace SPTInstaller.Helpers;

/// <summary>
/// Picks the mirror each chunk comes from, by the speed earlier chunks got or the mirror the user chose
/// </summary>
public static class DownloadSources
{
    private const double Smoothing = 0.3;

    // One chunk in this many goes to a slower mirror, so one that speeds up again is noticed
    private const int ExploreEvery = 10;

    // A mirror that just failed sits out this long in automatic mode, so chunks don't keep hitting it
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    private static readonly object Lock = new();
    private static readonly Dictionary<string, double> _speeds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> _downUntil = new(StringComparer.OrdinalIgnoreCase);
    private static int _picks;

    private static string? _preferred;

    /// <summary>
    /// The mirror name to download from, null to pick per chunk. A change applies from the next chunk.
    /// </summary>
    public static string? Preferred
    {
        get => _preferred;
        set
        {
            _preferred = value;
            Log.Information("Download source: {source}", value ?? "automatic");
        }
    }

    public static void Name(string url, string name)
    {
        lock (Lock)
        {
            _names[Host(url)] = name;
        }
    }

    /// <summary>
    /// The urls a chunk may use. A chosen mirror is honoured exactly while it is one of them, even when it fails.
    /// </summary>
    public static List<string> Candidates(IReadOnlyList<string> urls)
    {
        var preferred = Preferred;

        if (preferred != null)
        {
            lock (Lock)
            {
                var chosen = urls.Where(url => _names.GetValueOrDefault(Host(url)) == preferred).ToList();

                if (chosen.Count > 0)
                {
                    return chosen;
                }
            }
        }

        return [.. urls];
    }

    public static string Pick(List<string> candidates)
    {
        lock (Lock)
        {
            var pick = _picks++;
            var available = candidates.Where(url => !IsDown(Host(url))).ToList();

            if (available.Count == 0)
            {
                available = candidates;
            }

            // Until every mirror has a measured chunk they take turns
            if (available.Any(url => !_speeds.ContainsKey(Host(url))))
            {
                return available[pick % available.Count];
            }

            var bySpeed = available.OrderByDescending(url => _speeds[Host(url)]).ToList();

            return pick % ExploreEvery == ExploreEvery - 1 ? bySpeed[^1] : bySpeed[0];
        }
    }

    public static void Report(string url, long bytes, TimeSpan elapsed)
    {
        var speed = bytes / Math.Max(elapsed.TotalSeconds, 0.001);

        lock (Lock)
        {
            var host = Host(url);
            _speeds[host] = _speeds.TryGetValue(host, out var previous) ? previous + Smoothing * (speed - previous) : speed;
            _downUntil.Remove(host);
        }
    }

    public static void Failed(string url)
    {
        lock (Lock)
        {
            var host = Host(url);

            if (_speeds.TryGetValue(host, out var previous))
            {
                _speeds[host] = previous / 2;
            }

            _downUntil[host] = DateTime.UtcNow + Cooldown;
        }
    }

    private static bool IsDown(string host) => _downUntil.TryGetValue(host, out var until) && until > DateTime.UtcNow;

    /// <summary>
    /// Mirrors ordered fastest first by what chunks got so far, for files that come from a single mirror
    /// </summary>
    public static IEnumerable<T> Order<T>(IEnumerable<T> items, Func<T, string> urlOf)
    {
        lock (Lock)
        {
            var preferred = Preferred;

            return items
                .OrderByDescending(item => preferred != null && _names.GetValueOrDefault(Host(urlOf(item))) == preferred)
                .ThenByDescending(item => _speeds.GetValueOrDefault(Host(urlOf(item))))
                .ToList();
        }
    }

    public static readonly TimeSpan SpeedWindow = TimeSpan.FromSeconds(5);

    private static readonly Dictionary<string, long> _received = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<(DateTime Time, Dictionary<string, long> Received)> _snapshots = new();

    public static void Received(string url, long bytes)
    {
        lock (Lock)
        {
            var host = Host(url);
            _received[host] = _received.GetValueOrDefault(host) + bytes;
        }
    }

    /// <summary>
    /// What each mirror delivered over the last <see cref="SpeedWindow"/>, like "Main 32 MB/s · Mirror 12 MB/s"
    /// </summary>
    public static string TakeSummary()
    {
        lock (Lock)
        {
            var now = DateTime.UtcNow;

            while (_snapshots.Count > 1 && now - _snapshots.Peek().Time > SpeedWindow)
            {
                _snapshots.Dequeue();
            }

            if (_snapshots.Count == 0)
            {
                _snapshots.Enqueue((now, new Dictionary<string, long>(_received, StringComparer.OrdinalIgnoreCase)));
                return "";
            }

            var (since, before) = _snapshots.Peek();
            var seconds = Math.Max((now - since).TotalSeconds, 0.001);

            var text = string.Join(" · ", _received
                .Select(pair => (Name: _names.GetValueOrDefault(pair.Key, pair.Key), Speed: (pair.Value - before.GetValueOrDefault(pair.Key)) / seconds))
                .Where(entry => entry.Speed > 0)
                .Select(entry => $"{entry.Name} {DirectorySizeHelper.SizeSuffix((long)entry.Speed, 0)}/s")
                .Concat(_downUntil.Keys.Where(IsDown).Select(host => $"{_names.GetValueOrDefault(host, host)} not responding")));

            _snapshots.Enqueue((now, new Dictionary<string, long>(_received, StringComparer.OrdinalIgnoreCase)));

            return text;
        }
    }

    private static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Authority : url;
}
