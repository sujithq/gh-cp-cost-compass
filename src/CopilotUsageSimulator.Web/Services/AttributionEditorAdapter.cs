using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public sealed class AttributionEditorAdapter
{
    public AttributionEditorState MapFromScenario(SimulationScenario scenario)
    {
        var directAssignments = scenario.Attribution?.DirectAssignments ?? [];
        var directAssignmentIndex = directAssignments
            .Select((assignment, index) => new { assignment, index })
            .Where(item =>
                scenario.Timestamp >= item.assignment.EffectiveFrom &&
                (item.assignment.EffectiveTo is null || scenario.Timestamp < item.assignment.EffectiveTo))
            .Select(item => (int?)item.index)
            .FirstOrDefault() ?? (directAssignments.Count > 0 ? 0 : null);
        var costCenterId = directAssignmentIndex is null
            ? null
            : directAssignments[directAssignmentIndex.Value].CostCenterId;
        var resolvedAttribution = scenario.Attribution is null
            ? null
            : new AttributionResolver().Resolve(scenario.Attribution, scenario.Timestamp);
        costCenterId = resolvedAttribution?.Outcome == GuardrailOutcome.Passed
            ? resolvedAttribution.CostCenterId
            : costCenterId;
        var organizationId = resolvedAttribution?.LicensingOrganizationId ??
            scenario.Attribution?.LicensingOrganizationIds.FirstOrDefault();
        var licensingOrganizationIndex = scenario.Attribution?.LicensingOrganizationIds
            .Select((id, index) => new { id, index })
            .Where(item => string.Equals(item.id, organizationId, StringComparison.OrdinalIgnoreCase))
            .Select(item => (int?)item.index)
            .FirstOrDefault();

        return new AttributionEditorState
        {
            CostCenterId = costCenterId ?? "",
            DirectAssignmentIndex = directAssignmentIndex,
            OrganizationId = organizationId ?? "",
            LicensingOrganizationIndex = licensingOrganizationIndex
        };
    }

    public SimulationScenario ApplyToScenario(
        SimulationScenario scenario,
        AttributionEditorState state)
    {
        if (scenario.Attribution is null)
        {
            return scenario;
        }

        var licensingOrganizations = ScenarioEditorPatchHelpers.PatchAt(
            scenario.Attribution.LicensingOrganizationIds,
            state.LicensingOrganizationIndex,
            !string.IsNullOrWhiteSpace(state.OrganizationId),
            _ => state.OrganizationId,
            () => state.OrganizationId);
        var directAssignments = ScenarioEditorPatchHelpers.PatchAt(
            scenario.Attribution.DirectAssignments,
            state.DirectAssignmentIndex,
            !string.IsNullOrWhiteSpace(state.CostCenterId),
            assignment => assignment with { CostCenterId = state.CostCenterId },
            () => new EffectiveCostCenterAssignment { CostCenterId = state.CostCenterId });
        var attribution = scenario.Attribution with
        {
            LicensingOrganizationIds = licensingOrganizations,
            DirectAssignments = directAssignments
        };
        var billing = scenario.BillingContext is null
            ? null
            : scenario.BillingContext with
            {
                SeatAssignments = PatchEffectiveSeat(
                    scenario.BillingContext.SeatAssignments,
                    attribution.UserId,
                    scenario.Timestamp,
                    ScenarioEditorPatchHelpers.NullIfWhiteSpace(state.CostCenterId))
            };
        var patched = scenario with
        {
            Attribution = attribution,
            BillingContext = billing
        };

        state.LicensingOrganizationIndex = ScenarioEditorPatchHelpers.ResolvePatchedIndex(
            licensingOrganizations.Count,
            state.LicensingOrganizationIndex,
            !string.IsNullOrWhiteSpace(state.OrganizationId));
        state.DirectAssignmentIndex = ScenarioEditorPatchHelpers.ResolvePatchedIndex(
            directAssignments.Count,
            state.DirectAssignmentIndex,
            !string.IsNullOrWhiteSpace(state.CostCenterId));
        return patched;
    }

    private static IReadOnlyList<EffectiveSeatAssignment> PatchEffectiveSeat(
        IReadOnlyList<EffectiveSeatAssignment> seats,
        string userId,
        DateTimeOffset timestamp,
        string? costCenterId)
    {
        var patched = seats.ToList();
        var index = patched.FindIndex(seat =>
            string.Equals(seat.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
            timestamp >= seat.EffectiveFrom &&
            (seat.EffectiveTo is null || timestamp < seat.EffectiveTo));
        if (index >= 0)
        {
            patched[index] = patched[index] with { CostCenterId = costCenterId };
        }

        return patched;
    }
}
