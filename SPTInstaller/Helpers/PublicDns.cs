using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SPTInstaller.Helpers;

// DNS over HTTPS to the resolvers' own IPs, so an ISP that rewrites or intercepts port 53 cannot steer the lookup.
public static class PublicDns
{
    private static readonly string[] Resolvers =
    [
        "https://8.8.8.8/resolve",
        "https://8.8.4.4/resolve",
        "https://1.1.1.1/dns-query",
        "https://1.0.0.1/dns-query",
    ];

    private static readonly HttpClient _client =
        new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await ResolveAsync(host, cancellationToken);

        var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var lookups = Resolvers.Select(resolver => QueryAsync(resolver, host, race.Token)).ToList();

        while (lookups.Count > 0)
        {
            var finished = await Task.WhenAny(lookups);

            lookups.Remove(finished);

            if (finished.Result.Length > 0)
            {
                race.Cancel();
                Log.Information("Resolved {host} via public DNS: {addresses}", host,
                    string.Join(", ", finished.Result.Select(address => address.ToString())));

                return finished.Result;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        throw new HttpRequestException($"No public DNS resolver could resolve {host}");
    }

    private static async Task<IPAddress[]> QueryAsync(string resolver, string host, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get,
                $"{resolver}?name={Uri.EscapeDataString(host)}&type=A");
            request.Headers.Accept.ParseAdd("application/dns-json");

            using var response = await _client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            if (!json.RootElement.TryGetProperty("Answer", out var answers))
            {
                return [];
            }

            // Type 1 is an A record. The answer can also hold the CNAME chain that led to it.
            return answers.EnumerateArray()
                .Where(answer => answer.GetProperty("type").GetInt32() == 1)
                .Select(answer => IPAddress.Parse(answer.GetProperty("data").GetString()!))
                .ToArray();
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning("{resolver} could not resolve {host}: {message}", resolver, host, ex.Message);
            }

            return [];
        }
    }
}
