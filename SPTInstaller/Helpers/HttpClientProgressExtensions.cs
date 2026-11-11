using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SPTInstaller.Helpers;

public static class HttpClientProgressExtensions
{
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(20);

    public static async Task<bool> DownloadDataAsync(this HttpClient client, string requestUrl, Stream destination,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(StallTimeout);

        try
        {
            using var response = await client.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            await using var download = await response.Content.ReadAsStreamAsync(stall.Token);

            var buffer = new byte[81920];
            long total = 0;

            while (true)
            {
                stall.CancelAfter(StallTimeout);

                var read = await download.ReadAsync(buffer, stall.Token);

                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;

                if (contentLength > 0)
                {
                    progress?.Report(total * 100d / contentLength.Value);
                }
            }

            return !contentLength.HasValue || total == contentLength.Value;
        }
        catch (OperationCanceledException) when (stall.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No data from {requestUrl} for {StallTimeout.TotalSeconds} s");
        }
    }
}
