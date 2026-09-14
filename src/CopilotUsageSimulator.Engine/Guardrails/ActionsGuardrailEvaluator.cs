using CopilotUsageSimulator.Common.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Guardrails;

public sealed class ActionsGuardrailEvaluator
{
    private static readonly decimal[] AlertThresholds = [75m, 90m, 100m];

    public ActionsGuardrailEvaluation Evaluate(
        ActionsGuardrailSnapshot snapshot,
        ActionsUsageResult usage)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(usage);

        var access = EvaluateAccess(snapshot);
        if (access.Decision != SimulationDecision.Allowed)
        {
            return access;
        }

        var budgets = EvaluateBudgets(snapshot, usage);
        return budgets with
        {
            AppliedGuardrails = access.AppliedGuardrails.Concat(budgets.AppliedGuardrails).ToArray()
        };
    }

    public ActionsGuardrailEvaluation EvaluateAccess(ActionsGuardrailSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var applied = new List<AppliedGuardrail>();
        var alerts = new List<ThresholdEvent>();

        var stateResult = EvaluateState(
            GuardrailMetadataKeys.ActionsEnabled,
            "GitHub Actions",
            snapshot.ActionsEnabled,
            disabledDecision: SimulationDecision.Blocked);
        applied.Add(stateResult.Guardrail);
        if (stateResult.Decision != SimulationDecision.Allowed)
        {
            return new(stateResult.Decision, stateResult.Guardrail.Id, applied, alerts);
        }

        stateResult = EvaluateState(
            GuardrailMetadataKeys.ActionsRunnerAvailable,
            "Actions runner",
            snapshot.RunnerAvailable,
            disabledDecision: SimulationDecision.Blocked);
        applied.Add(stateResult.Guardrail);
        if (stateResult.Decision != SimulationDecision.Allowed)
        {
            return new(stateResult.Decision, stateResult.Guardrail.Id, applied, alerts);
        }

        stateResult = EvaluateState(
            GuardrailMetadataKeys.ActionsWorkflowApproval,
            "Workflow approval",
            snapshot.WorkflowApproved,
            disabledDecision: SimulationDecision.Waiting);
        applied.Add(stateResult.Guardrail);
        if (stateResult.Decision != SimulationDecision.Allowed)
        {
            return new(stateResult.Decision, stateResult.Guardrail.Id, applied, alerts);
        }

        stateResult = EvaluateState(
            GuardrailMetadataKeys.ActionsRepositoryRules,
            "Repository rules",
            snapshot.RepositoryRulesPermitRun,
            disabledDecision: SimulationDecision.Blocked);
        applied.Add(stateResult.Guardrail);
        if (stateResult.Decision != SimulationDecision.Allowed)
        {
            return new(stateResult.Decision, stateResult.Guardrail.Id, applied, alerts);
        }

        return new ActionsGuardrailEvaluation(SimulationDecision.Allowed, null, applied, alerts);
    }

    public ActionsGuardrailEvaluation EvaluateBudgets(
        ActionsGuardrailSnapshot snapshot,
        ActionsUsageResult usage)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(usage);
        var applied = new List<AppliedGuardrail>();
        var alerts = new List<ThresholdEvent>();
        var budgetAlerts = new List<ThresholdEvent>();
        ActionsSpendingBudget? blocker = null;
        decimal? lowestHeadroom = null;
        foreach (var budget in snapshot.Budgets)
        {
            var headroom = budget.LimitUsd - budget.ConsumedUsd;
            var blocks = budget.Enforcement == GuardrailEnforcement.HardStop &&
                usage.AdditionalUsd > headroom;
            applied.Add(new AppliedGuardrail
            {
                Id = budget.Id,
                MetadataKey = GuardrailMetadataKeys.ActionsBudget,
                Category = GuardrailCategories.ActionsBudget,
                Enforcement = budget.Enforcement,
                Outcome = blocks ? GuardrailOutcome.Blocked : GuardrailOutcome.Passed,
                Limit = budget.LimitUsd,
                ConsumedBefore = budget.ConsumedUsd,
                Requested = usage.AdditionalUsd,
                RemainingAfter = headroom - usage.AdditionalUsd,
                Message = blocks
                    ? "The Actions spending budget blocks the runner charge."
                    : "The Actions spending budget permits or observes the runner charge."
            });
            AddAlerts(budgetAlerts, budget, usage.AdditionalUsd);
            if (blocks && (lowestHeadroom is null || headroom < lowestHeadroom))
            {
                blocker = budget;
                lowestHeadroom = headroom;
            }
        }

        if (blocker is null)
        {
            alerts.AddRange(budgetAlerts);
        }

        return new ActionsGuardrailEvaluation(
            blocker is null ? SimulationDecision.Allowed : SimulationDecision.Blocked,
            blocker?.Id,
            applied,
            alerts);
    }

    private static StateEvaluation EvaluateState(
        string id,
        string label,
        GuardrailValue value,
        SimulationDecision disabledDecision)
    {
        var decision = value switch
        {
            GuardrailValue.Enabled => SimulationDecision.Allowed,
            GuardrailValue.Disabled => disabledDecision,
            GuardrailValue.Unknown => SimulationDecision.Indeterminate,
            _ => SimulationDecision.Indeterminate
        };
        var outcome = decision switch
        {
            SimulationDecision.Allowed => GuardrailOutcome.Passed,
            SimulationDecision.Waiting => GuardrailOutcome.Waiting,
            SimulationDecision.Blocked => GuardrailOutcome.Blocked,
            _ => GuardrailOutcome.Indeterminate
        };
        return new StateEvaluation(
            decision,
            new AppliedGuardrail
            {
                Id = id,
                MetadataKey = id,
                Category = GuardrailCategories.ActionsAccess,
                Enforcement = GuardrailEnforcement.HardStop,
                Outcome = outcome,
                Message = $"{label} is {value.ToString().ToLowerInvariant()}."
            });
    }

    private static void AddAlerts(
        ICollection<ThresholdEvent> alerts,
        ActionsSpendingBudget budget,
        decimal requested)
    {
        if (budget.LimitUsd <= 0)
        {
            return;
        }

        var before = budget.ConsumedUsd / budget.LimitUsd * 100m;
        var after = (budget.ConsumedUsd + requested) / budget.LimitUsd * 100m;
        foreach (var threshold in AlertThresholds.Where(x => before < x && after >= x))
        {
            alerts.Add(new ThresholdEvent
            {
                GuardrailId = budget.Id,
                ThresholdPercent = threshold,
                BeforePercent = before,
                AfterPercent = after
            });
        }
    }

    private sealed record StateEvaluation(
        SimulationDecision Decision,
        AppliedGuardrail Guardrail);
}

public sealed record ActionsGuardrailEvaluation(
    SimulationDecision Decision,
    string? FailingGuardrailId,
    IReadOnlyList<AppliedGuardrail> AppliedGuardrails,
    IReadOnlyList<ThresholdEvent> Alerts);
