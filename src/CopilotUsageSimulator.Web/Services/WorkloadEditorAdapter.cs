using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public sealed class WorkloadEditorAdapter
{
    public WorkloadEditorState MapFromScenario(
        SimulationScenario scenario,
        EngineConfiguration configuration)
    {
        var call = scenario.Calls.FirstOrDefault();

        return new WorkloadEditorState
        {
            Task = scenario.Metadata.GetValueOrDefault("task", ""),
            OperationId = scenario.OperationId,
            PlanId = scenario.PlanId,
            CostChecksOnly = scenario.CheckScope == SimulationCheckScope.CostRelatedOnly,
            RepositoryVisibility = scenario.RepositoryVisibility,
            ModelId = call?.ModelId ?? configuration.Models.First().Id,
            ContextTokens = call?.ContextTokens ?? 0,
            FreshInputTokens = call?.FreshInputTokens ?? 0,
            CachedInputTokens = call?.CachedInputTokens ?? 0,
            CacheWriteTokens = call?.CacheWriteTokens ?? 0,
            OutputTokens = call?.OutputTokens ?? 0,
            RepeatCount = ReadRepeatCount(scenario.Metadata)
        };
    }

    public SimulationScenario ApplyToScenario(
        SimulationScenario scenario,
        WorkloadEditorState state)
    {
        var call = scenario.Calls.FirstOrDefault() ?? new ModelCallInput { ModelId = state.ModelId };
        call = call with
        {
            ModelId = state.ModelId,
            ContextTokens = state.ContextTokens,
            FreshInputTokens = state.FreshInputTokens,
            CachedInputTokens = state.CachedInputTokens,
            CacheWriteTokens = state.CacheWriteTokens,
            OutputTokens = state.OutputTokens
        };
        var metadata = new Dictionary<string, string>(scenario.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["task"] = state.Task,
            ["repeatCount"] = state.RepeatCount.ToString()
        };

        return ApplyPlanSelection(
            scenario with
            {
                OperationId = state.OperationId,
                CheckScope = state.CostChecksOnly
                    ? SimulationCheckScope.CostRelatedOnly
                    : SimulationCheckScope.All,
                RepositoryVisibility = state.RepositoryVisibility,
                Calls = ScenarioEditorPatchHelpers.PatchFirst(scenario.Calls, _ => call, () => call),
                Metadata = metadata
            },
            state.PlanId);
    }

    public SimulationScenario ApplyChangesToScenario(
        SimulationScenario scenario,
        WorkloadEditorState state,
        WorkloadEditorState baseline)
    {
        var proposed = ApplyToScenario(scenario, state);
        var callChanged = state.ModelId != baseline.ModelId ||
            state.ContextTokens != baseline.ContextTokens ||
            state.FreshInputTokens != baseline.FreshInputTokens ||
            state.CachedInputTokens != baseline.CachedInputTokens ||
            state.CacheWriteTokens != baseline.CacheWriteTokens ||
            state.OutputTokens != baseline.OutputTokens;
        var metadata = new Dictionary<string, string>(scenario.Metadata, StringComparer.OrdinalIgnoreCase);
        if (state.Task != baseline.Task) metadata["task"] = state.Task;
        if (state.RepeatCount != baseline.RepeatCount) metadata["repeatCount"] = state.RepeatCount.ToString();
        return proposed with
        {
            Calls = callChanged ? proposed.Calls : scenario.Calls,
            BillingContext = state.PlanId != baseline.PlanId ? proposed.BillingContext : scenario.BillingContext,
            Metadata = metadata
        };
    }

    private static SimulationScenario ApplyPlanSelection(
        SimulationScenario scenario,
        string planId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);

        if (scenario.BillingContext is null || scenario.Attribution is null)
        {
            return scenario with { PlanId = planId };
        }

        var userId = scenario.Attribution.UserId;
        var matchedEffectiveSeat = false;
        var seatAssignments = scenario.BillingContext.SeatAssignments
            .Select(seat =>
            {
                if (!string.Equals(seat.UserId, userId, StringComparison.OrdinalIgnoreCase) ||
                    scenario.Timestamp < seat.EffectiveFrom ||
                    (seat.EffectiveTo is not null && scenario.Timestamp >= seat.EffectiveTo))
                {
                    return seat;
                }

                matchedEffectiveSeat = true;
                return seat with { PlanId = planId };
            })
            .ToList();

        if (!matchedEffectiveSeat)
        {
            seatAssignments.Add(new EffectiveSeatAssignment
            {
                UserId = userId,
                PlanId = planId,
                EffectiveFrom = scenario.Timestamp
            });
        }

        return scenario with
        {
            PlanId = planId,
            BillingContext = scenario.BillingContext with
            {
                SeatAssignments = seatAssignments
            }
        };
    }

    private static int ReadRepeatCount(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue("repeatCount", out var value) &&
        int.TryParse(value, out var count) &&
        count is >= 1 and <= 1000
            ? count
            : 1;
}
