using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Nodus.Agent.Pipeline;

/// <summary>
/// The model sometimes invents links. We check each one with an HTTP request.
/// Only links that are definitely dead are removed (404/410, domain does not exist).
/// 403 and timeouts are kept: many sites simply block bots.
/// </summary>
public sealed class HttpLinkChecker(HttpClient http, ILogger<HttpLinkChecker> logger) : ILinkChecker
{
    public async Task<IReadOnlySet<string>> FindDeadLinksAsync(IEnumerable<string> urls, CancellationToken ct)
    {
        var dead = new ConcurrentDictionary<string, bool>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct };

        await Parallel.ForEachAsync(urls.Distinct(), options, async (url, token) =>
        {
            if (!IsSafeToFetch(url) || await IsDeadAsync(url, token))
            {
                dead[url] = true;
            }
        });

        if (!dead.IsEmpty)
        {
            logger.LogInformation("Removed dead links: {Count}", dead.Count);
        }

        return dead.Keys.ToHashSet();
    }

    /// <summary>
    /// The links come from the model, which has been reading third-party sites. Never let them
    /// reach the server's internal network (localhost, IP addresses): SSRF protection.
    /// </summary>
    public static bool IsSafeToFetch(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.HostNameType == UriHostNameType.Dns
        && uri.Host.Contains('.')
        && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        && !uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsDeadAsync(string url, CancellationToken ct)
    {
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, url);
                using var getResponse = await http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct);
                return getResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
            }

            return response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
        }
        catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.NameResolutionError)
        {
            return true; // the domain does not exist
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false; // not sure — keep it
        }
    }
}

/// <summary>For Fake mode and tests: checks nothing.</summary>
public sealed class NoopLinkChecker : ILinkChecker
{
    public Task<IReadOnlySet<string>> FindDeadLinksAsync(IEnumerable<string> urls, CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
}
