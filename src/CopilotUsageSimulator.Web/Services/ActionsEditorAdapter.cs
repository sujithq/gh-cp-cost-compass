using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public sealed class ActionsEditorAdapter
{
    public ActionsEditorState MapFromScenario(SimulationScenario scenario)
    {
        var budget = scenario.ActionsGuardrails?.Budgets.FirstOrDefault();
        return new ActionsEditorState
        {
            Minutes = scenario.ActionsUsage?.Minutes ?? 0,
            UseBudget = budget is not null,
            BudgetId = budget?.Id,
            BudgetLimit = budget?.LimitUsd ?? 0,
            BudgetConsumed = budget?.ConsumedUsd ?? 0,
            BudgetEnforcement = budget?.Enforcement ?? GuardrailEnforcement.HardStop
        };
    }

    public SimulationScenario ApplyToScenario(
        SimulationScenario scenario,
        ActionsEditorState state,
        EngineConfiguration configuration)
    {
        var operation = configuration.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, scenario.OperationId, StringComparison.OrdinalIgnoreCase));
        var operationDefaults = operation?.ActionsMetering != ActionsMeteringMode.None
            ? ExampleScenarioFactory.Create(configuration, scenario.OperationId)
            : null;
        var sourceActionsUsage = scenario.ActionsUsage ?? operationDefaults?.ActionsUsage;
        var actionsUsage = sourceActionsUsage is null
            ? null
            : sourceActionsUsage with { Minutes = state.Minutes };
        var sourceGuardrails = scenario.ActionsGuardrails ?? operationDefaults?.ActionsGuardrails;
        if (sourceGuardrails is null)
        {
            return scenario with { ActionsUsage = actionsUsage };
        }

        var budgets = ScenarioEditorPatchHelpers.PatchById(
            sourceGuardrails.Budgets,
            state.BudgetId,
            state.UseBudget,
            "actions-budget",
            budget => budget.Id,
            budget => budget with
            {
                LimitUsd = state.BudgetLimit,
                ConsumedUsd = state.BudgetConsumed,
                Enforcement = state.BudgetEnforcement
            },
            id => new ActionsSpendingBudget
            {
                Id = id,
                LimitUsd = state.BudgetLimit,
                ConsumedUsd = state.BudgetConsumed,
                Enforcement = state.BudgetEnforcement
            });
        state.BudgetId = ScenarioEditorPatchHelpers.ResolvePatchedId(
            budgets,
            state.BudgetId,
            state.UseBudget,
            "actions-budget",
            budget => budget.Id);

        return scenario with
        {
            ActionsUsage = actionsUsage,
            ActionsGuardrails = sourceGuardrails with { Budgets = budgets }
        };
    }
}
