using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Reference;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class CompassProfileTests
{
    [Theory]
    [InlineData("code-review")]
    [InlineData("actions")]
    [InlineData("ide-agent")]
    [InlineData("code-completion")]
    public void UnsupportedOperationsAreNotEstimatesOrLivePolicyDenials(string operation)
    {
        var preview = new SimulationPreviewService(CompassFixture.Configuration())
            .Preview(CompassFixture.Scenario() with { OperationId = operation });

        Assert.Equal("compass-operation-unsupported", preview.ErrorCode);
        AssertNonEstimate(preview);
    }

    [Theory]
    [InlineData("pro")]
    [InlineData("pro-plus")]
    [InlineData("free")]
    [InlineData("student")]
    [InlineData("max")]
    public void PersonalPlansDoNotReceivePooledApprovals(string plan)
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            PlanId = plan,
            BillingContext = scenario.BillingContext! with
            {
                SeatAssignments = [scenario.BillingContext.SeatAssignments[0] with { PlanId = plan }]
            }
        };

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Equal("compass-plan-unsupported", preview.ErrorCode);
        AssertNonEstimate(preview);
    }

    [Theory]
    [InlineData("2026-08-31T23:59:59Z")]
    [InlineData("2026-09-28T00:00:00Z")]
    [InlineData("2026-10-01T12:00:00Z")]
    public void UnverifiedHistoricalAndFutureCohortsAreExplicitNonEstimates(string date)
    {
        var preview = new SimulationPreviewService(CompassFixture.Configuration())
            .Preview(CompassFixture.Scenario() with { Timestamp = DateTimeOffset.Parse(date) });

        Assert.Equal("compass-date-unsupported", preview.ErrorCode);
        AssertNonEstimate(preview);
    }

    [Fact]
    public void PreviewProfileDoesNotConstrainHistoricalDirectEnginePricing()
    {
        var configuration = CompassFixture.Configuration();
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            Timestamp = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
            BillingContext = scenario.BillingContext! with
            {
                CycleStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                CycleEnd = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
            }
        };

        var direct = new CopilotUsageSimulationEngine(configuration).Simulate(scenario);
        var preview = new SimulationPreviewService(configuration).Preview(scenario);

        Assert.Equal("paid-usage", direct.FirstFailingGate);
        Assert.Equal(100m, direct.CostRequirement.AiCredits);
        Assert.Equal("compass-date-unsupported", preview.ErrorCode);
    }

    [Theory]
    [InlineData(RepositoryVisibility.Public, "linux-2-core", 1, "compass-actions-unsupported")]
    [InlineData(RepositoryVisibility.Internal, "linux-2-core", 1, "compass-actions-unsupported")]
    [InlineData(RepositoryVisibility.Private, "windows-2-core", 1, "compass-actions-unsupported")]
    [InlineData(RepositoryVisibility.Private, "self-hosted", 1, "compass-actions-unsupported")]
    [InlineData(RepositoryVisibility.Private, "linux-2-core", 1.1, "compass-actions-minutes-unsupported")]
    public void UnsupportedActionsAccountingDoesNotMasqueradeAsACharge(
        RepositoryVisibility visibility, string runner, decimal minutes, string code)
    {
        var scenario = CompassFixture.Cloud(CompassFixture.Scenario(600_000));
        scenario = scenario with
        {
            RepositoryVisibility = visibility,
            ActionsUsage = scenario.ActionsUsage! with { RunnerId = runner, Minutes = minutes }
        };

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Equal(code, preview.ErrorCode);
        AssertNonEstimate(preview);
    }

    [Fact]
    public void CloudAgentUsesAccountAllowanceAndKnownStandardRunnerRate()
    {
        var scenario = CompassFixture.Cloud(CompassFixture.Scenario(600_000));
        scenario = scenario with
        {
            ActionsUsage = scenario.ActionsUsage! with { Minutes = 6m, IncludedMinutesRemaining = 100m },
            ActionsGuardrails = scenario.ActionsGuardrails! with { IncludedMinutes = 2m }
        };

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Null(preview.ErrorCode);
        Assert.Equal(SimulationDecision.Allowed, preview.Result!.Decision);
        Assert.Equal("linux-2-core", preview.Result.ActionsUsage!.RunnerId);
        Assert.Equal(2m, preview.Result.ActionsUsage.IncludedMinutes);
        Assert.Equal(4m, preview.Result.ActionsUsage.BillableMinutes);
        Assert.Equal(.024m, preview.ProposedActionsUsd);
        Assert.Contains(preview.Assumptions, value => value.Contains("per-job", StringComparison.Ordinal));
        Assert.Contains(preview.Assumptions, value => value.Contains("never derived from the Copilot seat", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("cli")]
    [InlineData("cloud-agent")]
    public void AutoDiscountAffectsOnlyTheModelMeterInSupportedExperiences(string operation)
    {
        var scenario = operation == "cloud-agent"
            ? CompassFixture.Cloud(CompassFixture.Scenario())
            : CompassFixture.Scenario() with { OperationId = operation };
        scenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                PaidUsage = scenario.EconomicGuardrails.PaidUsage with { State = GuardrailValue.Enabled }
            },
            Calls = [scenario.Calls[0] with { EnabledMultiplierIds = ["auto-model-selection"] }]
        };

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Null(preview.ErrorCode);
        Assert.Equal(SimulationDecision.Allowed, preview.Result!.Decision);
        Assert.Equal(90m, preview.RequiredAiCredits);
        Assert.Equal(.30m, preview.ProposedAiUsd);
        Assert.Equal(operation == "cloud-agent" ? .54m : 0m, preview.ProposedActionsUsd);
    }

    [Fact]
    public void ComplianceAndStackingRemainExplicitlyUnsupportedInCompass()
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            Calls = [scenario.Calls[0] with
            {
                EnabledMultiplierIds = ["auto-model-selection", "data-residency-fedramp"]
            }]
        };

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Equal("compass-multiplier-unsupported", preview.ErrorCode);
        AssertNonEstimate(preview);
    }

    [Fact]
    public void PricedButUnverifiedCatalogModelDoesNotReceiveAProfileApproval()
    {
        var configuration = CompassFixture.Configuration();
        var model = configuration.Models.First(value => value.Availability is null);
        var scenario = CompassFixture.Scenario();
        scenario = scenario with { Calls = [scenario.Calls[0] with { ModelId = model.Id }] };

        var preview = new SimulationPreviewService(configuration).Preview(scenario);

        Assert.Equal(ModelEligibilityReasonCodes.Unverified, preview.ErrorCode);
        AssertNonEstimate(preview);
    }

    [Fact]
    public void RetiredAvailabilityRemainsSeparateFromItsStillEffectivePrice()
    {
        var configuration = CompassFixture.Configuration();
        var model = configuration.Models.First(value => value.Availability?.RetiredAt <= CompassFixture.Timestamp);
        var scenario = CompassFixture.Scenario();
        scenario = scenario with { Calls = [scenario.Calls[0] with { ModelId = model.Id }] };

        var preview = new SimulationPreviewService(configuration).Preview(scenario);

        Assert.Equal(ModelEligibilityReasonCodes.Retired, preview.ErrorCode);
        AssertNonEstimate(preview);
        Assert.Empty(new SimulationPreviewService(configuration).Compare(scenario));
    }

    [Fact]
    public void ModelPlanRestrictionsUseTheReferenceEvaluatorInsteadOfPriceOnlyApproval()
    {
        var configuration = CompassFixture.Configuration();
        configuration = configuration with
        {
            Models = configuration.Models.Select(model => model.Id == CompassFixture.ModelId
                ? model with
                {
                    Availability = model.Availability! with { EligiblePlanIds = ["enterprise"] }
                }
                : model).ToArray()
        };

        var preview = new SimulationPreviewService(configuration).Preview(CompassFixture.Scenario());

        Assert.Equal(ModelEligibilityReasonCodes.PlanIneligible, preview.ErrorCode);
        AssertNonEstimate(preview);
    }

    [Fact]
    public void UnpublishedTokenComponentIsNotPricedAtZero()
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with { Calls = [scenario.Calls[0] with { CacheWriteTokens = 1 }] };

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Equal(ModelEligibilityReasonCodes.TokenComponentUnpriced, preview.ErrorCode);
        AssertNonEstimate(preview);
    }

    [Fact]
    public void UnknownAutoRouteCannotBeInferredFromPaidPlanEligibility()
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            Calls = [scenario.Calls[0] with { ModelId = "gpt-6-astra", EnabledMultiplierIds = ["auto-model-selection"] }]
        };

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Equal(ModelEligibilityReasonCodes.AutoModelUnverified, preview.ErrorCode);
        AssertNonEstimate(preview);
    }

    [Fact]
    public void PreviewCarriesReferenceIdentityAndCalendarBoundaryAssumptions()
    {
        var configuration = CompassFixture.Configuration();

        var preview = new SimulationPreviewService(configuration).Preview(CompassFixture.Scenario());

        Assert.Equal(configuration.Version, preview.CatalogVersion);
        Assert.Equal(configuration.ReferenceSnapshot!.Id, preview.ReferenceSnapshotId);
        Assert.Equal(configuration.ReferenceSnapshot.VerifiedOn, preview.ReferenceVerifiedOn);
        Assert.Equal(CompassFixture.Timestamp, preview.Timestamp);
        Assert.All(configuration.ReferenceSnapshot.Assumptions,
            assumption => Assert.Contains(assumption, preview.Assumptions));
    }

    private static void AssertNonEstimate(SimulationPreview preview)
    {
        Assert.NotNull(preview.ErrorMessage);
        Assert.Null(preview.Result);
        Assert.Null(preview.InitialRemaining);
        Assert.Null(preview.RequiredAiCredits);
        Assert.Null(preview.ProposedIncludedCredits);
        Assert.Null(preview.ProposedAiUsd);
        Assert.Null(preview.ProposedActionsUsd);
        Assert.Null(preview.ProposedTotalUsd);
        Assert.Null(preview.AcceptedAiCredits);
        Assert.Null(preview.AcceptedTotalUsd);
    }
}
