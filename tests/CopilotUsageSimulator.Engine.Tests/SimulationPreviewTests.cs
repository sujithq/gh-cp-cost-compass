using System.Text.Json;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class SimulationPreviewTests
{
    private readonly SimulationPreviewService _service = new(CompassFixture.Configuration());

    [Fact]
    public void RepeatedPreviewLeavesEveryInputCollectionAndBalanceUnchanged()
    {
        var scenario = CompassFixture.Scenario();
        var original = JsonSerializer.Serialize(scenario);

        var first = _service.Preview(scenario);
        var second = _service.Preview(scenario);

        Assert.Null(first.ErrorCode);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(original, JsonSerializer.Serialize(scenario));
        Assert.Equal(60m, second.InitialRemaining!.IncludedPoolCredits);
        Assert.Equal(60m, second.Result!.Remaining.IncludedPoolCredits);
    }

    [Fact]
    public void BlockedRequestKeepsRequiredOneHundredCreditsSeparateFromAcceptedZero()
    {
        var preview = _service.Preview(CompassFixture.Scenario());

        Assert.Null(preview.ErrorCode);
        Assert.Equal(SimulationDecision.Blocked, preview.Result!.Decision);
        Assert.Equal("paid-usage", preview.Result.FirstFailingGate);
        Assert.Equal(100m, preview.RequiredAiCredits);
        Assert.Equal(1m, preview.RequiredModelUsd);
        Assert.Equal(60m, preview.ProposedIncludedCredits);
        Assert.Equal(40m, preview.ProposedMeteredCredits);
        Assert.Equal(.40m, preview.ProposedAiUsd);
        Assert.Equal(0m, preview.ProposedActionsUsd);
        Assert.Equal(.40m, preview.ProposedTotalUsd);
        Assert.Equal(0m, preview.AcceptedAiCredits);
        Assert.Equal(0m, preview.AcceptedTotalUsd);
        Assert.Equal(0m, preview.Result.Allocation.IncludedCredits);
        Assert.Equal(0m, preview.Result.Allocation.MeteredCredits);
        Assert.Equal(0m, preview.Result.EffectiveUlb!.ReservedCredits);
        Assert.Equal(1_000m, preview.Result.EffectiveUlb.RemainingCredits);
        Assert.Empty(preview.Result.Alerts);
    }

    [Fact]
    public void FullyIncludedRequestDoesNotRequirePaidAuthorization()
    {
        var preview = _service.Preview(CompassFixture.Scenario(600_000));

        Assert.Null(preview.ErrorCode);
        Assert.Equal(SimulationDecision.Allowed, preview.Result!.Decision);
        Assert.Equal(60m, preview.RequiredAiCredits);
        Assert.Equal(60m, preview.ProposedIncludedCredits);
        Assert.Equal(0m, preview.ProposedTotalUsd);
        Assert.Equal(60m, preview.AcceptedAiCredits);
        Assert.Equal(0m, preview.Result.Remaining.IncludedPoolCredits);
        Assert.Equal(SimulationTraceState.NotApplicable,
            Assert.Single(preview.Result.Trace, entry => entry.Id == "paid-usage").State);
    }

    [Fact]
    public void EnablingPaidUsageRevealsTwentyCentBudgetAgainstFortyCentRequirement()
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                SpendingBudgets = [scenario.EconomicGuardrails.SpendingBudgets[0] with { LimitUsd = .20m }]
            }
        };

        var comparison = Assert.Single(_service.Compare(scenario), value => value.Id == "enable-paid-usage");

        Assert.Equal("paid-usage", comparison.Baseline.Result!.FirstFailingGate);
        Assert.Equal("enterprise-spend", comparison.Candidate.Result!.FirstFailingGate);
        Assert.Equal(.40m, comparison.Candidate.ProposedAiUsd);
        Assert.Equal(0m, comparison.Candidate.AcceptedTotalUsd);
        Assert.Equal(0m, comparison.Candidate.Result.Allocation.TotalCredits);
        Assert.Equal(0m, comparison.UsdDelta);
        Assert.False(comparison.Resolved);
        Assert.Empty(comparison.Candidate.Result.Alerts);
        Assert.Equal(.20m, comparison.CandidateScenario.EconomicGuardrails!.SpendingBudgets[0].LimitUsd);
    }

    [Fact]
    public void ReductionFitsPositivePoolOnlyAfterReevaluation()
    {
        var scenario = CompassFixture.Scenario();
        var before = JsonSerializer.Serialize(scenario);

        var comparisons = _service.Compare(scenario);
        var reduction = Assert.Single(comparisons, value => value.Id == "reduce-to-included");
        var paid = Assert.Single(comparisons, value => value.Id == "enable-paid-usage");

        Assert.Equal("reduce-to-included", comparisons[0].Id);
        Assert.True(reduction.Resolved);
        Assert.Equal(60m, reduction.Candidate.RequiredAiCredits);
        Assert.Equal(0m, reduction.Candidate.ProposedTotalUsd);
        Assert.Equal(-.40m, reduction.UsdDelta);
        Assert.Equal(600_000, reduction.CandidateScenario.Calls[0].FreshInputTokens);
        Assert.Equal(scenario.Calls[0].ContextTokens, reduction.CandidateScenario.Calls[0].ContextTokens);
        Assert.Equal(GuardrailValue.Disabled, reduction.CandidateScenario.EconomicGuardrails!.PaidUsage.State);
        Assert.True(paid.Resolved);
        Assert.Equal(.40m, paid.Candidate.AcceptedTotalUsd);
        Assert.Equal(before, JsonSerializer.Serialize(scenario));
    }

    [Fact]
    public void RepeatedComparisonsAreDeterministicAndDoNotAdvanceBalances()
    {
        var scenario = CompassFixture.Scenario();
        var before = JsonSerializer.Serialize(scenario);

        var first = _service.Compare(scenario);
        var second = _service.Compare(scenario);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(before, JsonSerializer.Serialize(scenario));
        Assert.All(second, comparison => Assert.Equal(60m, comparison.Baseline.InitialRemaining!.IncludedPoolCredits));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.00001)]
    public void ReductionDoesNotCreateFreeWorkFromAnEmptyOrUnusableAllowance(decimal remaining)
    {
        var scenario = CompassFixture.Scenario(1);
        scenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                EnterprisePoolConsumedCredits = 1_900m - remaining
            }
        };

        var comparisons = _service.Compare(scenario);

        Assert.DoesNotContain(comparisons, value => value.Id == "reduce-to-included");
        Assert.All(comparisons, value => Assert.True(value.Candidate.RequiredAiCredits > 0));
    }

    [Fact]
    public void CandidatesDoNotShareNestedMutableCollectionsWithEachOtherOrBaseline()
    {
        var scenario = CompassFixture.Scenario();
        var original = JsonSerializer.Serialize(scenario);
        var comparisons = _service.Compare(scenario);
        var reduction = Assert.Single(comparisons, value => value.Id == "reduce-to-included");
        var paid = Assert.Single(comparisons, value => value.Id == "enable-paid-usage");
        var paidBefore = JsonSerializer.Serialize(paid);
        var baselineBefore = JsonSerializer.Serialize(reduction.Baseline);
        var candidate = reduction.CandidateScenario;

        ((IDictionary<string, string>)candidate.Metadata)["fixture"] = "changed";
        ((IDictionary<string, string>)candidate.Calls[0].Metadata)["fixture"] = "changed";
        ((IDictionary<string, AccessGateState>)candidate.AccessGates).Clear();
        ((ISet<string>)candidate.EconomicGuardrails!.PaidUsage.ProductIds).Clear();
        ((ISet<string>)candidate.EconomicGuardrails.SpendingBudgets[0].ProductIds).Add("other");
        ((ISet<string>)candidate.EconomicGuardrails.EnterpriseBudgetExcludedCostCenterIds).Add("cc-1");
        ((EffectiveSeatAssignment[])candidate.BillingContext!.SeatAssignments)[0] =
            candidate.BillingContext.SeatAssignments[0] with { PlanId = "enterprise" };
        ((string[])candidate.Attribution!.LicensingOrganizationIds)[0] = "another-org";

        Assert.Equal(original, JsonSerializer.Serialize(scenario));
        Assert.Equal(paidBefore, JsonSerializer.Serialize(paid));
        Assert.Equal(baselineBefore, JsonSerializer.Serialize(reduction.Baseline));
    }

    [Fact]
    public void SpendingChangesCannotResolveEarlierPolicyOrUlb()
    {
        var policy = CompassFixture.Scenario() with
        {
            AccessGates = new Dictionary<string, AccessGateState> { ["policy"] = new() { Passed = false } }
        };
        var model = CompassFixture.Scenario() with
        {
            AccessGates = new Dictionary<string, AccessGateState> { ["model-availability"] = new() { Passed = false } }
        };
        var ulb = CompassFixture.Scenario();
        ulb = ulb with
        {
            EconomicGuardrails = ulb.EconomicGuardrails! with
            {
                UserLevelBudgets = [ulb.EconomicGuardrails.UserLevelBudgets[0] with { LimitCredits = 50m }]
            }
        };

        foreach (var (scenario, gate) in new[] { (policy, "policy"), (model, "model-availability"), (ulb, "ulb-user-1") })
        {
            var comparisons = _service.Compare(scenario);
            Assert.NotEmpty(comparisons);
            Assert.All(comparisons, value =>
            {
                Assert.False(value.Resolved);
                Assert.Equal(gate, value.Candidate.Result!.FirstFailingGate);
                Assert.Equal(0m, value.Candidate.AcceptedTotalUsd);
            });
        }
    }

    [Fact]
    public void ReductionRespectsCostCenterAllowanceWhilePaidUsageCannotOverrideItsHardStop()
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                EnterprisePoolConsumedCredits = 1_800m,
                IncludedUsageControls =
                [
                    new()
                    {
                        Id = "cc-1-included",
                        CostCenterId = "cc-1",
                        ConsumedCredits = 1_840m,
                        OverflowBehavior = IncludedOverflowBehavior.Block
                    }
                ]
            }
        };

        var comparisons = _service.Compare(scenario);
        var reduction = Assert.Single(comparisons, value => value.Id == "reduce-to-included");
        var paid = Assert.Single(comparisons, value => value.Id == "enable-paid-usage");

        Assert.True(reduction.Resolved);
        Assert.Equal(60m, reduction.Candidate.RequiredAiCredits);
        Assert.Equal(40m, reduction.Candidate.Result!.Remaining.IncludedPoolCredits);
        Assert.Equal(0m, reduction.Candidate.Result.Remaining.IncludedUsageControlCredits);
        Assert.False(paid.Resolved);
        Assert.Equal("cc-1-included", paid.Candidate.Result!.FirstFailingGate);
    }

    [Fact]
    public void EnablingPaidUsageDoesNotChangeProductOrSkuApplicability()
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                PaidUsage = scenario.EconomicGuardrails.PaidUsage with { ProductIds = new HashSet<string> { "other-product" } }
            }
        };

        var comparison = Assert.Single(_service.Compare(scenario), value => value.Id == "enable-paid-usage");

        Assert.False(comparison.Resolved);
        Assert.Equal("paid-usage.not-applicable", comparison.Baseline.Result!.FirstFailingGate);
        Assert.Equal("paid-usage.not-applicable", comparison.Candidate.Result!.FirstFailingGate);
        Assert.Equal(new[] { "other-product" }, comparison.CandidateScenario.EconomicGuardrails!.PaidUsage.ProductIds);
        Assert.Equal(0m, comparison.Candidate.AcceptedAiCredits);
    }

    [Fact]
    public void ActionsBudgetRejectionKeepsBothRequiredMetersAndNeitherAcceptedCharge()
    {
        var scenario = CompassFixture.Cloud(CompassFixture.Scenario());
        scenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                PaidUsage = scenario.EconomicGuardrails.PaidUsage with { State = GuardrailValue.Enabled },
                SpendingBudgets =
                [
                    scenario.EconomicGuardrails.SpendingBudgets[0] with
                    {
                        LimitUsd = .10m,
                        Enforcement = GuardrailEnforcement.AlertOnly
                    }
                ]
            },
            ActionsGuardrails = scenario.ActionsGuardrails! with
            {
                Budgets = [scenario.ActionsGuardrails.Budgets[0] with { LimitUsd = .20m }]
            }
        };

        var preview = _service.Preview(scenario);

        Assert.Null(preview.ErrorCode);
        Assert.Equal("actions-spend", preview.Result!.FirstFailingGate);
        Assert.Equal(100m, preview.RequiredAiCredits);
        Assert.Equal(60m, preview.ProposedIncludedCredits);
        Assert.Equal(.40m, preview.ProposedAiUsd);
        Assert.Equal(.54m, preview.ProposedActionsUsd);
        Assert.Equal(.94m, preview.ProposedTotalUsd);
        Assert.Equal(0m, preview.AcceptedAiCredits);
        Assert.Equal(0m, preview.AcceptedAiUsd);
        Assert.Equal(0m, preview.AcceptedActionsUsd);
        Assert.Equal(60m, preview.Result.Remaining.IncludedPoolCredits);
        Assert.Equal(10m, preview.Result.Remaining.ActionsIncludedMinutes);
        Assert.Equal(0m, preview.Result.EffectiveUlb!.ReservedCredits);
        Assert.Empty(preview.Result.Alerts);
    }

    [Fact]
    public void ExpiredPricingIsADiagnosticNonEstimateAndProducesNoComparisons()
    {
        var configuration = CompassFixture.Configuration();
        configuration = configuration with
        {
            Models = configuration.Models.Select(model => model.Id == CompassFixture.ModelId
                ? model with
                {
                    PricePeriods = model.PricePeriods.Select(period =>
                        period with { EffectiveTo = CompassFixture.Timestamp.AddDays(-1) }).ToArray()
                }
                : model).ToArray()
        };
        var service = new SimulationPreviewService(configuration);

        var preview = service.Preview(CompassFixture.Scenario());

        Assert.Equal("pricing-not-effective", preview.ErrorCode);
        Assert.NotNull(preview.ErrorMessage);
        Assert.Null(preview.Result);
        Assert.Null(preview.RequiredAiCredits);
        Assert.Null(preview.ProposedTotalUsd);
        Assert.Null(preview.AcceptedTotalUsd);
        Assert.Empty(service.Compare(CompassFixture.Scenario()));
    }

    [Fact]
    public void MalformedScenarioUsesDomainDiagnosticWithoutMutatingInput()
    {
        var scenario = CompassFixture.Scenario() with { Calls = null! };

        var preview = _service.Preview(scenario);

        Assert.Equal(SimulationScenarioValidator.InvalidContractCode, preview.ErrorCode);
        Assert.Null(preview.Result);
        Assert.Null(preview.RequiredAiCredits);
        Assert.Null(scenario.Calls);
    }

    [Fact]
    public void UnrepresentableArithmeticReturnsANonEstimateInsteadOfAnOverflow()
    {
        var configuration = CompassFixture.Configuration();
        configuration = configuration with
        {
            Models = configuration.Models.Select(model => model.Id == CompassFixture.ModelId
                ? model with
                {
                    PricePeriods = model.PricePeriods.Select(period => period with
                    {
                        Tiers = period.Tiers.Select(tier => tier with { InputUsdPerMillion = decimal.MaxValue }).ToArray()
                    }).ToArray()
                }
                : model).ToArray()
        };

        var preview = new SimulationPreviewService(configuration).Preview(CompassFixture.Scenario());

        Assert.Equal("calculation-overflow", preview.ErrorCode);
        Assert.Null(preview.Result);
        Assert.Null(preview.RequiredAiCredits);
        Assert.Null(preview.ProposedTotalUsd);
        Assert.Null(preview.AcceptedTotalUsd);
    }

    [Fact]
    public void MissingCallsRemainPartiallySimulatedWithoutInventedZeroEstimate()
    {
        var preview = _service.Preview(CompassFixture.Scenario() with { Calls = [] });

        Assert.Null(preview.ErrorCode);
        Assert.Equal(SimulationDecision.PartiallySimulated, preview.Result!.Decision);
        Assert.Null(preview.RequiredAiCredits);
        Assert.Null(preview.ProposedTotalUsd);
        Assert.Equal(0m, preview.AcceptedAiCredits);
    }
}
