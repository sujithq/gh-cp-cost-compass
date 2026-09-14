using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public sealed class EconomicEditorAdapter
{
    public EconomicEditorState MapFromScenario(SimulationScenario scenario)
    {
        var resolvedAttribution = scenario.Attribution is null
            ? null
            : new AttributionResolver().Resolve(scenario.Attribution, scenario.Timestamp);
        var applicability = new EconomicGuardrailApplicabilityResolver();
        var economic = scenario.EconomicGuardrails;
        var universalUlb = economic is null || resolvedAttribution is null
            ? null
            : applicability.ResolveUserLevelBudget(
                economic,
                resolvedAttribution,
                UserLevelBudgetKind.Universal,
                scenario.Timestamp).Value;
        var costCenterUlb = economic is null || resolvedAttribution is null
            ? null
            : applicability.ResolveUserLevelBudget(
                economic,
                resolvedAttribution,
                UserLevelBudgetKind.CostCenter,
                scenario.Timestamp).Value;
        var individualUlb = economic is null || resolvedAttribution is null
            ? null
            : applicability.ResolveUserLevelBudget(
                economic,
                resolvedAttribution,
                UserLevelBudgetKind.Individual,
                scenario.Timestamp).Value;
        var includedControl = economic is null || resolvedAttribution is null
            ? null
            : applicability.ResolveIncludedUsageControl(
                economic,
                resolvedAttribution,
                scenario.Timestamp).Value;
        var spendingBudgets = economic is null || resolvedAttribution is null
            ? []
            : applicability.ResolveSpendingBudgets(
                economic,
                resolvedAttribution,
                scenario.ProductId,
                scenario.SkuId,
                scenario.Timestamp);
        var enterpriseBudget = EconomicGuardrailApplicabilityResolver.Select(
            spendingBudgets.Where(budget => budget.Scope == SpendingBudgetScope.Enterprise)).Value;
        var costCenterBudget = EconomicGuardrailApplicabilityResolver.Select(
            spendingBudgets.Where(budget => budget.Scope == SpendingBudgetScope.CostCenter)).Value;
        var organizationBudget = EconomicGuardrailApplicabilityResolver.Select(
            spendingBudgets.Where(budget => budget.Scope == SpendingBudgetScope.Organization)).Value;

        return new EconomicEditorState
        {
            PoolConsumed = economic?.EnterprisePoolConsumedCredits ?? 0,
            UseUniversalUlb = universalUlb is not null,
            UniversalUlbId = universalUlb?.Id,
            UniversalUlbLimit = universalUlb?.LimitCredits ?? 0,
            UniversalUlbConsumed = universalUlb?.ConsumedCredits ?? 0,
            UseCostCenterUlb = costCenterUlb is not null,
            CostCenterUlbId = costCenterUlb?.Id,
            CostCenterUlbLimit = costCenterUlb?.LimitCredits ?? 0,
            CostCenterUlbConsumed = costCenterUlb?.ConsumedCredits ?? 0,
            UseIndividualUlb = individualUlb is not null,
            IndividualUlbId = individualUlb?.Id,
            UlbLimit = individualUlb?.LimitCredits ?? 0,
            UlbConsumed = individualUlb?.ConsumedCredits ?? 0,
            UseIncludedControl = includedControl is not null,
            IncludedControlId = includedControl?.Id,
            IncludedControlConsumed = includedControl?.ConsumedCredits ?? 0,
            IncludedOverflow = includedControl?.OverflowBehavior ?? IncludedOverflowBehavior.PaidUsage,
            PaidUsage = economic?.PaidUsage.State ?? GuardrailValue.Unknown,
            PaidUsageProductIds = FormatApplicabilityIds(economic?.PaidUsage.ProductIds),
            PaidUsageSkuIds = FormatApplicabilityIds(economic?.PaidUsage.SkuIds),
            UseCostCenterBudget = costCenterBudget is not null,
            CostCenterBudgetId = costCenterBudget?.Id,
            CostCenterBudgetLimit = costCenterBudget?.LimitUsd ?? 0,
            CostCenterBudgetConsumed = costCenterBudget?.ConsumedUsd ?? 0,
            CostCenterBudgetEnforcement = costCenterBudget?.Enforcement ?? GuardrailEnforcement.HardStop,
            UseOrganizationBudget = organizationBudget is not null,
            OrganizationBudgetId = organizationBudget?.Id,
            OrganizationBudgetLimit = organizationBudget?.LimitUsd ?? 0,
            OrganizationBudgetConsumed = organizationBudget?.ConsumedUsd ?? 0,
            OrganizationBudgetEnforcement = organizationBudget?.Enforcement ?? GuardrailEnforcement.HardStop,
            UseEnterpriseBudget = enterpriseBudget is not null,
            EnterpriseBudgetId = enterpriseBudget?.Id,
            EnterpriseBudgetLimit = enterpriseBudget?.LimitUsd ?? 0,
            EnterpriseBudgetConsumed = enterpriseBudget?.ConsumedUsd ?? 0,
            EnterpriseBudgetEnforcement = enterpriseBudget?.Enforcement ?? GuardrailEnforcement.HardStop
        };
    }

    public SimulationScenario ApplyToScenario(
        SimulationScenario scenario,
        EconomicEditorState state)
    {
        if (scenario.EconomicGuardrails is null)
        {
            return scenario;
        }

        var resolvedAttribution = scenario.Attribution is null
            ? null
            : new AttributionResolver().Resolve(scenario.Attribution, scenario.Timestamp);
        var userId = scenario.Attribution?.UserId;
        var costCenterId = resolvedAttribution?.Outcome == GuardrailOutcome.Passed
            ? resolvedAttribution.CostCenterId
            : scenario.Attribution?.DirectAssignments
                .FirstOrDefault(assignment =>
                    scenario.Timestamp >= assignment.EffectiveFrom &&
                    (assignment.EffectiveTo is null || scenario.Timestamp < assignment.EffectiveTo))
                ?.CostCenterId;
        var organizationId = resolvedAttribution?.LicensingOrganizationId ??
            scenario.Attribution?.LicensingOrganizationIds.FirstOrDefault();
        var economic = UpdateEconomic(
            scenario.EconomicGuardrails,
            userId,
            costCenterId,
            organizationId,
            state);
        CapturePatchedIds(economic, state);
        return scenario with { EconomicGuardrails = economic };
    }

    private static EconomicGuardrailSnapshot UpdateEconomic(
        EconomicGuardrailSnapshot economic,
        string? userId,
        string? costCenterId,
        string? organizationId,
        EconomicEditorState state)
    {
        var budgets = PatchSpendingBudget(
            economic.SpendingBudgets,
            state.CostCenterBudgetId,
            state.UseCostCenterBudget,
            "budget-cost-center",
            SpendingBudgetScope.CostCenter,
            costCenterId,
            state.CostCenterBudgetLimit,
            state.CostCenterBudgetConsumed,
            state.CostCenterBudgetEnforcement);
        budgets = PatchSpendingBudget(
            budgets,
            state.OrganizationBudgetId,
            state.UseOrganizationBudget,
            "budget-organization",
            SpendingBudgetScope.Organization,
            organizationId,
            state.OrganizationBudgetLimit,
            state.OrganizationBudgetConsumed,
            state.OrganizationBudgetEnforcement);
        budgets = PatchSpendingBudget(
            budgets,
            state.EnterpriseBudgetId,
            state.UseEnterpriseBudget,
            "budget-enterprise",
            SpendingBudgetScope.Enterprise,
            null,
            state.EnterpriseBudgetLimit,
            state.EnterpriseBudgetConsumed,
            state.EnterpriseBudgetEnforcement);

        var ulbs = ScenarioEditorPatchHelpers.PatchById(
            economic.UserLevelBudgets,
            state.UniversalUlbId,
            state.UseUniversalUlb,
            "ulb-universal",
            budget => budget.Id,
            budget => budget with
            {
                Kind = UserLevelBudgetKind.Universal,
                TargetId = null,
                LimitCredits = state.UniversalUlbLimit,
                ConsumedCredits = state.UniversalUlbConsumed
            },
            id => new UserLevelBudget
            {
                Id = id,
                Kind = UserLevelBudgetKind.Universal,
                LimitCredits = state.UniversalUlbLimit,
                ConsumedCredits = state.UniversalUlbConsumed
            });
        ulbs = ScenarioEditorPatchHelpers.PatchById(
            ulbs,
            state.CostCenterUlbId,
            state.UseCostCenterUlb && !string.IsNullOrWhiteSpace(costCenterId),
            "ulb-cost-center",
            budget => budget.Id,
            budget => budget with
            {
                Kind = UserLevelBudgetKind.CostCenter,
                TargetId = costCenterId,
                LimitCredits = state.CostCenterUlbLimit,
                ConsumedCredits = state.CostCenterUlbConsumed
            },
            id => new UserLevelBudget
            {
                Id = id,
                Kind = UserLevelBudgetKind.CostCenter,
                TargetId = costCenterId,
                LimitCredits = state.CostCenterUlbLimit,
                ConsumedCredits = state.CostCenterUlbConsumed
            });
        ulbs = ScenarioEditorPatchHelpers.PatchById(
            ulbs,
            state.IndividualUlbId,
            state.UseIndividualUlb,
            "ulb-individual",
            budget => budget.Id,
            budget => budget with
            {
                Kind = UserLevelBudgetKind.Individual,
                TargetId = userId,
                LimitCredits = state.UlbLimit,
                ConsumedCredits = state.UlbConsumed
            },
            id => new UserLevelBudget
            {
                Id = id,
                Kind = UserLevelBudgetKind.Individual,
                TargetId = userId,
                LimitCredits = state.UlbLimit,
                ConsumedCredits = state.UlbConsumed
            });
        var includedControls = ScenarioEditorPatchHelpers.PatchById(
            economic.IncludedUsageControls,
            state.IncludedControlId,
            state.UseIncludedControl && !string.IsNullOrWhiteSpace(costCenterId),
            "included-cost-center",
            control => control.Id,
            control => control with
            {
                CostCenterId = costCenterId!,
                ConsumedCredits = state.IncludedControlConsumed,
                OverflowBehavior = state.IncludedOverflow
            },
            id => new CostCenterIncludedUsageControl
            {
                Id = id,
                CostCenterId = costCenterId!,
                ConsumedCredits = state.IncludedControlConsumed,
                OverflowBehavior = state.IncludedOverflow
            });

        return economic with
        {
            EnterprisePoolConsumedCredits = state.PoolConsumed,
            PaidUsage = economic.PaidUsage with
            {
                State = state.PaidUsage,
                ProductIds = ParseApplicabilityIds(state.PaidUsageProductIds),
                SkuIds = ParseApplicabilityIds(state.PaidUsageSkuIds)
            },
            SpendingBudgets = budgets,
            UserLevelBudgets = ulbs,
            IncludedUsageControls = includedControls
        };
    }

    private static IReadOnlyList<SpendingBudget> PatchSpendingBudget(
        IReadOnlyList<SpendingBudget> budgets,
        string? selectedId,
        bool enabled,
        string defaultId,
        SpendingBudgetScope scope,
        string? scopeId,
        decimal limit,
        decimal consumed,
        GuardrailEnforcement enforcement) =>
        ScenarioEditorPatchHelpers.PatchById(
            budgets,
            selectedId,
            enabled,
            defaultId,
            budget => budget.Id,
            budget => budget with
            {
                Scope = scope,
                ScopeId = scopeId,
                LimitUsd = limit,
                ConsumedUsd = consumed,
                Enforcement = enforcement
            },
            id => new SpendingBudget
            {
                Id = id,
                Scope = scope,
                ScopeId = scopeId,
                LimitUsd = limit,
                ConsumedUsd = consumed,
                Enforcement = enforcement
            });

    private static void CapturePatchedIds(
        EconomicGuardrailSnapshot economic,
        EconomicEditorState state)
    {
        state.UniversalUlbId = ScenarioEditorPatchHelpers.ResolvePatchedId(
            economic.UserLevelBudgets,
            state.UniversalUlbId,
            state.UseUniversalUlb,
            "ulb-universal",
            budget => budget.Id);
        state.CostCenterUlbId = ScenarioEditorPatchHelpers.ResolvePatchedId(
            economic.UserLevelBudgets,
            state.CostCenterUlbId,
            state.UseCostCenterUlb,
            "ulb-cost-center",
            budget => budget.Id);
        state.IndividualUlbId = ScenarioEditorPatchHelpers.ResolvePatchedId(
            economic.UserLevelBudgets,
            state.IndividualUlbId,
            state.UseIndividualUlb,
            "ulb-individual",
            budget => budget.Id);
        state.IncludedControlId = ScenarioEditorPatchHelpers.ResolvePatchedId(
            economic.IncludedUsageControls,
            state.IncludedControlId,
            state.UseIncludedControl,
            "included-cost-center",
            control => control.Id);
        state.CostCenterBudgetId = ScenarioEditorPatchHelpers.ResolvePatchedId(
            economic.SpendingBudgets,
            state.CostCenterBudgetId,
            state.UseCostCenterBudget,
            "budget-cost-center",
            budget => budget.Id);
        state.OrganizationBudgetId = ScenarioEditorPatchHelpers.ResolvePatchedId(
            economic.SpendingBudgets,
            state.OrganizationBudgetId,
            state.UseOrganizationBudget,
            "budget-organization",
            budget => budget.Id);
        state.EnterpriseBudgetId = ScenarioEditorPatchHelpers.ResolvePatchedId(
            economic.SpendingBudgets,
            state.EnterpriseBudgetId,
            state.UseEnterpriseBudget,
            "budget-enterprise",
            budget => budget.Id);
    }

    private static string FormatApplicabilityIds(IReadOnlySet<string>? values) =>
        values is null ? "" : string.Join(", ", values.Order(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlySet<string> ParseApplicabilityIds(string values) =>
        values.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
