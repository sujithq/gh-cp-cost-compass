using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class EconomicGuardrailApplicabilityResolverTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly EconomicGuardrailApplicabilityResolver _resolver = new();

    [Fact]
    public void ResolvesOnlyApplicableEconomicRecords()
    {
        var attribution = Attribution();
        var snapshot = new EconomicGuardrailSnapshot
        {
            UserLevelBudgets =
            [
                Ulb("expired-ulb", "user-1", Timestamp.AddDays(-10), Timestamp.AddDays(-1)),
                Ulb("other-user-ulb", "user-2"),
                Ulb("current-ulb", "user-1")
            ],
            IncludedUsageControls =
            [
                Control("expired-control", "cc-1", Timestamp.AddDays(-10), Timestamp.AddDays(-1)),
                Control("other-control", "cc-2"),
                Control("current-control", "cc-1")
            ],
            SpendingBudgets =
            [
                Budget("expired-budget", SpendingBudgetScope.CostCenter, "cc-1") with
                {
                    EffectiveFrom = Timestamp.AddDays(-10),
                    EffectiveTo = Timestamp.AddDays(-1)
                },
                Budget("wrong-product", SpendingBudgetScope.CostCenter, "cc-1") with
                {
                    ProductIds = new HashSet<string> { "another-product" }
                },
                Budget("current-cost-center", SpendingBudgetScope.CostCenter, "cc-1"),
                Budget("organization-fallback", SpendingBudgetScope.Organization, "org-1"),
                Budget("enterprise", SpendingBudgetScope.Enterprise, null)
            ]
        };

        var ulb = _resolver.ResolveUserLevelBudget(
            snapshot,
            attribution,
            UserLevelBudgetKind.Individual,
            Timestamp);
        var control = _resolver.ResolveIncludedUsageControl(snapshot, attribution, Timestamp);
        var budgets = _resolver.ResolveSpendingBudgets(
            snapshot,
            attribution,
            "github-copilot",
            "copilot-ai-credits",
            Timestamp);

        Assert.Equal("current-ulb", ulb.Value!.Id);
        Assert.Equal("current-control", control.Value!.Id);
        Assert.Equal(
            ["current-cost-center", "enterprise"],
            budgets.Select(budget => budget.Id));
    }

    [Fact]
    public void ReportsSamePrecedenceUlbAmbiguity()
    {
        var snapshot = new EconomicGuardrailSnapshot
        {
            UserLevelBudgets =
            [
                Ulb("first", "user-1"),
                Ulb("second", "user-1")
            ]
        };

        var selection = _resolver.ResolveEffectiveUserLevelBudget(
            snapshot,
            Attribution(),
            Timestamp);

        Assert.True(selection.IsAmbiguous);
        Assert.Null(selection.Value);
        Assert.Equal(2, selection.Matches.Count);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(40, true)]
    public void SpendingBudgetAppliesFromTrackingBaselineOnward(
        int daysFromTrackingStart,
        bool expected)
    {
        var trackingStartedAt = Timestamp.AddDays(1);
        var snapshot = new EconomicGuardrailSnapshot
        {
            SpendingBudgets =
            [
                Budget("tracked-budget", SpendingBudgetScope.Enterprise, null) with
                {
                    TrackingStartedAt = trackingStartedAt
                }
            ]
        };

        var budgets = _resolver.ResolveSpendingBudgets(
            snapshot,
            Attribution(),
            "github-copilot",
            "copilot-ai-credits",
            trackingStartedAt.AddDays(daysFromTrackingStart));

        Assert.Equal(expected, budgets.Count == 1);
    }

    private static AttributionResult Attribution() =>
        new()
        {
            UserId = "user-1",
            CostCenterId = "cc-1",
            LicensingOrganizationId = "org-1",
            Rule = AttributionRule.DirectUser,
            Outcome = GuardrailOutcome.Passed,
            Explanation = "Test attribution."
        };

    private static UserLevelBudget Ulb(
        string id,
        string userId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null) =>
        new()
        {
            Id = id,
            Kind = UserLevelBudgetKind.Individual,
            TargetId = userId,
            LimitCredits = 100m,
            EffectiveFrom = from ?? DateTimeOffset.MinValue,
            EffectiveTo = to
        };

    private static CostCenterIncludedUsageControl Control(
        string id,
        string costCenterId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null) =>
        new()
        {
            Id = id,
            CostCenterId = costCenterId,
            EffectiveFrom = from ?? DateTimeOffset.MinValue,
            EffectiveTo = to
        };

    private static SpendingBudget Budget(
        string id,
        SpendingBudgetScope scope,
        string? scopeId) =>
        new()
        {
            Id = id,
            Scope = scope,
            ScopeId = scopeId,
            LimitUsd = 100m
        };
}
