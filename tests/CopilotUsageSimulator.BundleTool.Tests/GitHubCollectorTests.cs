using System.Net;
using System.Web;
using CopilotUsageSimulator.Engine.Configuration;
using static CopilotUsageSimulator.BundleTool.Tests.GitHubReadClientTests;

namespace CopilotUsageSimulator.BundleTool.Tests;

public sealed class GitHubCollectorTests
{
    [Fact]
    public async Task CollectsReplayableEvidenceWithoutUnrelatedPersonalData()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var result = await new GitHubSnapshotCollector(new GitHubReadClient(http, "test-token"), new FixedClock()).CollectAsync("Example", "Alice");

        Assert.All(result.Snapshot.Sources, source => Assert.True(source.Complete, source.Problem));
        Assert.Equal(3, result.Snapshot.Seats.Count);
        Assert.Equal("ent:developers", Assert.Single(result.Snapshot.Teams).Slug);
        Assert.Equal(9.5m, Assert.Single(result.Snapshot.UserBudgetStates).ConsumedAmount);
        Assert.Equal(999m, Assert.Single(result.Snapshot.Budgets).ConsumedAmount);
        Assert.Equal("universal", result.Snapshot.EffectiveUserBudget!.BudgetId);
        Assert.All(result.Snapshot.UsageReports, usage => Assert.Null(usage.ReportedThrough));
        var json = ImportJson.Write(result.Snapshot);
        Assert.DoesNotContain("test-token", json);
        Assert.DoesNotContain("private-email", json);
        Assert.DoesNotContain("avatar_url", json);
        var replayed = ImportJson.Read<EnterpriseSnapshot>(json);
        var bundle = new SnapshotAssembler().Create(replayed, ImportFixture.Workload(), ImportFixture.Overrides(), EngineConfigurationLoader.LoadDefault());
        Assert.Equal("universal", bundle.Report.FirstFailingGate);
    }

    [Fact]
    public async Task CollectsAggregateAndOneUsageReportPerUniqueSeatWhenRequested()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);

        var result = await new GitHubSnapshotCollector(new GitHubReadClient(http, "test-token"), new FixedClock())
            .CollectAsync("example", "alice", collectAllSeatUsage: true);

        var aiUsage = result.Snapshot.UsageReports.Where(report => report.Kind == "ai-credit").ToArray();
        Assert.Equal(3, aiUsage.Length);
        Assert.Null(aiUsage[0].User);
        Assert.Equal(["alice", "bob"], aiUsage.Skip(1).Select(report => report.User));
        Assert.Contains(result.Snapshot.Sources, source => source.Dataset == "ai-credit-usage:enterprise" && source.Complete);
        Assert.Contains(result.Snapshot.Sources, source => source.Dataset == "ai-credit-usage:user:bob" && source.Complete);
    }

    [Fact]
    public async Task MissingTeamPermissionPreservesPartialSnapshotButPreventsCreation()
    {
        using var handler = new FixtureHandler { FailedPath = "/enterprises/example/teams/ent:developers/memberships" };
        using var http = new HttpClient(handler);
        var result = await new GitHubSnapshotCollector(new GitHubReadClient(http, "test-token"), new FixedClock()).CollectAsync("example", "alice");

        Assert.False(result.Snapshot.Sources.Single(source => source.Dataset == "teams").Complete);
        Assert.Equal(403, result.Snapshot.Sources.Single(source => source.Dataset == "team-members:77").HttpStatus);
        Assert.NotEmpty(result.Snapshot.Seats);
        Assert.Equal("collection-incomplete", Assert.Throws<ImportException>(() => new SnapshotAssembler()
            .Create(result.Snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), EngineConfigurationLoader.LoadDefault())).Code);
    }

    [Fact]
    public async Task UsageFromAnotherMonthIsNotLabeledCurrent()
    {
        using var handler = new FixtureHandler { UsageMonth = 8 };
        using var http = new HttpClient(handler);

        var result = await new GitHubSnapshotCollector(new GitHubReadClient(http, "test-token"), new FixedClock()).CollectAsync("example", "alice");

        Assert.Empty(result.Snapshot.UsageReports);
        Assert.Contains(result.Report.Diagnostics, diagnostic => diagnostic.Message.Contains("usage-period-mismatch"));
    }

    [Fact]
    public async Task UniqueSeatCountMustMatchReportedInventory()
    {
        using var handler = new FixtureHandler { TotalSeats = 3 };
        using var http = new HttpClient(handler);

        var result = await new GitHubSnapshotCollector(new GitHubReadClient(http, "test-token"), new FixedClock()).CollectAsync("example", "alice");

        Assert.False(result.Snapshot.Sources.Single(source => source.Dataset == "seats").Complete);
        Assert.Contains(result.Report.Diagnostics, diagnostic => diagnostic.Message.Contains("seat-count-mismatch"));
    }

    internal sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ImportFixture.Timestamp;
    }

    internal sealed class FixtureHandler : HttpMessageHandler
    {
        public string? FailedPath { get; init; }
        public int UsageMonth { get; init; } = 9;
        public int TotalSeats { get; init; } = 2;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
            if (path == FailedPath) return Task.FromResult(Response("private-email test-token", status: HttpStatusCode.Forbidden));
            var json = path switch
            {
                "/enterprises/example/copilot/billing/seats" => $$"""
                    {"total_seats":{{TotalSeats}},"seats":[
                      {"assignee":{"id":101,"login":"alice","email":"private-email","avatar_url":"https://example.test/private"},"organization":{"login":"engineering"},"plan_type":"business"},
                      {"assignee":{"id":101,"login":"alice"},"organization":{"login":"engineering"},"assigning_team":{"slug":"ent:developers"},"plan_type":"business"},
                      {"assignee":{"id":102,"login":"bob"},"organization":{"login":"engineering"},"plan_type":"enterprise"}]}
                    """,
                "/enterprises/example/settings/billing/cost-centers" => """{"costCenters":[{"id":"cc-engineering","name":"Engineering","state":"active","resources":[]}]}""",
                "/enterprises/example/settings/billing/cost-centers/cc-engineering" => """
                    {"id":"cc-engineering","name":"Engineering","state":"active","ai_credit_pool_enabled":false,
                     "resources":[{"type":"User","name":"alice"},{"type":"Organization","name":"engineering"},{"type":"EnterpriseTeam","name":"ent:developers"}],"has_next_page":false}
                    """,
                "/enterprises/example/teams" => """[{"id":77,"slug":"ent:developers","name":"Developers","created_at":"2026-01-01T00:00:00Z"}]""",
                "/enterprises/example/teams/ent:developers/memberships" => """[{"id":102,"login":"bob","email":"private-email","avatar_url":"https://example.test/private"}]""",
                "/enterprises/example/settings/billing/budgets" when query["user"] == "alice" => """{"budgets":[],"user":"alice","effective_budget":{"id":"universal","budget_amount":10,"consumed_amount":9.5},"has_next_page":false}""",
                "/enterprises/example/settings/billing/budgets" => """
                    {"budgets":[{"id":"universal","budget_scope":"multi_user_customer","budget_type":"BundlePricing","budget_product_sku":"ai_credits",
                    "budget_amount":10,"consumed_amount":999,"prevent_further_usage":true,"budget_alerting":{"will_alert":false,"alert_recipients":["private-email"]}}],"has_next_page":false,"total_count":1}
                    """,
                "/enterprises/example/settings/billing/budgets/universal/user-states" when query["user"] == "alice" => """{"user_states":[{"user":"alice","consumed_amount":9.5,"target_amount":10}],"has_next_page":false,"total_count":1}""",
                "/enterprises/example/settings/billing/ai_credit/usage" => $$"""{"enterprise":"example","user":"{{query["user"]}}","timePeriod":{"year":2026,"month":{{UsageMonth}}},"usageItems":[]}""",
                "/enterprises/example/settings/billing/usage/summary" => $$"""{"enterprise":"example","timePeriod":{"year":2026,"month":{{UsageMonth}}},"usageItems":[]}""",
                _ => throw new InvalidOperationException($"Unexpected fixture request: {request.RequestUri}")
            };
            return Task.FromResult(Response(json));
        }
    }
}