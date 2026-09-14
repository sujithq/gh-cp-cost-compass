namespace CopilotUsageSimulator.Engine.Guardrails;

public sealed record BillingContext
{
    public required string BillingEntityId { get; init; }
    public required DateTimeOffset CycleStart { get; init; }
    public required DateTimeOffset CycleEnd { get; init; }
    public IReadOnlyList<EffectiveSeatAssignment> SeatAssignments { get; init; } = [];
}

public sealed record EffectiveSeatAssignment
{
    public required string UserId { get; init; }
    public required string PlanId { get; init; }
    public string? CostCenterId { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset? EffectiveTo { get; init; }
}

public sealed record AttributionInput
{
    public required string UserId { get; init; }
    public IReadOnlyList<EffectiveCostCenterAssignment> DirectAssignments { get; init; } = [];
    public IReadOnlyList<EffectiveTeamCostCenterAssignment> TeamAssignments { get; init; } = [];
    public IReadOnlyList<string> LicensingOrganizationIds { get; init; } = [];
    public string? CycleSelectedLicensingOrganizationId { get; init; }
    public IReadOnlyList<EffectiveOrganizationCostCenterAssignment> OrganizationAssignments { get; init; } = [];
}

public sealed record EffectiveCostCenterAssignment
{
    public required string CostCenterId { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset? EffectiveTo { get; init; }
}

public sealed record EffectiveTeamCostCenterAssignment
{
    public required string TeamId { get; init; }
    public required string CostCenterId { get; init; }
    public required DateTimeOffset TeamCreatedAt { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset? EffectiveTo { get; init; }
}

public sealed record EffectiveOrganizationCostCenterAssignment
{
    public required string OrganizationId { get; init; }
    public required string CostCenterId { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset? EffectiveTo { get; init; }
}

public enum AttributionRule
{
    DirectUser,
    EnterpriseTeam,
    LicensingOrganization,
    EnterpriseOnly,
    UnresolvedMultipleLicensingOrganizations,
    AmbiguousAttribution
}

public sealed record AttributionResult
{
    public required string UserId { get; init; }
    public string? LicensingOrganizationId { get; init; }
    public string? CostCenterId { get; init; }
    public AttributionRule Rule { get; init; }
    public GuardrailOutcome Outcome { get; init; }
    public required string Explanation { get; init; }
}

public sealed class AttributionResolver
{
    public AttributionResult Resolve(AttributionInput input, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!string.IsNullOrWhiteSpace(input.CycleSelectedLicensingOrganizationId) &&
            !input.LicensingOrganizationIds.Contains(
                input.CycleSelectedLicensingOrganizationId,
                StringComparer.OrdinalIgnoreCase))
        {
            return Indeterminate(
                input.UserId,
                "The cycle-selected licensing organization is not one of the user's licensing organizations.");
        }

        var licensingOrganizationId = ResolveLicensingOrganization(input);
        if (licensingOrganizationId is null && input.LicensingOrganizationIds.Count > 1)
        {
            return Indeterminate(
                input.UserId,
                "Multiple licensing organizations exist and no cycle-selected organization was supplied.");
        }

        var direct = input.DirectAssignments
            .Where(x => IsEffective(x.EffectiveFrom, x.EffectiveTo, timestamp))
            .ToArray();
        if (direct.Length > 1)
        {
            return Indeterminate(input.UserId, "Multiple direct cost-center assignments are effective.");
        }

        if (direct.Length == 1)
        {
            return Resolved(
                input.UserId,
                direct[0].CostCenterId,
                licensingOrganizationId,
                AttributionRule.DirectUser,
                "Direct user assignment selected the cost center.");
        }

        var team = input.TeamAssignments
            .Where(x => IsEffective(x.EffectiveFrom, x.EffectiveTo, timestamp))
            .OrderBy(x => x.TeamCreatedAt)
            .ThenBy(x => x.TeamId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (team is not null)
        {
            return Resolved(
                input.UserId,
                team.CostCenterId,
                licensingOrganizationId,
                AttributionRule.EnterpriseTeam,
                $"Enterprise team '{team.TeamId}' selected the cost center.");
        }

        var organizationId = licensingOrganizationId;

        if (organizationId is not null)
        {
            var organizationAssignments = input.OrganizationAssignments
                .Where(x =>
                    string.Equals(x.OrganizationId, organizationId, StringComparison.OrdinalIgnoreCase) &&
                    IsEffective(x.EffectiveFrom, x.EffectiveTo, timestamp))
                .ToArray();
            if (organizationAssignments.Length > 1)
            {
                return Indeterminate(input.UserId, $"Organization '{organizationId}' has multiple effective cost centers.");
            }

            if (organizationAssignments.Length == 1)
            {
                return Resolved(
                    input.UserId,
                    organizationAssignments[0].CostCenterId,
                    organizationId,
                    AttributionRule.LicensingOrganization,
                    $"Licensing organization '{organizationId}' selected the cost center.");
            }
        }

        return Resolved(
            input.UserId,
            null,
            organizationId,
            AttributionRule.EnterpriseOnly,
            "No cost center applies; usage is enterprise-only.");
    }

    private static string? ResolveLicensingOrganization(AttributionInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.CycleSelectedLicensingOrganizationId))
        {
            return input.LicensingOrganizationIds.Contains(
                input.CycleSelectedLicensingOrganizationId,
                StringComparer.OrdinalIgnoreCase)
                ? input.CycleSelectedLicensingOrganizationId
                : null;
        }

        return input.LicensingOrganizationIds.Count == 1 ? input.LicensingOrganizationIds[0] : null;
    }

    private static bool IsEffective(
        DateTimeOffset from,
        DateTimeOffset? to,
        DateTimeOffset timestamp) =>
        timestamp >= from && (to is null || timestamp < to);

    private static AttributionResult Resolved(
        string userId,
        string? costCenterId,
        string? organizationId,
        AttributionRule rule,
        string explanation) =>
        new()
        {
            UserId = userId,
            CostCenterId = costCenterId,
            LicensingOrganizationId = organizationId,
            Rule = rule,
            Outcome = GuardrailOutcome.Passed,
            Explanation = explanation
        };

    private static AttributionResult Indeterminate(string userId, string explanation) =>
        new()
        {
            UserId = userId,
            Rule = explanation.Contains("Multiple licensing organizations", StringComparison.OrdinalIgnoreCase)
                ? AttributionRule.UnresolvedMultipleLicensingOrganizations
                : AttributionRule.AmbiguousAttribution,
            Outcome = GuardrailOutcome.Indeterminate,
            Explanation = explanation
        };
}
