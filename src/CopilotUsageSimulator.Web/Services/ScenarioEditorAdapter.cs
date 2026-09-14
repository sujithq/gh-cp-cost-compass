using System.Text.Json;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public sealed class ScenarioEditorAdapter(
    WorkloadEditorAdapter workload,
    AttributionEditorAdapter attribution,
    EconomicEditorAdapter economic,
    RuntimeEditorAdapter runtime,
    ActionsEditorAdapter actions)
{
    public ScenarioEditorState MapFromScenario(
        SimulationScenario scenario,
        EngineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(configuration);

        return new ScenarioEditorState
        {
            Workload = workload.MapFromScenario(scenario, configuration),
            Attribution = attribution.MapFromScenario(scenario),
            Economic = economic.MapFromScenario(scenario),
            Runtime = runtime.MapFromScenario(scenario),
            Actions = actions.MapFromScenario(scenario)
        };
    }

    public SimulationScenario ApplyToScenario(
        SimulationScenario scenario,
        ScenarioEditorState state,
        EngineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(configuration);

        var patched = workload.ApplyToScenario(scenario, state.Workload);
        patched = attribution.ApplyToScenario(patched, state.Attribution);
        patched = economic.ApplyToScenario(patched, state.Economic);
        patched = actions.ApplyToScenario(patched, state.Actions, configuration);
        return runtime.ApplyToScenario(patched, state.Runtime);
    }

    public SimulationScenario ApplyChangedSectionsToScenario(
        SimulationScenario scenario,
        ScenarioEditorState state,
        ScenarioEditorState baseline,
        EngineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(configuration);

        var patched = Changed(state.Workload, baseline.Workload)
            ? workload.ApplyChangesToScenario(scenario, state.Workload, baseline.Workload)
            : scenario;
        var attributionChanged = Changed(state.Attribution, baseline.Attribution);
        if (attributionChanged)
        {
            patched = attribution.ApplyToScenario(patched, state.Attribution);
        }
        if (attributionChanged || Changed(state.Economic, baseline.Economic))
        {
            patched = economic.ApplyToScenario(patched, state.Economic);
        }
        if (Changed(state.Actions, baseline.Actions))
        {
            var source = patched;
            patched = actions.ApplyToScenario(patched, state.Actions, configuration);
            if (source.ActionsGuardrails is null)
            {
                patched = patched with { ActionsGuardrails = null };
            }
            if (source.ActionsUsage is null && state.Actions.Minutes == baseline.Actions.Minutes)
            {
                patched = patched with { ActionsUsage = null };
            }
        }
        return Changed(state.Runtime, baseline.Runtime)
            ? runtime.ApplyToScenario(patched, state.Runtime)
            : patched;
    }

    // UI sections contain scalar values; compare the mapped form, not domain snapshots with different defaults.
    private static bool Changed<T>(T current, T baseline) =>
        JsonSerializer.Serialize(current) != JsonSerializer.Serialize(baseline);
}
