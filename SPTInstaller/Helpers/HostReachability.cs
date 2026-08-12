using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SPTInstaller.Helpers;

public static class HostReachability
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private static readonly ConcurrentDictionary<string, Task<bool>> _probes = new(StringComparer.OrdinalIgnoreCase);

    public static Task<bool> IsReachableAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Task.FromResult(false);
        }

        return _probes.GetOrAdd(uri.GetLeftPart(UriPartial.Authority), ProbeAsync);
    }

    public static async Task<List<T>> KeepReachableAsync<T>(IReadOnlyList<T> items, Func<T, string> urlOf)
    {
        var probes = new Task<bool>[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            probes[i] = IsReachableAsync(urlOf(items[i]));
        }

        await Task.WhenAll(probes);

        List<T> reachable = [];

        for (var i = 0; i < items.Count; i++)
        {
            if (probes[i].Result)
            {
                reachable.Add(items[i]);
            }
        }

        if (reachable.Count == 0)
        {
            Log.Warning("No download host answered, offering every option anyway");

            return [.. items];
        }

        if (reachable.Count < items.Count)
        {
            Log.Information("Hiding {count} of {total} download option(s), their host did not answer",
                items.Count - reachable.Count, items.Count);
        }

        return reachable;
    }

    private static async Task<bool> ProbeAsync(string authority)
    {
        List<Task<bool>> attempts =
        [
            AnswersAsync(DownloadCacheHelper._httpClient, authority),
            AnswersAsync(DownloadCacheHelper._directHttpClient, authority),
        ];

        while (attempts.Count > 0)
        {
            var finished = await Task.WhenAny(attempts);

            attempts.Remove(finished);

            if (finished.Result)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> AnswersAsync(HttpClient client, string authority)
    {
        using CancellationTokenSource timeout = new(ProbeTimeout);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Head, authority);
            using var response = await client.SendAsync(request, timeout.Token);
            Log.Debug("{authority} answered", authority, (int)response.StatusCode);

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning("{authority} did not answer: {message}", authority, ex.Message);

            return false;
        }
    }
}
