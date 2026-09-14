using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.Engine.Simulation;

internal static class CompassPreviewProfile
{
    private static readonly DateTimeOffset StartsAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndsAt = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    public static void Validate(EngineConfiguration configuration, SimulationScenario scenario)
    {
        if (!IsOrganizationalPlan(scenario.PlanId) ||
            configuration.Plans.SingleOrDefault(plan => IdEquals(plan.Id, scenario.PlanId))?.IsPooled != true)
        {
            Unsupported("compass-plan-unsupported",
                "Cost Compass supports pooled Business and Enterprise seats, not personal, legacy annual, or unlicensed billing.");
        }

        if (scenario.Timestamp < StartsAt || scenario.Timestamp >= EndsAt)
        {
            Unsupported("compass-date-unsupported",
                "This Compass profile covers standard September 1–27, 2026 scenarios. Historical promotional cohorts and September 28/October rollout-dependent billing are not verified by this profile.");
        }

        if (scenario.BillingContext?.SeatAssignments.Any(seat =>
                EconomicGuardrailApplicabilityResolver.IsEffective(seat.EffectiveFrom, seat.EffectiveTo, scenario.Timestamp) &&
                !IsOrganizationalPlan(seat.PlanId)) == true)
        {
            Unsupported("compass-seat-inventory-unsupported",
                "Every active seat in the Compass pooled inventory must use Business or Enterprise.");
        }

        var cloudAgent = IdEquals(scenario.OperationId, "cloud-agent");
        if (!cloudAgent && !IdEquals(scenario.OperationId, "chat") && !IdEquals(scenario.OperationId, "cli"))
        {
            Unsupported("compass-operation-unsupported",
                "Cost Compass supports Chat, CLI, and a constrained cloud-agent workload. Code review and standalone Actions require separate billing evidence and are not supported estimates.");
        }

        var operation = configuration.Operations.SingleOrDefault(value => IdEquals(value.Id, scenario.OperationId));
        if (operation?.IsBilled != true ||
            (cloudAgent ? operation.ActionsMetering == ActionsMeteringMode.None : operation.ActionsMetering != ActionsMeteringMode.None))
        {
            Unsupported("compass-operation-evidence-unsupported",
                "The configured operation's billing meters do not match the supported Compass workload.");
        }

        if (cloudAgent)
        {
            if (scenario.RepositoryVisibility != RepositoryVisibility.Private ||
                scenario.ActionsUsage is not { } usage || !IdEquals(usage.RunnerId, "linux-2-core"))
            {
                Unsupported("compass-actions-unsupported",
                    "The cloud-agent profile requires a private repository, standard linux-2-core runner, and supplied Actions usage with a separate GitHub-account allowance.");
            }

            if (scenario.ActionsUsage!.Minutes != decimal.Truncate(scenario.ActionsUsage.Minutes))
            {
                Unsupported("compass-actions-minutes-unsupported",
                    "Supply whole, already-accounted Actions minutes after rounding every job up. Raw runtime and aggregate fractional-minute rounding are not modeled.");
            }

            if (scenario.CheckScope == SimulationCheckScope.All && scenario.ActionsGuardrails is null)
            {
                Unsupported("compass-actions-context-required",
                    "Full-scope cloud-agent previews require a supplied Actions guardrail snapshot. Explicit cost-only scope can estimate usage without verifying Actions access or approvals.");
            }
        }

        if (scenario.Calls.Any(call => call.EnabledMultiplierIds.Any(id => !IdEquals(id, "auto-model-selection"))))
        {
            Unsupported("compass-multiplier-unsupported",
                "Only documented Auto model-cost adjustment is supported by this profile. Compliance model restrictions, stacking, and custom adjustments lack verified Compass support.");
        }
    }

    public static IReadOnlyList<string> Assumptions(SimulationScenario scenario)
    {
        var assumptions = new List<string>
        {
            "Compass profile: standard September 2026 pooled Business/Enterprise AI credits; no historical promotional, personal, legacy annual, or future payment/rollout cohort is assumed.",
            "This is an offline incremental-usage what-if, not a live access check or a complete invoice. Seat fees, tax, currency conversion, and infrastructure costs are excluded.",
            "Seat inventory, billing attribution, access states, existing consumption, and guardrail snapshots are supplied assumptions, not discovered GitHub settings.",
            "Required model value includes included usage. Proposed additional charges are payable only if all evaluated gates allow the request; rejected previews accept no usage."
        };
        if (IdEquals(scenario.OperationId, "cloud-agent"))
        {
            assumptions.Add("Cloud agent assumes a private repository and standard linux-2-core runner. Input minutes are pre-accounted whole minutes after independent per-job ceiling, not raw runtime.");
            assumptions.Add("The Actions allowance is supplied separately for the GitHub account; it is never derived from the Copilot seat plan. Actions payer/account details are not independently discovered.");
        }

        if (scenario.CheckScope == SimulationCheckScope.CostRelatedOnly)
        {
            assumptions.Add("Cost-related-only scope explicitly excludes access and runtime verification.");
        }

        return assumptions.ToArray();
    }

    private static bool IsOrganizationalPlan(string id) => IdEquals(id, "business") || IdEquals(id, "enterprise");

    private static bool IdEquals(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void Unsupported(string code, string message) => throw new SimulationException(message, code);
}
