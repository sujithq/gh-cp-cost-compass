using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.BundleTool.Tests;

internal static class ImportFixture
{
    public static readonly DateTimeOffset Timestamp = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    public static EnterpriseSnapshot Snapshot() => new()
    {
        Schema = EnterpriseSnapshot.Format, SchemaVersion = 1,
        Enterprise = "example-enterprise", SelectedUser = "alice",
        CapturedAt = Timestamp, CaptureCompletedAt = Timestamp,
        Sources = new[] { "seats", "cost-centers", "teams", "budgets" }
            .Select(dataset => new SourceCoverage { Dataset = dataset, Endpoint = "fixture", Complete = true, Pages = 1 }).ToArray(),
        Seats =
        [
            new() { UserId = "101", UserLogin = "alice", PlanType = "business", Organization = "engineering" },
            new() { UserId = "102", UserLogin = "bob", PlanType = "enterprise", Organization = "engineering" }
        ],
        CostCenters =
        [
            new()
            {
                Id = "cc-engineering", Name = "Engineering", State = "active", AiCreditPoolEnabled = false,
                Resources = [new() { Type = "User", Name = "alice" }, new() { Type = "Organization", Name = "engineering" }]
            }
        ],
        Teams = [], Budgets = [], UserBudgetStates = []
    };

    public static ImportOverrides Overrides() => new()
    {
        SchemaVersion = 1, EnterprisePoolConsumedCredits = 5_740m,
        ExpectedPoolEntitlementCredits = 5_800m,
        EnterpriseBudgetExcludedCostCenterIds = [],
        PaidUsage = new() { State = GuardrailValue.Disabled }
    };

    public static WorkloadInput Workload() => new()
    {
        SchemaVersion = 1, OperationId = "chat",
        Calls = [new ModelCallInput { ModelId = "gpt-5.6-sol", ContextTokens = 90_000, FreshInputTokens = 50_000, OutputTokens = 40_000 }]
    };
}