using CopilotUsageSimulator.Bundles;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.BundleTool.Tests;

public sealed class SnapshotAssemblerTests
{
    private readonly SnapshotAssembler _assembler = new();
    private readonly EngineConfiguration _catalog = EngineConfigurationLoader.LoadDefault();

    [Fact]
    public void CreatesExistingV1BundleAndPreservesBlockedPreview()
    {
        var snapshot = ImportFixture.Snapshot();
        var before = ImportJson.Write(snapshot);

        var first = _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog);
        var second = _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog);
        var imported = new CompassBundleCodec(new ScenarioJson()).Import(first.Json, _catalog);

        Assert.Equal(first.Json, second.Json);
        Assert.Equal(before, ImportJson.Write(snapshot));
        Assert.Equal("Blocked", first.Report.PreviewDecision);
        Assert.Equal("paid-usage", first.Report.FirstFailingGate);
        Assert.Equal(2, imported.Scenario.BillingContext!.SeatAssignments.Count);
        Assert.Equal(5_740m, imported.Scenario.EconomicGuardrails!.EnterprisePoolConsumedCredits);
        Assert.Equal(CompassBundleCodec.CatalogHash(_catalog), first.Report.CatalogSha256);
    }

    [Fact]
    public void MissingPaidPolicyStaysUnknownNotDisabledOrEnabled()
    {
        var result = _assembler.Create(ImportFixture.Snapshot(), ImportFixture.Workload(),
            ImportFixture.Overrides() with { PaidUsage = null }, _catalog);

        Assert.Equal("Indeterminate", result.Report.PreviewDecision);
        Assert.Equal("paid-usage.unknown", result.Report.FirstFailingGate);
        Assert.Contains(result.Report.Diagnostics, issue => issue.Code == "paid-usage-unknown");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DoesNotInferMissingPoolValuesFromDiscounts(bool consumed)
    {
        var overrides = consumed ? ImportFixture.Overrides() with { EnterprisePoolConsumedCredits = null }
            : ImportFixture.Overrides() with { ExpectedPoolEntitlementCredits = null };

        Assert.Equal("missing-financial-value", Assert.Throws<ImportException>(() =>
            _assembler.Create(ImportFixture.Snapshot(), ImportFixture.Workload(), overrides, _catalog)).Code);
    }

    [Fact]
    public void EntitlementMismatchIsNotHiddenByDiscardingSeats()
    {
        var overrides = ImportFixture.Overrides() with { ExpectedPoolEntitlementCredits = 1_900m };

        Assert.Equal("pool-entitlement-mismatch", Assert.Throws<ImportException>(() =>
            _assembler.Create(ImportFixture.Snapshot(), ImportFixture.Workload(), overrides, _catalog)).Code);
    }

    [Fact]
    public void UniversalBudgetUsesUserStateNotGroupConsumption()
    {
        var snapshot = ImportFixture.Snapshot() with
        {
            Budgets = [Budget("universal", "multi_user_customer", 10m, 999m)],
            UserBudgetStates = [new() { BudgetId = "universal", User = "alice", ConsumedAmount = 9.5m }]
        };

        var result = _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog);
        var imported = new CompassBundleCodec(new ScenarioJson()).Import(result.Json, _catalog);
        var ulb = Assert.Single(imported.Scenario.EconomicGuardrails!.UserLevelBudgets);

        Assert.Equal(1_000m, ulb.LimitCredits);
        Assert.Equal(950m, ulb.ConsumedCredits);
        Assert.Equal("universal", result.Report.FirstFailingGate);
    }

    [Fact]
    public void MissingUserStateIsNotZero()
    {
        var snapshot = ImportFixture.Snapshot() with { Budgets = [Budget("universal", "multi_user_customer", 10m, 999m)] };

        Assert.Equal("missing-financial-value", Assert.Throws<ImportException>(() =>
            _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog)).Code);
    }

    [Fact]
    public void EffectiveBudgetIdentityCannotOverrideEnginePrecedence()
    {
        var snapshot = ImportFixture.Snapshot() with
        {
            Budgets = [Budget("universal", "multi_user_customer", 10m, 999m), Budget("individual", "user", 20m, 0m) with { User = "alice" }],
            EffectiveUserBudget = new() { BudgetId = "universal", User = "alice", ConsumedAmount = 0m, TargetAmount = 10m }
        };

        Assert.Equal("effective-user-budget-mismatch", Assert.Throws<ImportException>(() =>
            _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog)).Code);
    }

    [Fact]
    public void ContradictoryPerUserTargetsRequireAnExplicitConfirmation()
    {
        var snapshot = ImportFixture.Snapshot() with
        {
            Budgets = [Budget("universal", "multi_user_customer", 10m, 999m)],
            UserBudgetStates = [new() { BudgetId = "universal", User = "alice", ConsumedAmount = 0m, TargetAmount = 20m }]
        };

        Assert.Equal("user-budget-limit-mismatch", Assert.Throws<ImportException>(() =>
            _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog)).Code);
    }

    [Theory]
    [InlineData("2026-09-11", "universal")]
    [InlineData("2026-09-12", "individual")]
    public void ExpiredIndividualBudgetFallsBackAtUtcBoundary(string expiration, string effectiveId)
    {
        var snapshot = ImportFixture.Snapshot() with
        {
            Budgets = [Budget("universal", "multi_user_customer", 10m, 999m), Budget("individual", "user", 20m, 0m) with { User = "alice", ExpiresAt = DateOnly.Parse(expiration) }],
            UserBudgetStates = [new() { BudgetId = "universal", User = "alice", ConsumedAmount = 0m }]
        };

        var created = _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog);
        var scenario = new CompassBundleCodec(new ScenarioJson()).Import(created.Json, _catalog).Scenario;
        var attribution = new AttributionResolver().Resolve(scenario.Attribution!, scenario.Timestamp);
        Assert.Equal(effectiveId, new EconomicGuardrailApplicabilityResolver().ResolveEffectiveUserLevelBudget(scenario.EconomicGuardrails!, attribution, scenario.Timestamp).Value!.Id);
    }

    [Fact]
    public void OfflineUsagePeriodCannotBeRelabeledAsCaptureMonth()
    {
        var snapshot = ImportFixture.Snapshot() with
        {
            UsageReports = [new() { Kind = "ai-credit", Year = 2026, Month = 8, User = "alice", Items = [] }]
        };

        Assert.Equal("usage-period-mismatch", Assert.Throws<ImportException>(() =>
            _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog)).Code);
    }

    [Fact]
    public void DuplicateUsageReportsAreRejected()
    {
        var usage = new UsageObservation { Kind = "ai-credit", Year = 2026, Month = 9, User = "alice", Items = [] };
        var snapshot = ImportFixture.Snapshot() with { UsageReports = [usage, usage] };

        Assert.Equal("duplicate-usage-report", Assert.Throws<ImportException>(() =>
            _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog)).Code);
    }

    [Fact]
    public void UsageForAnUnobservedUserIsRejected()
    {
        var snapshot = ImportFixture.Snapshot() with
        {
            UsageReports = [new() { Kind = "ai-credit", Year = 2026, Month = 9, User = "mallory", Items = [] }]
        };

        Assert.Equal("usage-period-mismatch", Assert.Throws<ImportException>(() =>
            _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog)).Code);
    }

    [Fact]
    public void CloudAgentUsesSeparateConfirmedActionsAllowanceAndBudget()
    {
        var snapshot = ImportFixture.Snapshot() with
        {
            Budgets = [Budget("actions", "enterprise", 0m, 0m) with { BudgetType = "ProductPricing", ProductSku = "actions" }]
        };
        var workload = ImportFixture.Workload() with
        {
            OperationId = "cloud-agent",
            Actions = new() { Account = "actions-payer", Minutes = 5m, IncludedMinutesRemaining = 0m, ApplicableBudgetIds = ["actions"], ApplicableBudgetsConfirmed = true }
        };
        var confirmations = ImportFixture.Overrides() with { PaidUsage = new() { State = GuardrailValue.Enabled } };

        var created = _assembler.Create(snapshot, workload, confirmations, _catalog);
        var scenario = new CompassBundleCodec(new ScenarioJson()).Import(created.Json, _catalog).Scenario;

        Assert.Equal("Blocked", created.Report.PreviewDecision);
        Assert.Contains("actions", created.Report.FirstFailingGate);
        Assert.Equal(0m, scenario.ActionsGuardrails!.IncludedMinutes);
        Assert.Equal(5740m, scenario.EconomicGuardrails!.EnterprisePoolConsumedCredits);
        Assert.Equal("actions-payer", scenario.Metadata["actionsAccount"]);
    }

    [Fact]
    public void HardAndAlertOnlyBudgetsKeepDistinctEnforcement()
    {
        var snapshot = ImportFixture.Snapshot() with
        {
            Budgets = [Budget("hard", "enterprise", .20m, 0m), Budget("alert", "enterprise", .10m, .05m) with { PreventFurtherUsage = false }]
        };
        var overrides = ImportFixture.Overrides() with { PaidUsage = new() { State = GuardrailValue.Enabled } };

        var result = _assembler.Create(snapshot, ImportFixture.Workload(), overrides, _catalog);

        Assert.Equal("Blocked", result.Report.PreviewDecision);
        Assert.Equal("hard", result.Report.FirstFailingGate);
    }

    [Fact]
    public void IncludedControlNeedsExplicitCreditUnitsAndOverflow()
    {
        var snapshot = ImportFixture.Snapshot();
        snapshot = snapshot with { CostCenters = [snapshot.CostCenters[0] with { AiCreditPoolEnabled = true, TargetAmount = 58m, CurrentAmount = 57.4m }] };

        Assert.Equal("missing-financial-value", Assert.Throws<ImportException>(() =>
            _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog)).Code);

        var overrides = ImportFixture.Overrides() with
        {
            IncludedControls = new Dictionary<string, IncludedControlConfirmation>
            {
                ["cc-engineering"] = new() { EntitlementCredits = 5_800m, ConsumedCredits = 5_740m, OverflowBehavior = IncludedOverflowBehavior.Block }
            }
        };
        var result = _assembler.Create(snapshot, ImportFixture.Workload(), overrides, _catalog);
        Assert.Equal("included-cc-engineering", result.Report.FirstFailingGate);
    }

    [Fact]
    public void UnsupportedProfileDateCannotBeBackdatedAutomatically()
    {
        var timestamp = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var snapshot = ImportFixture.Snapshot() with { CapturedAt = timestamp, CaptureCompletedAt = timestamp };

        Assert.Equal("compass-date-unsupported", Assert.Throws<ImportException>(() =>
            _assembler.Create(snapshot, ImportFixture.Workload(), ImportFixture.Overrides(), _catalog)).Code);
    }

    [Fact]
    public void FullScopeCannotInventPassingAccessGates()
    {
        var workload = ImportFixture.Workload() with { CheckScope = SimulationCheckScope.All };

        Assert.Equal("runtime-unconfirmed", Assert.Throws<ImportException>(() =>
            _assembler.Create(ImportFixture.Snapshot(), workload, ImportFixture.Overrides(), _catalog)).Code);
    }

    internal static BudgetObservation Budget(string id, string scope, decimal limit, decimal consumed) => new()
    {
        Id = id, Scope = scope, BudgetType = "BundlePricing", ProductSku = "ai_credits",
        BudgetAmount = limit, ConsumedAmount = consumed, PreventFurtherUsage = true, WillAlert = true
    };
}