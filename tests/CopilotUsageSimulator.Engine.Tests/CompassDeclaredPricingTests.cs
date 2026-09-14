using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Reference;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class CompassDeclaredPricingTests
{
    [Fact]
    public void HistoricalUnverifiedEligibilityDoesNotBypassDeclaredUnpricedComponents()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        var scenario = HistoricalMaiScenario();
        scenario = scenario with
        {
            Calls = [scenario.Calls[0] with { CacheWriteTokens = 1 }]
        };
        var eligibility = new ModelEligibilityEvaluator(configuration).Evaluate(
            scenario.Calls[0].ModelId, scenario.PlanId, scenario.OperationId, scenario.Timestamp, scenario.Calls[0]);
        Assert.Equal(ModelEligibilityReasonCodes.Unverified, eligibility.ReasonCode);

        var exception = Assert.Throws<SimulationException>(() =>
            new CopilotUsageSimulationEngine(configuration).Simulate(scenario));

        Assert.Equal(ModelEligibilityReasonCodes.TokenComponentUnpriced, exception.Code);
    }

    [Fact]
    public void HistoricalConfiguredPricingRemainsIndependentOfAvailabilityCoverage()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        var scenario = HistoricalMaiScenario();

        var result = new CopilotUsageSimulationEngine(configuration).Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal(20m, result.CostRequirement.AiCredits);
        Assert.Equal(.20m, Assert.Single(result.Calls).RawUsd);
        Assert.Null(result.Calls[0].Pricing!.CacheWriteUsdPerMillion);
        Assert.Equal("compass-date-unsupported", new SimulationPreviewService(configuration).Preview(scenario).ErrorCode);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("student")]
    public void AutoPlanRestrictionUsesSelectedSeatRatherThanAnotherPaidSeat(string plan)
    {
        var scenario = SelectedUnpaidSeatScenario(plan);

        var exception = Assert.Throws<SimulationException>(() =>
            new CopilotUsageSimulationEngine(EngineConfigurationLoader.LoadDefault()).Simulate(scenario));

        Assert.Equal(ModelEligibilityReasonCodes.MultiplierNotApplicable, exception.Code);
        Assert.Contains($"plan '{plan}'", exception.Message);
    }

    [Fact]
    public void ConflictingScenarioPlanCannotHideTheSelectedUnpaidSeat()
    {
        var scenario = SelectedUnpaidSeatScenario("free") with { PlanId = "business" };

        var exception = Assert.Throws<SimulationException>(() =>
            new CopilotUsageSimulationEngine(EngineConfigurationLoader.LoadDefault()).Simulate(scenario));

        Assert.Equal(SimulationScenarioValidator.InvalidContractCode, exception.Code);
    }

    [Fact]
    public void MissingOptionalMultiplierPlanMetadataKeepsLegacyConfiguredBehavior()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        configuration = configuration with
        {
            Multipliers = configuration.Multipliers.Select(multiplier => multiplier.Id == "auto-model-selection"
                ? multiplier with { ApplicablePlanIds = null }
                : multiplier).ToArray()
        };

        var result = new CopilotUsageSimulationEngine(configuration).Simulate(SelectedUnpaidSeatScenario("free"));

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal(.18m, Assert.Single(result.Calls).AdjustedUsd);
        Assert.Equal(18m, result.Allocation.TotalCredits);
    }

    [Theory]
    [InlineData("gpt-5.6-luna")]
    [InlineData("mai-code-1.1-flash")]
    public void DirectLegacyResidencyAndStackingRemainIndependentOfCompassSupport(string modelId)
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            Calls =
            [
                new()
                {
                    ModelId = modelId,
                    ContextTokens = 100_000,
                    FreshInputTokens = 1_000_000,
                    EnabledMultiplierIds = ["auto-model-selection", "data-residency-fedramp"]
                }
            ]
        };
        var configuration = EngineConfigurationLoader.LoadDefault();

        var result = new CopilotUsageSimulationEngine(configuration).Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal(.198m, Assert.Single(result.Calls).AdjustedUsd);
        Assert.Equal(19.8m, result.CostRequirement.AiCredits);
        Assert.Equal(2, result.Calls[0].AppliedMultipliers.Count);
        Assert.Equal("compass-multiplier-unsupported", new SimulationPreviewService(configuration).Preview(scenario).ErrorCode);
    }

    [Fact]
    public void ExplicitRetirementIsNotIgnoredWithMissingLegacyPlanEvidence()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        var retired = configuration.Models.First(model => model.Availability?.RetiredAt <= CompassFixture.Timestamp);
        var scenario = CompassFixture.Scenario();
        scenario = scenario with { Calls = [scenario.Calls[0] with { ModelId = retired.Id }] };
        var engine = new CopilotUsageSimulationEngine(configuration);

        var exception = Assert.Throws<SimulationException>(() => engine.Simulate(scenario));

        Assert.Equal(ModelEligibilityReasonCodes.Retired, exception.Code);
        var earlierPolicy = engine.Simulate(scenario with
        {
            AccessGates = new Dictionary<string, AccessGateState> { ["policy"] = new() { Passed = false } }
        });
        Assert.Equal(SimulationDecision.Blocked, earlierPolicy.Decision);
        Assert.Equal("policy", earlierPolicy.FirstFailingGate);
        Assert.Empty(earlierPolicy.Calls);
    }

    [Fact]
    public void ExplicitModelPlanExclusionIsEnforcedForTheSelectedSeat()
    {
        var scenario = SelectedUnpaidSeatScenario("pro");
        scenario = scenario with
        {
            Calls = [scenario.Calls[0] with { ModelId = "gpt-6-astra", EnabledMultiplierIds = [] }]
        };

        var exception = Assert.Throws<SimulationException>(() =>
            new CopilotUsageSimulationEngine(EngineConfigurationLoader.LoadDefault()).Simulate(scenario));

        Assert.Equal(ModelEligibilityReasonCodes.PlanIneligible, exception.Code);
    }

    [Fact]
    public void KnownAnnouncementBoundaryIsEnforcedIndependentlyOfTariffCoverage()
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            Timestamp = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero),
            Calls = [scenario.Calls[0] with { ModelId = "gpt-6-astra" }]
        };

        var exception = Assert.Throws<SimulationException>(() =>
            new CopilotUsageSimulationEngine(EngineConfigurationLoader.LoadDefault()).Simulate(scenario));

        Assert.Equal(ModelEligibilityReasonCodes.NotYetAvailable, exception.Code);
    }

    [Fact]
    public void NonExhaustiveEligibilityIsUnknownRatherThanAnExplicitCoreDenial()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        configuration = configuration with
        {
            Models = configuration.Models.Select(model => model.Id == CompassFixture.ModelId
                ? model with
                {
                    Availability = model.Availability! with
                    {
                        EligiblePlanIds = ["enterprise"],
                        EligiblePlansAreExhaustive = false
                    }
                }
                : model).ToArray()
        };

        var result = new CopilotUsageSimulationEngine(configuration).Simulate(CompassFixture.Scenario());

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal(20m, result.CostRequirement.AiCredits);
        Assert.Equal(ModelEligibilityReasonCodes.Unverified,
            new SimulationPreviewService(configuration).Preview(CompassFixture.Scenario()).ErrorCode);
    }

    [Fact]
    public void DirectUnknownModelRetainsItsLegacyDiagnosticCode()
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with { Calls = [scenario.Calls[0] with { ModelId = "unknown-model-fixture" }] };

        var exception = Assert.Throws<SimulationException>(() =>
            new CopilotUsageSimulationEngine(EngineConfigurationLoader.LoadDefault()).Simulate(scenario));

        Assert.Equal("model-not-found", exception.Code);
    }

    private static SimulationScenario HistoricalMaiScenario()
    {
        var scenario = CompassFixture.Scenario();
        return scenario with
        {
            Timestamp = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
            BillingContext = scenario.BillingContext! with
            {
                CycleStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                CycleEnd = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
            }
        };
    }

    private static SimulationScenario SelectedUnpaidSeatScenario(string plan)
    {
        var scenario = CompassFixture.Scenario();
        return scenario with
        {
            PlanId = plan,
            BillingContext = scenario.BillingContext! with
            {
                SeatAssignments =
                [
                    scenario.BillingContext.SeatAssignments[0],
                    new() { UserId = "user-2", PlanId = plan, CostCenterId = "cc-2" }
                ]
            },
            Attribution = new()
            {
                UserId = "user-2",
                LicensingOrganizationIds = ["org-2"],
                DirectAssignments = [new() { CostCenterId = "cc-2" }]
            },
            Calls =
            [
                new()
                {
                    ModelId = "gpt-5.6-luna",
                    ContextTokens = 100_000,
                    FreshInputTokens = 1_000_000,
                    EnabledMultiplierIds = ["auto-model-selection"]
                }
            ]
        };
    }
}
