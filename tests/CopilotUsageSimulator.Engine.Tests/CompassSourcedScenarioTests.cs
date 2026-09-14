using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class CompassSourcedScenarioTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SolWithMixedSeatsSupportsTheExactCompassDemo(bool includeSpendingBudget)
    {
        var scenario = CompassFixture.Scenario();
        scenario = scenario with
        {
            Calls =
            [
                new()
                {
                    ModelId = "gpt-5.6-sol",
                    ContextTokens = 100_000,
                    FreshInputTokens = 50_000,
                    OutputTokens = 40_000
                }
            ],
            BillingContext = scenario.BillingContext! with
            {
                SeatAssignments =
                [
                    new() { UserId = "user-1", PlanId = "business", CostCenterId = "cc-1" },
                    new() { UserId = "user-2", PlanId = "enterprise", CostCenterId = "cc-2" }
                ]
            },
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                EnterprisePoolConsumedCredits = 5_740m,
                SpendingBudgets = includeSpendingBudget
                    ?
                    [
                        new()
                        {
                            Id = "cc-1-spend",
                            Scope = SpendingBudgetScope.CostCenter,
                            ScopeId = "cc-1",
                            LimitUsd = .20m,
                            Enforcement = GuardrailEnforcement.HardStop
                        }
                    ]
                    : []
            },
            Metadata = new Dictionary<string, string> { ["fixture"] = "sourced-sol-september" }
        };
        var service = new SimulationPreviewService(EngineConfigurationLoader.LoadDefault());

        var preview = service.Preview(scenario);
        var comparisons = service.Compare(scenario);
        var reduction = Assert.Single(comparisons, candidate => candidate.Id == "reduce-to-included");
        var paid = Assert.Single(comparisons, candidate => candidate.Id == "enable-paid-usage");

        Assert.Null(preview.ErrorCode);
        Assert.Equal(60m, preview.InitialRemaining!.IncludedPoolCredits);
        Assert.Equal(1m, preview.RequiredModelUsd);
        Assert.Equal(100m, preview.RequiredAiCredits);
        Assert.Equal(60m, preview.ProposedIncludedCredits);
        Assert.Equal(40m, preview.ProposedMeteredCredits);
        Assert.Equal(.40m, preview.ProposedAiUsd);
        Assert.Equal(.40m, preview.ProposedTotalUsd);
        Assert.Equal("paid-usage", preview.Result!.FirstFailingGate);
        Assert.Equal(0m, preview.AcceptedAiCredits);
        Assert.Equal(0m, preview.AcceptedTotalUsd);
        Assert.Equal(0m, preview.Result.Allocation.TotalCredits);
        Assert.Equal(60m, preview.Result.Remaining.IncludedPoolCredits);

        Assert.True(reduction.Resolved);
        Assert.Equal(30_000, reduction.CandidateScenario.Calls[0].FreshInputTokens);
        Assert.Equal(24_000, reduction.CandidateScenario.Calls[0].OutputTokens);
        Assert.Equal(60m, reduction.Candidate.RequiredAiCredits);
        Assert.Equal(60m, reduction.Candidate.AcceptedAiCredits);
        Assert.Equal(0m, reduction.Candidate.AcceptedTotalUsd);
        Assert.Equal(0m, reduction.Candidate.Result!.Remaining.IncludedPoolCredits);

        Assert.Equal(.40m, paid.Candidate.ProposedTotalUsd);
        Assert.Equal(!includeSpendingBudget, paid.Resolved);
        Assert.Equal(includeSpendingBudget ? "cc-1-spend" : null, paid.Candidate.Result!.FirstFailingGate);
        Assert.Equal(includeSpendingBudget ? 0m : 100m, paid.Candidate.AcceptedAiCredits);
        Assert.Equal(includeSpendingBudget ? 0m : .40m, paid.Candidate.AcceptedTotalUsd);
        if (includeSpendingBudget)
        {
            Assert.Equal(.20m, paid.Candidate.InitialRemaining!.SpendingBudgetRemainingUsd["cc-1-spend"]);
            Assert.Equal(.20m, paid.Candidate.Result.Remaining.SpendingBudgetRemainingUsd["cc-1-spend"]);
            Assert.Empty(paid.Candidate.Result.Alerts);
        }
    }
}
