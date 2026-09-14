using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CopilotUsageSimulator.BundleTool.Tests;

public sealed class GitHubReadClientTests
{
    [Fact]
    public async Task FollowsAllGrantPagesInsteadOfStoppingAtUniqueSeatCount()
    {
        using var handler = new QueueHandler(
            Response("""{"total_seats":1,"seats":[{"grant":1}]}""", "<https://api.github.com/enterprises/example/copilot/billing/seats?page=2>; rel=\"next\""),
            Response("""{"total_seats":1,"seats":[{"grant":2}]}"""));
        using var http = new HttpClient(handler);
        var client = new GitHubReadClient(http, "test-token");

        var result = await client.ReadArrayAsync("/enterprises/example/copilot/billing/seats", "seats", CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Pages);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer test-token", request.Authorization);
            Assert.Equal(GitHubReadClient.ApiVersion, request.ApiVersion);
        });
    }

    [Fact]
    public async Task ResourcePaginationHonorsHasNextPage()
    {
        using var handler = new QueueHandler(Response("""{"resources":[{"name":"alice"}],"has_next_page":true}"""),
            Response("""{"resources":[{"name":"bob"}],"has_next_page":false}"""));
        using var http = new HttpClient(handler);

        var result = await new GitHubReadClient(http, "test-token").ReadArrayAsync("/enterprises/example/settings/billing/cost-centers/cc", "resources", CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains("page=2", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task NeverSendsTokenToExternalPaginationHost()
    {
        using var handler = new QueueHandler(Response("[{\"name\":\"alice\"}]", "<https://untrusted.test/next>; rel=\"next\""));
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<ImportException>(() => new GitHubReadClient(http, "test-token")
            .ReadArrayAsync("/enterprises/example/teams", null, CancellationToken.None));

        Assert.Equal("github-host-rejected", error.Code);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("test-token", error.Message);
    }

    [Fact]
    public async Task RateLimitRetriesAreBoundedAndPermissionFailuresAreNotRetried()
    {
        var limited = Response("{}", status: HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        using var handler = new QueueHandler(limited, Response("[]"), Response("test-token in raw body", status: HttpStatusCode.Forbidden));
        using var http = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var client = new GitHubReadClient(http, "test-token", (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        Assert.Empty((await client.ReadArrayAsync("/enterprises/example/teams", null, CancellationToken.None)).Items);
        var error = await Assert.ThrowsAsync<ImportException>(() => client.ReadObjectAsync("/enterprises/example/teams", CancellationToken.None));

        Assert.Single(delays);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("github-http-403", error.Code);
        Assert.DoesNotContain("test-token", error.Message);
    }

    [Fact]
    public async Task MissingArrayIsNotEmptyArray()
    {
        using var handler = new QueueHandler(Response("{}"));
        using var http = new HttpClient(handler);

        Assert.Equal("github-schema-invalid", (await Assert.ThrowsAsync<ImportException>(() =>
            new GitHubReadClient(http, "test-token").ReadArrayAsync("/enterprises/example/copilot/billing/seats", "seats", CancellationToken.None))).Code);
    }

    [Theory]
    [InlineData("?page=2")]
    [InlineData("?user=alice&page=3")]
    [InlineData("?user=bob&page=2")]
    public async Task PaginationCannotDropFiltersOrSkipPages(string nextQuery)
    {
        using var handler = new QueueHandler(Response("[{\"user\":\"alice\"}]",
            $"<https://api.github.com/enterprises/example/budgets{nextQuery}>; rel=\"next\""));
        using var http = new HttpClient(handler);

        Assert.Equal("github-pagination-invalid", (await Assert.ThrowsAsync<ImportException>(() =>
            new GitHubReadClient(http, "test-token").ReadArrayAsync("/enterprises/example/budgets?user=alice", null, CancellationToken.None))).Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RepeatedContentsOnDifferentPageUrlsAreRejected()
    {
        const string page = """{"resources":[{"name":"alice"}],"has_next_page":true}""";
        using var handler = new QueueHandler(Response(page), Response(page));
        using var http = new HttpClient(handler);

        Assert.Equal("github-pagination-invalid", (await Assert.ThrowsAsync<ImportException>(() =>
            new GitHubReadClient(http, "test-token").ReadArrayAsync("/enterprises/example/cost-centers/cc", "resources", CancellationToken.None))).Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task AuthenticationFeatureAndRedirectFailuresDoNotBecomeEmptyData(HttpStatusCode status)
    {
        using var handler = new QueueHandler(Response("test-token", status: status));
        using var http = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<ImportException>(() => new GitHubReadClient(http, "test-token")
            .ReadObjectAsync("/enterprises/example/budgets", CancellationToken.None));

        Assert.Equal($"github-http-{(int)status}", exception.Code);
        Assert.DoesNotContain("test-token", exception.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ServerErrorsStopAfterThreeAttempts()
    {
        using var handler = new QueueHandler(Response("{}", status: HttpStatusCode.ServiceUnavailable),
            Response("{}", status: HttpStatusCode.ServiceUnavailable), Response("{}", status: HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);

        Assert.Equal("github-http-503", (await Assert.ThrowsAsync<ImportException>(() =>
            new GitHubReadClient(http, "test-token", (_, _) => Task.CompletedTask).ReadObjectAsync("/enterprises/example/budgets", CancellationToken.None))).Code);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task OversizedPageIsRejectedBeforeParsing()
    {
        using var handler = new QueueHandler(Response(new string(' ', 4 * 1024 * 1024 + 1)));
        using var http = new HttpClient(handler);

        Assert.Equal("github-response-too-large", (await Assert.ThrowsAsync<ImportException>(() =>
            new GitHubReadClient(http, "test-token").ReadObjectAsync("/enterprises/example/budgets", CancellationToken.None))).Code);
    }

    [Fact]
    public async Task CancellationStopsBeforeSendingCredentials()
    {
        using var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubReadClient(http, "test-token")
            .ReadArrayAsync("/enterprises/example/teams", null, cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    internal static HttpResponseMessage Response(string json, string? link = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (link is not null) response.Headers.Add("Link", link);
        return response;
    }

    internal sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<(HttpMethod Method, Uri Uri, string? Authorization, string? ApiVersion)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
                request.Headers.GetValues("X-GitHub-Api-Version").Single()));
            return Task.FromResult(_responses.Dequeue());
        }
    }
}