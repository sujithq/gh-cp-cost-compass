using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public sealed class SimulationResultsState
{
    public SimulationResult? Result { get; private set; }

    public List<SimulationResult> Runs { get; } = [];

    public string VisibilityMode { get; set; } = "all";

    public HashSet<string> HiddenCategories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<AppliedGuardrail> VisibleGuardrails =>
        Result?.AppliedGuardrails.Where(ShouldShow) ?? [];

    public AppliedGuardrail? BlockingGuardrail =>
        Result?.AppliedGuardrails.FirstOrDefault(guardrail =>
            string.Equals(guardrail.Id, Result.FirstFailingGate, StringComparison.OrdinalIgnoreCase))
        ?? Result?.AppliedGuardrails.FirstOrDefault(guardrail =>
            guardrail.Outcome is GuardrailOutcome.Blocked
                or GuardrailOutcome.SoftStopped
                or GuardrailOutcome.Waiting
                or GuardrailOutcome.Indeterminate);

    public int CompletedRuns => Runs.Count(run => run.Decision == SimulationDecision.Allowed);

    public decimal TotalConsumedCredits => AllowedRuns.Sum(run => run.Allocation.TotalCredits);

    public decimal TotalIncludedCredits => AllowedRuns.Sum(run => run.Allocation.IncludedCredits);

    public decimal TotalMeteredCredits => AllowedRuns.Sum(run => run.Allocation.MeteredCredits);

    public decimal TotalMeteredUsd => AllowedRuns.Sum(run => run.Allocation.MeteredUsd);

    public decimal TotalActionsUsd => AllowedRuns.Sum(run => run.ActionsUsage?.AdditionalUsd ?? 0m);

    private IEnumerable<SimulationResult> AllowedRuns =>
        Runs.Where(run => run.Decision == SimulationDecision.Allowed);

    public void SetRuns(IReadOnlyList<SimulationResult> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        Runs.Clear();
        Runs.AddRange(runs);
        Result = Runs.LastOrDefault();
    }

    public void Clear()
    {
        Result = null;
        Runs.Clear();
    }

    public void SetCategoryVisibility(string category, bool visible)
    {
        if (visible)
        {
            HiddenCategories.Remove(category);
        }
        else
        {
            HiddenCategories.Add(category);
        }
    }

    public void ApplyPreferences(DisplayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        VisibilityMode = preferences.VisibilityMode;
        HiddenCategories.Clear();
        HiddenCategories.UnionWith(preferences.HiddenCategories);
    }

    public DisplayPreferences GetPreferences() =>
        new(VisibilityMode, HiddenCategories.ToArray());

    private bool ShouldShow(AppliedGuardrail guardrail) =>
        VisibilityMode switch
        {
            "issues" => guardrail.Outcome != GuardrailOutcome.Passed,
            "failures" => guardrail.Outcome is GuardrailOutcome.Blocked or GuardrailOutcome.Indeterminate,
            "custom" => !HiddenCategories.Contains(guardrail.Category),
            _ => true
        };
}

public sealed record DisplayPreferences(string VisibilityMode, string[] HiddenCategories);
