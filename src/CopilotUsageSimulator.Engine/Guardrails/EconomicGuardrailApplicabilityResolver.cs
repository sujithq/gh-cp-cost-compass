using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Guardrails;

public sealed class EconomicGuardrailApplicabilityResolver
{
    public ApplicableGuardrailSelection<UserLevelBudget> ResolveUserLevelBudget(
        EconomicGuardrailSnapshot snapshot,
        AttributionResult attribution,
        UserLevelBudgetKind kind,
        DateTimeOffset timestamp) =>
        Select(snapshot.UserLevelBudgets.Where(budget =>
            budget.Kind == kind &&
            IsEffective(budget.EffectiveFrom, budget.EffectiveTo, timestamp) &&
            kind switch
            {
                UserLevelBudgetKind.Individual =>
                    string.Equals(budget.TargetId, attribution.UserId, StringComparison.OrdinalIgnoreCase),
                UserLevelBudgetKind.CostCenter =>
                    attribution.CostCenterId is not null &&
                    string.Equals(
                        budget.TargetId,
                        attribution.CostCenterId,
                        StringComparison.OrdinalIgnoreCase),
                UserLevelBudgetKind.Universal => budget.TargetId is null,
                _ => false
            }));

    public ApplicableGuardrailSelection<UserLevelBudget> ResolveEffectiveUserLevelBudget(
        EconomicGuardrailSnapshot snapshot,
        AttributionResult attribution,
        DateTimeOffset timestamp)
    {
        foreach (var kind in Enum.GetValues<UserLevelBudgetKind>().OrderDescending())
        {
            var selection = ResolveUserLevelBudget(snapshot, attribution, kind, timestamp);
            if (selection.Matches.Count > 0)
            {
                return selection;
            }
        }

        return ApplicableGuardrailSelection<UserLevelBudget>.None;
    }

    public ApplicableGuardrailSelection<CostCenterIncludedUsageControl> ResolveIncludedUsageControl(
        EconomicGuardrailSnapshot snapshot,
        AttributionResult attribution,
        DateTimeOffset timestamp)
    {
        if (attribution.CostCenterId is null)
        {
            return ApplicableGuardrailSelection<CostCenterIncludedUsageControl>.None;
        }

        return Select(snapshot.IncludedUsageControls.Where(control =>
            string.Equals(
                control.CostCenterId,
                attribution.CostCenterId,
                StringComparison.OrdinalIgnoreCase) &&
            IsEffective(control.EffectiveFrom, control.EffectiveTo, timestamp)));
    }

    public IReadOnlyList<SpendingBudget> ResolveSpendingBudgets(
        EconomicGuardrailSnapshot snapshot,
        AttributionResult attribution,
        string productId,
        string skuId,
        DateTimeOffset timestamp)
    {
        var effective = snapshot.SpendingBudgets.Where(budget =>
            IsEffective(budget.EffectiveFrom, budget.EffectiveTo, timestamp) &&
            (budget.TrackingStartedAt is null || timestamp >= budget.TrackingStartedAt) &&
            Matches(budget.ProductIds, productId) &&
            Matches(budget.SkuIds, skuId));

        var applicable = new List<SpendingBudget>();
        if (attribution.CostCenterId is not null)
        {
            applicable.AddRange(effective.Where(budget =>
                budget.Scope == SpendingBudgetScope.CostCenter &&
                string.Equals(
                    budget.ScopeId,
                    attribution.CostCenterId,
                    StringComparison.OrdinalIgnoreCase)));
        }

        if (applicable.Count == 0 && attribution.LicensingOrganizationId is not null)
        {
            applicable.AddRange(effective.Where(budget =>
                budget.Scope == SpendingBudgetScope.Organization &&
                string.Equals(
                    budget.ScopeId,
                    attribution.LicensingOrganizationId,
                    StringComparison.OrdinalIgnoreCase)));
        }

        var excluded = attribution.CostCenterId is not null &&
            snapshot.EnterpriseBudgetExcludedCostCenterIds.Contains(
                attribution.CostCenterId,
                StringComparer.OrdinalIgnoreCase);
        if (!excluded)
        {
            applicable.AddRange(effective.Where(
                budget => budget.Scope == SpendingBudgetScope.Enterprise));
        }

        return applicable;
    }

    public static ApplicableGuardrailSelection<T> Select<T>(IEnumerable<T> candidates)
    {
        var matches = candidates.ToArray();
        return new ApplicableGuardrailSelection<T>(
            matches.Length == 1 ? matches[0] : default,
            matches);
    }

    public static bool IsEffective(
        DateTimeOffset from,
        DateTimeOffset? to,
        DateTimeOffset timestamp) =>
        timestamp >= from && (to is null || timestamp < to);

    public static bool Matches(IReadOnlySet<string> configured, string value) =>
        configured.Count == 0 || configured.Contains(value, StringComparer.OrdinalIgnoreCase);
}

public sealed record ApplicableGuardrailSelection<T>(
    T? Value,
    IReadOnlyList<T> Matches)
{
    public static ApplicableGuardrailSelection<T> None { get; } = new(default, []);

    public bool IsAmbiguous => Matches.Count > 1;
}
