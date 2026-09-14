using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.Engine.Simulation;

public sealed class SimulationSessionRunner
{
    public SimulationSessionResult Run(
        ICopilotUsageSimulationEngine engine,
        SimulationScenario scenario,
        int repeatCount)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(scenario);

        if (repeatCount is < 1 or > 1000)
        {
            throw new SimulationException("Repeat task must be between 1 and 1000.", "repeat-count-invalid");
        }

        var runs = new List<SimulationResult>();
        var current = scenario;
        var balances = new EconomicBalanceCalculator(engine.Configuration);
        for (var iteration = 0; iteration < repeatCount; iteration++)
        {
            var result = engine.Simulate(current);
            runs.Add(result);
            if (result.Decision != SimulationDecision.Allowed)
            {
                break;
            }

            current = AdvanceScenario(current, result, balances);
        }

        return new SimulationSessionResult(runs, current);
    }

    private static SimulationScenario AdvanceScenario(
        SimulationScenario scenario,
        SimulationResult result,
        EconomicBalanceCalculator balances)
    {
        var economic = scenario.EconomicGuardrails is null
            ? null
            : balances.ApplyAllocation(scenario.EconomicGuardrails, result);

        var runtime = scenario.RuntimeGuardrails;
        if (runtime is not null &&
            scenario.CheckScope == SimulationCheckScope.All &&
            result.Calls.Count > 0)
        {
            runtime = runtime with
            {
                ModelCallsConsumed = runtime.ModelCallsConsumed + result.Calls.Count,
                ElapsedDuration = runtime.ElapsedDuration + runtime.RequestedDuration,
                CliCreditsConsumed = runtime.CliCreditsConsumed + result.Allocation.TotalCredits
            };
        }

        var actions = scenario.ActionsGuardrails;
        if (actions is not null && result.ActionsUsage is not null)
        {
            actions = actions with
            {
                ConsumedIncludedMinutes =
                    actions.ConsumedIncludedMinutes + result.ActionsUsage.IncludedMinutes,
                Budgets = actions.Budgets
                    .Select(budget => budget with
                    {
                        ConsumedUsd = budget.ConsumedUsd + result.ActionsUsage.AdditionalUsd
                    })
                    .ToArray()
            };
        }

        return scenario with
        {
            EconomicGuardrails = economic,
            RuntimeGuardrails = runtime,
            ActionsGuardrails = actions
        };
    }

}

public sealed record SimulationSessionResult(
    IReadOnlyList<SimulationResult> Runs,
    SimulationScenario NextScenario)
{
    public int CompletedRuns => Runs.Count(run => run.Decision == SimulationDecision.Allowed);
}