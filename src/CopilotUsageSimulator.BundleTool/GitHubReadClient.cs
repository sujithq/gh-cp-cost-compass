using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace CopilotUsageSimulator.BundleTool;

public sealed record GitHubArrayResult(IReadOnlyList<JsonElement> Items, JsonElement FirstPage, int Pages);

public sealed class GitHubReadClient
{
    public const string ApiVersion = "2026-03-10";
    private static readonly Uri ApiRoot = new("https://api.github.com/");
    private const int PageSize = 100;
    private const int MaximumPages = 1000;
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly string _token;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private int _requestCount;
    public int CompletedPages { get; private set; }

    public GitHubReadClient(HttpClient http, string token, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
            throw new ImportException("token-invalid", "A nonblank token without whitespace must be supplied through the selected environment variable.", 3);
        _http = http;
        _token = token;
        _delay = delay ?? Task.Delay;
    }

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<JsonElement> ReadObjectAsync(string path, CancellationToken cancellationToken) =>
        (await ReadPageAsync(new Uri(ApiRoot, path), cancellationToken)).Body;

    public async Task<GitHubArrayResult> ReadArrayAsync(
        string path, string? arrayProperty, CancellationToken cancellationToken, bool paginate = true)
    {
        var initial = new Uri(ApiRoot, path);
        var current = paginate ? PageUri(initial, 1) : initial;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pageContents = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<JsonElement>();
        var pages = 0;
        var totalBytes = 0;
        JsonElement first = default;
        while (true)
        {
            if (++pages > MaximumPages || !visited.Add(current.AbsoluteUri))
                throw Failure("github-pagination-invalid", initial, "Pagination repeated a page or exceeded its bound.");
            var result = await ReadPageAsync(current, cancellationToken);
            totalBytes += result.Bytes;
            if (totalBytes > ImportFiles.MaximumSnapshotBytes)
                throw Failure("github-response-too-large", initial, "The paginated dataset exceeds the snapshot input bound.");
            if (pages == 1) first = result.Body;
            var array = arrayProperty is null ? result.Body : RequiredProperty(result.Body, arrayProperty);
            if (array.ValueKind != JsonValueKind.Array)
                throw Failure("github-schema-invalid", initial, "Expected a JSON array, not a missing collection.");
            if (array.GetArrayLength() > 0 && !pageContents.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(array.GetRawText())))))
                throw Failure("github-pagination-invalid", initial, "Repeated page contents cannot establish a complete inventory.");
            if (pages > 1 && first.ValueKind == JsonValueKind.Object && result.Body.ValueKind == JsonValueKind.Object)
            {
                foreach (var countField in new[] { "total_seats", "total_count" })
                {
                    if (first.TryGetProperty(countField, out var firstCount) &&
                        (!result.Body.TryGetProperty(countField, out var count) || !JsonElement.DeepEquals(firstCount, count)))
                        throw Failure("github-inventory-changed", initial, "Reported inventory totals changed during pagination; recollect the snapshot.");
                }
            }
            items.AddRange(array.EnumerateArray().Select(value => value.Clone()));
            if (!paginate)
            {
                if (result.Next is not null || (result.Body.ValueKind == JsonValueKind.Object &&
                    result.Body.TryGetProperty("has_next_page", out var more) && more.ValueKind != JsonValueKind.False))
                    throw Failure("github-pagination-invalid", initial, "An unpaged endpoint reported additional pages.");
                break;
            }

            bool? hasNext = null;
            if (result.Body.ValueKind == JsonValueKind.Object && result.Body.TryGetProperty("has_next_page", out var flag))
            {
                if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw Failure("github-schema-invalid", initial, "has_next_page must be a boolean.");
                hasNext = flag.GetBoolean();
            }
            if (hasNext == false && result.Next is not null)
                throw Failure("github-pagination-invalid", initial, "Conflicting pagination signals.");
            if (result.Next is { } next)
            {
                ValidateUri(next);
                if (next.AbsolutePath != initial.AbsolutePath)
                    throw Failure("github-pagination-invalid", initial, "Pagination changed resource paths.");
                var nextQuery = HttpUtility.ParseQueryString(next.Query);
                var initialQuery = HttpUtility.ParseQueryString(initial.Query);
                if (!int.TryParse(nextQuery["page"], out var nextPage) || nextPage != pages + 1 ||
                    initialQuery.AllKeys.Any(key => nextQuery[key] != initialQuery[key]) ||
                    nextQuery.AllKeys.Any(key => key is not ("page" or "per_page") && initialQuery.GetValues(key) is null) ||
                    (nextQuery["per_page"] is { } perPage && perPage != PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    throw Failure("github-pagination-invalid", initial, "Pagination changed filters, page size or the expected page sequence.");
                current = PageUri(initial, nextPage);
            }
            else if (hasNext == true || (hasNext is null && array.GetArrayLength() >= PageSize))
            {
                current = PageUri(initial, pages + 1);
            }
            else break;
            if (array.GetArrayLength() == 0)
                throw Failure("github-pagination-invalid", initial, "An empty page claims more results.");
        }
        return new GitHubArrayResult(items, first, pages);
    }

    private async Task<Page> ReadPageAsync(Uri uri, CancellationToken cancellationToken)
    {
        ValidateUri(uri);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            return await ReadPageCoreAsync(uri, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure("github-timeout", uri, "The response did not complete within the bounded page deadline.");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw Failure("github-transport-failed", uri, "The response stream failed; raw transport details are not logged.");
        }
    }

    private async Task<Page> ReadPageCoreAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_requestCount > 4096) throw Failure("github-request-limit", uri, "Collection exceeded its request bound.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("compass-bundle", "0.2.0"));
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException ||
                (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                if (attempt == 2) throw Failure("github-transport-failed", uri, "The GET failed after bounded retries; credentials and response bodies are not logged.");
                await _delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken);
                continue;
            }
            using (response)
            {
                var rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (response.StatusCode == HttpStatusCode.Forbidden &&
                     (response.Headers.RetryAfter is not null ||
                      (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0"))));
                var transient = rateLimited || (int)response.StatusCode >= 500;
                if (transient && attempt < 2)
                {
                    var retry = response.Headers.RetryAfter?.Delta ??
                        (response.Headers.RetryAfter?.Date is { } retryAt ? retryAt - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(attempt + 1));
                    if (retry > TimeSpan.FromSeconds(30))
                        throw Failure("github-rate-limited", uri, "Retry-After exceeds 30 seconds. Retry collection after the server's reset window.");
                    await _delay(retry < TimeSpan.Zero ? TimeSpan.Zero : retry, cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    var code = rateLimited ? "github-rate-limited" : $"github-http-{(int)response.StatusCode}";
                    throw Failure(code, uri,
                        $"HTTP {(int)response.StatusCode}. Check endpoint permissions, SSO authorization and feature availability; this is not an empty dataset.", (int)response.StatusCode);
                }
                if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                    throw Failure("github-response-too-large", uri, "Response exceeded the per-page byte limit.");
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + read > MaximumResponseBytes)
                        throw Failure("github-response-too-large", uri, "Response exceeded the per-page byte limit.");
                    buffer.Write(chunk, 0, read);
                }
                try
                {
                    using var document = JsonDocument.Parse(buffer.ToArray());
                    CompletedPages++;
                    return new Page(document.RootElement.Clone(), NextLink(response, uri), checked((int)buffer.Length));
                }
                catch (JsonException)
                {
                    throw Failure("github-schema-invalid", uri, "Response was not valid JSON; raw response content is not logged.");
                }
            }
        }
        throw Failure("github-transport-failed", uri, "The GET did not complete.");
    }

    private static Uri? NextLink(HttpResponseMessage response, Uri current)
    {
        if (!response.Headers.TryGetValues("Link", out var links)) return null;
        Uri? next = null;
        foreach (var link in links)
        {
            var matches = Regex.Matches(link, "<(?<url>[^>]+)>\\s*;\\s*rel=\"(?<rel>[^\"]+)\"");
            if (matches.Count == 0) throw Failure("github-pagination-invalid", current, "Unsupported Link header format.");
            foreach (Match match in matches)
            {
                if (!match.Groups["rel"].Value.Split(' ').Contains("next", StringComparer.OrdinalIgnoreCase)) continue;
                if (next is not null || !Uri.TryCreate(current, match.Groups["url"].Value, out next))
                    throw Failure("github-pagination-invalid", current, "Ambiguous or invalid next-page link.");
            }
        }
        return next;
    }

    private static Uri PageUri(Uri uri, int page)
    {
        var query = uri.Query.TrimStart('?');
        return new UriBuilder(uri) { Query = $"{(query.Length == 0 ? "" : query + "&")}per_page={PageSize}&page={page}" }.Uri;
    }

    private static void ValidateUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !ImportChecks.Same(uri.Host, ApiRoot.Host) ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ImportException("github-host-rejected", "Authenticated requests and pagination are restricted to https://api.github.com.", 3);
    }

    internal static JsonElement RequiredProperty(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            throw new ImportException("github-schema-invalid", $"Required GitHub response field '{name}' is missing.", 3);
        return value;
    }

    private static ImportException Failure(string code, Uri uri, string reason, int? status = null) => new(code, $"GET {uri.AbsolutePath}: {reason}", 3, status);
    private sealed record Page(JsonElement Body, Uri? Next, int Bytes);
}