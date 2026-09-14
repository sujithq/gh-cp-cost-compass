using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class CompassActionsContextTests
{
    [Fact]
    public void FullScopeRequiresActionsContextRatherThanAssumingPermission()
    {
        var scenario = MissingActionsContext();

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Equal("compass-actions-context-required", preview.ErrorCode);
        Assert.Null(preview.Result);
        Assert.Null(preview.RequiredAiCredits);
        Assert.Null(scenario.ActionsGuardrails);
    }

    [Fact]
    public void ExplicitCostOnlyScopeCanPriceWithoutClaimingAccessWasVerified()
    {
        var scenario = MissingActionsContext() with { CheckScope = SimulationCheckScope.CostRelatedOnly };

        var preview = new SimulationPreviewService(CompassFixture.Configuration()).Preview(scenario);

        Assert.Null(preview.ErrorCode);
        Assert.Equal(SimulationDecision.Allowed, preview.Result!.Decision);
        Assert.Equal(.54m, preview.ProposedActionsUsd);
        Assert.All(preview.Result.Trace.Where(entry => entry.Stage == "actions-access"),
            entry => Assert.Equal(SimulationTraceState.Excluded, entry.State));
        Assert.Null(scenario.ActionsGuardrails);
    }

    [Fact]
    public void DirectEngineRetainsItsLegacyOptionalSnapshotContract()
    {
        var result = new CopilotUsageSimulationEngine(CompassFixture.Configuration())
            .Simulate(MissingActionsContext());

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
    }

    private static SimulationScenario MissingActionsContext() =>
        CompassFixture.Cloud(CompassFixture.Scenario(600_000)) with { ActionsGuardrails = null };
}
