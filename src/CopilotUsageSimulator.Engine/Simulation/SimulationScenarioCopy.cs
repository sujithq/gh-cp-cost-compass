namespace CopilotUsageSimulator.Engine.Simulation;

internal static class SimulationScenarioCopy
{
    public static SimulationScenario Create(SimulationScenario scenario) => scenario with
    {
        Calls = scenario.Calls.Select(call => call with
        {
            EnabledMultiplierIds = call.EnabledMultiplierIds.ToArray(),
            Metadata = Copy(call.Metadata)
        }).ToArray(),
        AccessGates = scenario.AccessGates.ToDictionary(entry => entry.Key, entry => entry.Value with { }),
        Metadata = Copy(scenario.Metadata),
        ActionsUsage = scenario.ActionsUsage is null ? null : scenario.ActionsUsage with { },
        BillingContext = scenario.BillingContext is null ? null : scenario.BillingContext with
        {
            SeatAssignments = scenario.BillingContext.SeatAssignments.Select(seat => seat with { }).ToArray()
        },
        Attribution = scenario.Attribution is null ? null : scenario.Attribution with
        {
            DirectAssignments = scenario.Attribution.DirectAssignments.Select(value => value with { }).ToArray(),
            TeamAssignments = scenario.Attribution.TeamAssignments.Select(value => value with { }).ToArray(),
            OrganizationAssignments = scenario.Attribution.OrganizationAssignments.Select(value => value with { }).ToArray(),
            LicensingOrganizationIds = scenario.Attribution.LicensingOrganizationIds.ToArray()
        },
        EconomicGuardrails = scenario.EconomicGuardrails is null ? null : scenario.EconomicGuardrails with
        {
            UserLevelBudgets = scenario.EconomicGuardrails.UserLevelBudgets.Select(value => value with { }).ToArray(),
            IncludedUsageControls = scenario.EconomicGuardrails.IncludedUsageControls.Select(value => value with { }).ToArray(),
            PaidUsage = scenario.EconomicGuardrails.PaidUsage with
            {
                ProductIds = Copy(scenario.EconomicGuardrails.PaidUsage.ProductIds),
                SkuIds = Copy(scenario.EconomicGuardrails.PaidUsage.SkuIds)
            },
            SpendingBudgets = scenario.EconomicGuardrails.SpendingBudgets.Select(value => value with
            {
                ProductIds = Copy(value.ProductIds),
                SkuIds = Copy(value.SkuIds)
            }).ToArray(),
            EnterpriseBudgetExcludedCostCenterIds = Copy(scenario.EconomicGuardrails.EnterpriseBudgetExcludedCostCenterIds)
        },
        RuntimeGuardrails = scenario.RuntimeGuardrails is null ? null : scenario.RuntimeGuardrails with { },
        ActionsGuardrails = scenario.ActionsGuardrails is null ? null : scenario.ActionsGuardrails with
        {
            Budgets = scenario.ActionsGuardrails.Budgets.Select(value => value with { }).ToArray()
        }
    };

    private static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string> values) =>
        values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    private static IReadOnlySet<string> Copy(IReadOnlySet<string> values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
