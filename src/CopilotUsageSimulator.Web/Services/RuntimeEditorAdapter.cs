using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public sealed class RuntimeEditorAdapter
{
    public RuntimeEditorState MapFromScenario(SimulationScenario scenario) =>
        new()
        {
            Enabled = scenario.RuntimeGuardrails is not null,
            MaximumModelCalls = scenario.RuntimeGuardrails?.MaximumModelCalls,
            ModelCallsConsumed = scenario.RuntimeGuardrails?.ModelCallsConsumed ?? 0,
            MaximumSubagentDepth = scenario.RuntimeGuardrails?.MaximumSubagentDepth,
            RequestedSubagentDepth = scenario.RuntimeGuardrails?.RequestedSubagentDepth ?? 0,
            MaximumDurationMinutes = scenario.RuntimeGuardrails?.MaximumDuration is null
                ? null
                : (decimal)scenario.RuntimeGuardrails.MaximumDuration.Value.TotalMinutes,
            ElapsedDurationMinutes = (decimal)(scenario.RuntimeGuardrails?.ElapsedDuration.TotalMinutes ?? 0),
            RequestedDurationMinutes = (decimal)(scenario.RuntimeGuardrails?.RequestedDuration.TotalMinutes ?? 0),
            CliSoftCreditLimit = scenario.RuntimeGuardrails?.CliSoftCreditLimit,
            CliCreditsConsumed = scenario.RuntimeGuardrails?.CliCreditsConsumed ?? 0
        };

    public SimulationScenario ApplyToScenario(
        SimulationScenario scenario,
        RuntimeEditorState state)
    {
        if (!state.Enabled)
        {
            return scenario with { RuntimeGuardrails = null };
        }

        var runtime = (scenario.RuntimeGuardrails ?? new RuntimeGuardrailSnapshot()) with
        {
            MaximumModelCalls = state.MaximumModelCalls,
            ModelCallsConsumed = state.ModelCallsConsumed,
            MaximumSubagentDepth = state.MaximumSubagentDepth,
            RequestedSubagentDepth = state.RequestedSubagentDepth,
            MaximumDuration = state.MaximumDurationMinutes is null
                ? null
                : TimeSpan.FromMinutes((double)state.MaximumDurationMinutes.Value),
            ElapsedDuration = TimeSpan.FromMinutes((double)state.ElapsedDurationMinutes),
            RequestedDuration = TimeSpan.FromMinutes((double)state.RequestedDurationMinutes),
            CliSoftCreditLimit = state.CliSoftCreditLimit,
            CliCreditsConsumed = state.CliCreditsConsumed
        };
        return scenario with { RuntimeGuardrails = runtime };
    }
}
