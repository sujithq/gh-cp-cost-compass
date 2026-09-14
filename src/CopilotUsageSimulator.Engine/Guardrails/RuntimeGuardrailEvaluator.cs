using CopilotUsageSimulator.Common.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Guardrails;

public sealed class RuntimeGuardrailEvaluator
{
    public RuntimeGuardrailEvaluation EvaluateBeforeCalls(SimulationScenario scenario)
    {
        var snapshot = scenario.RuntimeGuardrails;
        if (snapshot is null)
        {
            return RuntimeGuardrailEvaluation.Allowed();
        }

        var applied = new List<AppliedGuardrail>();
        if (snapshot.MaximumModelCalls is not null)
        {
            var requestedCalls = scenario.Calls.Count;
            var remaining = snapshot.MaximumModelCalls.Value - snapshot.ModelCallsConsumed;
            var blocked = requestedCalls > remaining;
            applied.Add(Create(
                GuardrailMetadataKeys.RuntimeModelCalls,
                blocked,
                snapshot.MaximumModelCalls,
                snapshot.ModelCallsConsumed,
                requestedCalls,
                remaining - requestedCalls,
                "model calls"));
            if (blocked)
            {
                return RuntimeGuardrailEvaluation.Blocked(GuardrailMetadataKeys.RuntimeModelCalls, applied);
            }
        }

        if (snapshot.MaximumSubagentDepth is not null)
        {
            var blocked = snapshot.RequestedSubagentDepth > snapshot.MaximumSubagentDepth;
            applied.Add(Create(
                GuardrailMetadataKeys.RuntimeSubagentDepth,
                blocked,
                snapshot.MaximumSubagentDepth,
                0,
                snapshot.RequestedSubagentDepth,
                snapshot.MaximumSubagentDepth.Value - snapshot.RequestedSubagentDepth,
                "subagent depth"));
            if (blocked)
            {
                return RuntimeGuardrailEvaluation.Blocked(GuardrailMetadataKeys.RuntimeSubagentDepth, applied);
            }
        }

        if (snapshot.MaximumDuration is not null)
        {
            var limit = (decimal)snapshot.MaximumDuration.Value.TotalMinutes;
            var consumed = (decimal)snapshot.ElapsedDuration.TotalMinutes;
            var requested = (decimal)snapshot.RequestedDuration.TotalMinutes;
            var blocked = consumed + requested > limit;
            applied.Add(Create(
                GuardrailMetadataKeys.RuntimeDuration,
                blocked,
                limit,
                consumed,
                requested,
                limit - consumed - requested,
                "runtime duration"));
            if (blocked)
            {
                return RuntimeGuardrailEvaluation.Blocked(GuardrailMetadataKeys.RuntimeDuration, applied);
            }
        }

        return new RuntimeGuardrailEvaluation(SimulationDecision.Allowed, null, applied);
    }

    public RuntimeGuardrailEvaluation EvaluateCredits(
        RuntimeGuardrailSnapshot? snapshot,
        decimal requestedCredits)
    {
        if (snapshot?.CliSoftCreditLimit is null)
        {
            return RuntimeGuardrailEvaluation.Allowed();
        }

        var remaining = snapshot.CliSoftCreditLimit.Value - snapshot.CliCreditsConsumed;
        var stopped = requestedCredits > remaining;
        var guardrail = new AppliedGuardrail
        {
            Id = GuardrailMetadataKeys.RuntimeCliCredits,
            MetadataKey = GuardrailMetadataKeys.RuntimeCliCredits,
            Category = GuardrailCategories.Runtime,
            Enforcement = GuardrailEnforcement.SoftStop,
            Outcome = stopped ? GuardrailOutcome.SoftStopped : GuardrailOutcome.Passed,
            Limit = snapshot.CliSoftCreditLimit,
            ConsumedBefore = snapshot.CliCreditsConsumed,
            Requested = requestedCredits,
            RemainingAfter = remaining - requestedCredits,
            Message = stopped
                ? "The configured CLI session credit limit would be exceeded."
                : "The CLI session credit limit permits the request."
        };
        return new RuntimeGuardrailEvaluation(
            stopped ? SimulationDecision.SoftStopped : SimulationDecision.Allowed,
            stopped ? guardrail.Id : null,
            [guardrail]);
    }

    private static AppliedGuardrail Create(
        string id,
        bool blocked,
        decimal? limit,
        decimal consumed,
        decimal requested,
        decimal remaining,
        string label) =>
        new()
        {
            Id = id,
            MetadataKey = id,
            Category = GuardrailCategories.Runtime,
            Enforcement = GuardrailEnforcement.HardStop,
            Outcome = blocked ? GuardrailOutcome.Blocked : GuardrailOutcome.Passed,
            Limit = limit,
            ConsumedBefore = consumed,
            Requested = requested,
            RemainingAfter = remaining,
            Message = blocked
                ? $"The {label} guardrail blocks the request."
                : $"The {label} guardrail permits the request."
        };
}

public sealed record RuntimeGuardrailEvaluation(
    SimulationDecision Decision,
    string? FailingGuardrailId,
    IReadOnlyList<AppliedGuardrail> AppliedGuardrails)
{
    public static RuntimeGuardrailEvaluation Allowed() =>
        new(SimulationDecision.Allowed, null, []);

    public static RuntimeGuardrailEvaluation Blocked(
        string id,
        IReadOnlyList<AppliedGuardrail> applied) =>
        new(SimulationDecision.Blocked, id, applied);
}
