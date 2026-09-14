using System.Text.Json;
using CopilotUsageSimulator.Common.Guardrails;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class CompassTraceTests
{
    [Fact]
    public void EarlyAccessTerminalPreservesGateOrderAndSkipsLaterChecks()
    {
        var configuration = CompassFixture.Configuration();
        var scenario = CompassFixture.Scenario() with
        {
            AccessGates = new Dictionary<string, AccessGateState>
            {
                ["policy"] = new() { Passed = false, Reason = "Administrator policy denies this operation." },
                ["network"] = new() { Passed = false }
            }
        };

        var result = new CopilotUsageSimulationEngine(configuration).Simulate(scenario);

        Assert.Equal("policy", result.FirstFailingGate);
        Assert.Equal(Enumerable.Range(1, result.Trace.Count), result.Trace.Select(entry => entry.Order));
        Assert.Equal(configuration.Gates.OrderBy(gate => gate.Sequence).Select(gate => gate.Id),
            result.Trace.Where(entry => entry.Stage == "access").Select(entry => entry.Id));
        var policy = Assert.Single(result.Trace, entry => entry.Id == "policy");
        Assert.Equal(SimulationTraceState.Blocked, policy.State);
        Assert.Equal("Administrator policy denies this operation.", policy.Message);
        Assert.Equal(SimulationTraceState.Passed, Assert.Single(result.Trace, entry => entry.Id == "license-seat").State);
        Assert.All(result.Trace.Where(entry => entry.Order > policy.Order),
            entry => Assert.Equal(SimulationTraceState.NotEvaluated, entry.State));
        Assert.Null(result.CostRequirement.AiCredits);
        Assert.Null(result.CostRequirement.TotalUsd);
    }

    [Fact]
    public void CostOnlyModeExplicitlyExcludesCatalogRuntimeAndActionsAccess()
    {
        var scenario = CompassFixture.Cloud(CompassFixture.Scenario(600_000));
        scenario = scenario with
        {
            CheckScope = SimulationCheckScope.CostRelatedOnly,
            AccessGates = new Dictionary<string, AccessGateState> { ["policy"] = new() { Passed = false } },
            RuntimeGuardrails = new() { MaximumModelCalls = 0, CliSoftCreditLimit = 0m },
            ActionsGuardrails = scenario.ActionsGuardrails! with { ActionsEnabled = GuardrailValue.Disabled }
        };

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Null(preview.ErrorCode);
        Assert.Equal(SimulationDecision.Allowed, preview.Result!.Decision);
        Assert.All(preview.Result.Trace.Where(entry => entry.Stage is "access" or "runtime-preflight" or "runtime-credits" or "actions-access"),
            entry => Assert.Equal(SimulationTraceState.Excluded, entry.State));
        Assert.DoesNotContain(preview.Result.AppliedGuardrails, guardrail =>
            guardrail.Category is GuardrailCategories.Runtime or GuardrailCategories.ActionsAccess);
        Assert.Contains(preview.Assumptions, assumption => assumption.Contains("excludes access and runtime", StringComparison.Ordinal));
    }

    [Fact]
    public void NonApplicableCatalogGateIsNotAnInventedPass()
    {
        var result = new CopilotUsageSimulationEngine(CompassFixture.Configuration())
            .Simulate(CompassFixture.Scenario(600_000));

        Assert.Equal(SimulationTraceState.NotApplicable,
            Assert.Single(result.Trace, entry => entry.Id == "cloud-agent-runtime").State);
        Assert.All(result.Trace.Where(entry => entry.Stage == "actions-access"),
            entry => Assert.Equal(SimulationTraceState.NotApplicable, entry.State));
        Assert.All(result.Trace.Where(entry => entry.Stage == "runtime-preflight"),
            entry => Assert.Equal(SimulationTraceState.NotApplicable, entry.State));
        Assert.Contains(result.Trace, entry => entry.Id == "license-seat" &&
            entry.Message.Contains("PassWhenUnspecified", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SimulationDecision.Allowed)]
    [InlineData(SimulationDecision.Blocked)]
    [InlineData(SimulationDecision.PartiallySimulated)]
    [InlineData(SimulationDecision.SoftStopped)]
    [InlineData(SimulationDecision.Waiting)]
    [InlineData(SimulationDecision.Indeterminate)]
    public void EveryDecisionPreservesAtomicConsumptionAndAcceptedChargeAlerts(SimulationDecision decision)
    {
        var scenario = CompassFixture.Cloud(CompassFixture.Scenario());
        scenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                PaidUsage = scenario.EconomicGuardrails.PaidUsage with { State = GuardrailValue.Enabled },
                SpendingBudgets = [scenario.EconomicGuardrails.SpendingBudgets[0] with
                {
                    LimitUsd = .10m,
                    Enforcement = GuardrailEnforcement.AlertOnly
                }]
            },
            ActionsGuardrails = scenario.ActionsGuardrails! with
            {
                Budgets = [scenario.ActionsGuardrails.Budgets[0] with
                {
                    LimitUsd = .10m,
                    Enforcement = GuardrailEnforcement.AlertOnly
                }]
            }
        };
        scenario = decision switch
        {
            SimulationDecision.Blocked => scenario with
            {
                EconomicGuardrails = scenario.EconomicGuardrails! with
                {
                    UserLevelBudgets = [scenario.EconomicGuardrails.UserLevelBudgets[0] with { LimitCredits = 50m }]
                }
            },
            SimulationDecision.PartiallySimulated => scenario with { Calls = [] },
            SimulationDecision.SoftStopped => scenario with { RuntimeGuardrails = new() { CliSoftCreditLimit = 50m } },
            SimulationDecision.Waiting => scenario with
            {
                ActionsGuardrails = scenario.ActionsGuardrails! with { WorkflowApproved = GuardrailValue.Disabled }
            },
            SimulationDecision.Indeterminate => scenario with
            {
                Attribution = scenario.Attribution! with { LicensingOrganizationIds = ["org-1", "org-2"] }
            },
            _ => scenario
        };
        var original = JsonSerializer.Serialize(scenario);

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Null(preview.ErrorCode);
        Assert.Equal(decision, preview.Result!.Decision);
        Assert.Equal(original, JsonSerializer.Serialize(scenario));
        if (decision == SimulationDecision.Allowed)
        {
            Assert.Equal(100m, preview.AcceptedAiCredits);
            Assert.Equal(.94m, preview.AcceptedTotalUsd);
            Assert.NotEmpty(preview.Result.Alerts);
            Assert.Equal(0m, preview.Result.Remaining.IncludedPoolCredits);
            Assert.Equal(0m, preview.Result.Remaining.ActionsIncludedMinutes);
        }
        else
        {
            Assert.Equal(0m, preview.AcceptedAiCredits);
            Assert.Equal(0m, preview.AcceptedTotalUsd);
            Assert.Equal(0m, preview.Result.Allocation.TotalCredits);
            Assert.Equal(0m, preview.Result.Allocation.IncludedCredits);
            Assert.Equal(0m, preview.Result.Allocation.MeteredCredits);
            Assert.Empty(preview.Result.Alerts);
            Assert.Equal(JsonSerializer.Serialize(preview.InitialRemaining), JsonSerializer.Serialize(preview.Result.Remaining));
            Assert.True(preview.Result.EffectiveUlb is null || preview.Result.EffectiveUlb.ReservedCredits == 0m);
        }

        if (preview.Result.FirstFailingGate is { } gate)
        {
            var trace = Assert.Single(preview.Result.Trace, entry => entry.Id == gate);
            var expected = decision switch
            {
                SimulationDecision.Blocked => SimulationTraceState.Blocked,
                SimulationDecision.Waiting => SimulationTraceState.Waiting,
                SimulationDecision.SoftStopped => SimulationTraceState.SoftStopped,
                _ => SimulationTraceState.Indeterminate
            };
            Assert.Equal(expected, trace.State);
        }
    }

    [Fact]
    public void EconomicFailureRecordsActualQuantitiesAndDoesNotEvaluateActionsBudget()
    {
        var result = new CopilotUsageSimulationEngine(CompassFixture.Configuration())
            .Simulate(CompassFixture.Cloud(CompassFixture.Scenario()));

        Assert.Equal("paid-usage", result.FirstFailingGate);
        var paid = Assert.Single(result.Trace, entry => entry.Id == "paid-usage");
        Assert.Equal(40m, paid.Requested);
        Assert.Equal(SimulationTraceState.Blocked, paid.State);
        Assert.Equal("user-1", paid.UserId);
        Assert.Equal("business", paid.PlanId);
        Assert.Equal("cc-1", paid.CostCenterId);
        Assert.Equal("org-1", paid.LicensingOrganizationId);
        Assert.Contains(result.Trace, entry => entry.Stage == "actions-budgets" && entry.State == SimulationTraceState.NotEvaluated);
        Assert.DoesNotContain(result.AppliedGuardrails, entry => entry.Id == "actions-spend");
    }

    [Fact]
    public void SelectedSeatAttributionAndBudgetIdentitySurviveCounterfactuals()
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            PlanId = "enterprise",
            BillingContext = scenario.BillingContext! with
            {
                SeatAssignments =
                [
                    scenario.BillingContext.SeatAssignments[0],
                    new() { UserId = "user-2", PlanId = "enterprise", CostCenterId = "cc-2" }
                ]
            },
            Attribution = new()
            {
                UserId = "user-2",
                LicensingOrganizationIds = ["org-2"],
                DirectAssignments = [new() { CostCenterId = "cc-2" }]
            },
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                EnterprisePoolConsumedCredits = 5_740m,
                UserLevelBudgets =
                [
                    scenario.EconomicGuardrails.UserLevelBudgets[0] with { LimitCredits = 0m },
                    new() { Id = "ulb-user-2", Kind = UserLevelBudgetKind.Individual, TargetId = "user-2", LimitCredits = 200m }
                ],
                SpendingBudgets =
                [
                    new() { Id = "cc-1-spend", Scope = SpendingBudgetScope.CostCenter, ScopeId = "cc-1", LimitUsd = 0m, Enforcement = GuardrailEnforcement.HardStop },
                    new() { Id = "cc-2-spend", Scope = SpendingBudgetScope.CostCenter, ScopeId = "cc-2", LimitUsd = .20m, Enforcement = GuardrailEnforcement.HardStop }
                ]
            }
        };

        var comparison = Assert.Single(new SimulationPreviewService(CompassFixture.Configuration()).Compare(scenario),
            value => value.Id == "enable-paid-usage");

        Assert.Equal("cc-2-spend", comparison.Candidate.Result!.FirstFailingGate);
        Assert.False(comparison.Resolved);
        Assert.Equal("ulb-user-2", comparison.Candidate.Result.EffectiveUlb!.Id);
        Assert.Equal(60m, comparison.Candidate.InitialRemaining!.IncludedPoolCredits);
        Assert.DoesNotContain(comparison.Candidate.Result.AppliedGuardrails, value => value.Id is "cc-1-spend" or "ulb-user-1");
        var budget = Assert.Single(comparison.Candidate.Result.Trace, value => value.Id == "cc-2-spend");
        Assert.Equal("user-2", budget.UserId);
        Assert.Equal("cc-2", budget.CostCenterId);
        Assert.Equal("org-2", budget.LicensingOrganizationId);
        Assert.Equal("enterprise", budget.PlanId);
        Assert.Equal(0m, comparison.CandidateScenario.EconomicGuardrails!.UserLevelBudgets[0].LimitCredits);
    }

    [Fact]
    public void BudgetTracePreservesTheActuallyEvaluatedBatchAndMostRestrictiveBlocker()
    {
        var scenario = CompassFixture.Cloud(CompassFixture.Scenario());
        scenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                PaidUsage = scenario.EconomicGuardrails.PaidUsage with { State = GuardrailValue.Enabled },
                SpendingBudgets =
                [
                    new() { Id = "first-budget", Scope = SpendingBudgetScope.Enterprise, LimitUsd = .30m, Enforcement = GuardrailEnforcement.HardStop },
                    new() { Id = "tightest-budget", Scope = SpendingBudgetScope.Enterprise, LimitUsd = .20m, Enforcement = GuardrailEnforcement.HardStop }
                ]
            }
        };

        var result = new CopilotUsageSimulationEngine(CompassFixture.Configuration()).Simulate(scenario);

        Assert.Equal("tightest-budget", result.FirstFailingGate);
        var budgets = result.Trace.Where(entry => entry.Stage == GuardrailCategories.MeteredSpendingBudget).ToArray();
        Assert.Equal(new[] { "first-budget", "tightest-budget" }, budgets.Select(entry => entry.Id));
        Assert.All(budgets, entry =>
        {
            Assert.Equal(SimulationTraceState.Blocked, entry.State);
            Assert.Equal(.40m, entry.Requested);
        });
        Assert.Contains(result.Trace, entry => entry.Stage == "actions-budgets" && entry.State == SimulationTraceState.NotEvaluated);
        Assert.Equal(0m, result.Allocation.TotalCredits);
        Assert.Equal(.40m, result.CostRequirement.AiUsd);
    }
}
