using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.BundleTool;

internal sealed class SnapshotFinancialMapper(
    EnterpriseSnapshot snapshot,
    ImportOverrides overrides,
    EngineConfiguration catalog,
    ResolvedInventory inventory,
    List<ImportDiagnostic> diagnostics)
{
    private readonly DateTimeOffset _timestamp = snapshot.CaptureCompletedAt.ToUniversalTime();
    private readonly EconomicGuardrailApplicabilityResolver _applicability = new();
    private readonly HashSet<string> _usedBudgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedControls = new(StringComparer.OrdinalIgnoreCase);

    public EconomicGuardrailSnapshot Map()
    {
        var balances = new EconomicBalanceCalculator(catalog);
        var entitlement = balances.CalculatePoolEntitlement(inventory.Billing, _timestamp);
        var expected = ImportChecks.Amount(overrides.ExpectedPoolEntitlementCredits, "overrides.expectedPoolEntitlementCredits");
        ImportChecks.Require(entitlement.IsKnown && entitlement.Credits == expected, "pool-entitlement-mismatch",
            $"Confirmed pool entitlement ({expected}) must match the effective seat inventory ({entitlement.Credits}). Reconcile retained allowances and seat changes before exporting.");
        var consumed = ImportChecks.Amount(overrides.EnterprisePoolConsumedCredits, "overrides.enterprisePoolConsumedCredits");
        ImportChecks.Require(overrides.EnterpriseBudgetExcludedCostCenterIds is not null, "exclusions-unconfirmed",
            "Confirm overrides.enterpriseBudgetExcludedCostCenterIds explicitly, including [] when none are excluded.");
        ImportChecks.Unique(overrides.EnterpriseBudgetExcludedCostCenterIds!, "overrides.enterpriseBudgetExcludedCostCenterIds");
        var excluded = overrides.EnterpriseBudgetExcludedCostCenterIds!.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in excluded)
        {
            ImportChecks.Require(snapshot.CostCenters.Any(center => ImportChecks.Same(center.Id, id) && ImportChecks.Same(center.State, "active")),
                "exclusion-target-unknown", $"Excluded cost center '{id}' must identify an active observed cost center.");
        }

        var attribution = new AttributionResolver().Resolve(inventory.Attribution, _timestamp);
        var controls = new List<CostCenterIncludedUsageControl>();
        if (attribution.CostCenterId is { } costCenterId)
        {
            var center = snapshot.CostCenters.Single(center => ImportChecks.Same(center.Id, costCenterId));
            _usedControls.Add(center.Id);
            var confirmed = ImportChecks.Find(overrides.IncludedControls, center.Id);
            var enabled = confirmed?.Enabled ?? center.AiCreditPoolEnabled;
            ImportChecks.Require(enabled is not null, "included-control-unconfirmed",
                $"Confirm includedControls.{center.Id}.enabled; the API did not expose the setting.");
            if (enabled == true)
            {
                var cap = balances.CalculateCostCenterEntitlement(inventory.Billing, center.Id, _timestamp);
                var confirmedCap = ImportChecks.Amount(confirmed?.EntitlementCredits, $"includedControls.{center.Id}.entitlementCredits");
                ImportChecks.Require(cap.IsKnown && cap.Credits == confirmedCap, "included-entitlement-mismatch",
                    $"Cost center '{center.Id}' confirmed cap does not match its effective seat entitlement. Raw targetAmount/currentAmount are not assumed to be credits.");
                ImportChecks.Require(confirmed?.OverflowBehavior is not null, "overflow-unconfirmed",
                    $"Confirm includedControls.{center.Id}.overflowBehavior (block or paidUsage).");
                controls.Add(new CostCenterIncludedUsageControl
                {
                    Id = $"included-{center.Id}", CostCenterId = center.Id, EffectiveFrom = _timestamp,
                    ConsumedCredits = ImportChecks.Amount(confirmed?.ConsumedCredits, $"includedControls.{center.Id}.consumedCredits"),
                    OverflowBehavior = confirmed!.OverflowBehavior!.Value
                });
            }
        }

        var userDefinitions = new List<UserLevelBudget>();
        var spendingDefinitions = new List<SpendingBudget>();
        foreach (var budget in snapshot.Budgets.OrderBy(budget => budget.Id, StringComparer.Ordinal))
        {
            if (!IsAiBudget(budget)) continue;
            if (budget.Scope is "user" or "multi_user_customer" or "multi_user_cost_center")
            {
                userDefinitions.Add(new UserLevelBudget
                {
                    Id = budget.Id,
                    Kind = budget.Scope switch
                    {
                        "user" => UserLevelBudgetKind.Individual,
                        "multi_user_cost_center" => UserLevelBudgetKind.CostCenter,
                        _ => UserLevelBudgetKind.Universal
                    },
                    TargetId = budget.Scope switch
                    {
                        "user" => ImportChecks.Identifier(budget.User, $"budgets.{budget.Id}.user"),
                        "multi_user_cost_center" => CostCenterId(budget.EntityName),
                        _ => null
                    },
                    EffectiveFrom = _timestamp,
                    EffectiveTo = Expiration(budget)
                });
            }
            else
            {
                var scope = budget.Scope switch
                {
                    "enterprise" => SpendingBudgetScope.Enterprise,
                    "organization" => SpendingBudgetScope.Organization,
                    "cost_center" => SpendingBudgetScope.CostCenter,
                    _ => throw new ImportException("budget-scope-unsupported", $"AI budget '{budget.Id}' uses unsupported scope '{budget.Scope}'; it cannot be ignored safely.")
                };
                spendingDefinitions.Add(new SpendingBudget
                {
                    Id = budget.Id, Scope = scope,
                    ScopeId = scope switch
                    {
                        SpendingBudgetScope.CostCenter => CostCenterId(budget.EntityName),
                        SpendingBudgetScope.Organization => ImportChecks.Identifier(budget.EntityName, $"budgets.{budget.Id}.entityName"),
                        _ => null
                    },
                    EffectiveFrom = _timestamp,
                    EffectiveTo = Expiration(budget),
                    TrackingStartedAt = ImportChecks.Find(overrides.Budgets, budget.Id)?.TrackingStartedAt
                });
            }
        }

        var selection = new EconomicGuardrailSnapshot
        {
            UserLevelBudgets = userDefinitions,
            SpendingBudgets = spendingDefinitions,
            EnterpriseBudgetExcludedCostCenterIds = excluded
        };
        var effectiveUlb = _applicability.ResolveEffectiveUserLevelBudget(selection, attribution, _timestamp).Value;
        var observedEffective = snapshot.EffectiveUserBudget;
        if (observedEffective is not null)
        {
            ImportChecks.Require(ImportChecks.Same(observedEffective.User, snapshot.SelectedUser) &&
                ImportChecks.Same(observedEffective.BudgetId, effectiveUlb?.Id), "effective-user-budget-mismatch",
                "The API's effective user budget disagrees with the Engine-selected identity. Recollect or reconcile definitions before creating a bundle.");
        }
        var userBudgets = new List<UserLevelBudget>();
        foreach (var kind in Enum.GetValues<UserLevelBudgetKind>())
        {
            var candidate = _applicability.ResolveUserLevelBudget(selection, attribution, kind, _timestamp);
            ImportChecks.Require(!candidate.IsAmbiguous, "ambiguous-user-budget", $"Multiple {kind} budgets apply to the selected user.");
            if (candidate.Value is not { } definition) continue;
            _usedBudgets.Add(definition.Id);
            var source = snapshot.Budgets.Single(budget => budget.Id == definition.Id);
            var confirmation = ImportChecks.Find(overrides.Budgets, source.Id);
            var states = snapshot.UserBudgetStates.Where(state => ImportChecks.Same(state.BudgetId, source.Id) &&
                ImportChecks.Same(state.User, snapshot.SelectedUser)).ToArray();
            ImportChecks.Require(states.Length <= 1, "duplicate-user-budget-state", $"Multiple states for '{snapshot.SelectedUser}' under budget '{source.Id}'.");
            var state = states.SingleOrDefault();
            var evidence = ImportChecks.Same(definition.Id, observedEffective?.BudgetId) ? observedEffective : null;
            var limit = ImportChecks.Amount(confirmation?.LimitUsd ?? source.BudgetAmount, $"budgets.{source.Id}.limitUsd");
            var spent = ImportChecks.Amount(confirmation?.ConsumedUsd ?? state?.ConsumedAmount ?? evidence?.ConsumedAmount ??
                (kind == UserLevelBudgetKind.Individual ? source.ConsumedAmount : null), $"budgets.{source.Id}.consumedUsd for {snapshot.SelectedUser}");
            if (confirmation?.LimitUsd is null)
            {
                ImportChecks.Require(evidence?.TargetAmount is null || evidence.TargetAmount == limit,
                    "user-budget-limit-mismatch", "Effective per-user limit differs from its budget definition; recollect or confirm the correct limit.");
                if (state is not null && (state.OverrideBudgetId is null || ImportChecks.Same(state.OverrideBudgetId, source.Id)))
                    ImportChecks.Require(state.TargetAmount is null || state.TargetAmount == limit, "user-budget-limit-mismatch",
                        "Per-user target amount differs from its budget definition; an explicit limit confirmation is required.");
            }
            if (confirmation?.ConsumedUsd is null)
                ImportChecks.Require(evidence?.ConsumedAmount is null || evidence.ConsumedAmount == spent,
                    "user-budget-consumption-mismatch", "Per-user consumption changed across endpoints; recollect or explicitly confirm the baseline.");
            var enforcement = Enforcement(source, confirmation);
            ImportChecks.Require(enforcement == GuardrailEnforcement.HardStop, "user-budget-not-hard",
                $"ULB '{source.Id}' must be a hard stop, not an alert-only spending budget.");
            userBudgets.Add(definition with { LimitCredits = limit / catalog.UsdPerCredit, ConsumedCredits = spent / catalog.UsdPerCredit });
        }

        foreach (var state in snapshot.UserBudgetStates.Where(state => ImportChecks.Same(state.User, snapshot.SelectedUser) && state.OverrideBudgetId is not null))
        {
            ImportChecks.Require(ImportChecks.Same(state.OverrideBudgetId, effectiveUlb?.Id), "effective-user-budget-mismatch",
                "The returned per-user override budget disagrees with observed budget precedence. Recollect or reconcile this snapshot.");
        }

        var spending = _applicability.ResolveSpendingBudgets(selection, attribution, "github-copilot", "copilot-ai-credits", _timestamp)
            .Select(definition =>
            {
                var source = snapshot.Budgets.Single(budget => budget.Id == definition.Id);
                _usedBudgets.Add(source.Id);
                var confirmation = ImportChecks.Find(overrides.Budgets, source.Id);
                WarnAlerts(source);
                return definition with
                {
                    LimitUsd = ImportChecks.Amount(confirmation?.LimitUsd ?? source.BudgetAmount, $"budgets.{source.Id}.limitUsd"),
                    ConsumedUsd = ImportChecks.Amount(confirmation?.ConsumedUsd ?? source.ConsumedAmount, $"budgets.{source.Id}.consumedUsd"),
                    Enforcement = Enforcement(source, confirmation)
                };
            }).ToArray();

        diagnostics.Add(new("info", "selected-user-snapshot", "selectedUser",
            "All reconciled shared seats are retained; ULB consumption and applicable controls belong to this selected user. Export again before changing user context."));
        if (overrides.PaidUsage is null || overrides.PaidUsage.State == GuardrailValue.Unknown)
        {
            diagnostics.Add(new("warning", "paid-usage-unknown", "paidUsage",
                "Paid usage was not verified. Unknown is preserved and may produce an indeterminate preview."));
        }
        return new EconomicGuardrailSnapshot
        {
            EnterprisePoolConsumedCredits = consumed,
            IncludedUsageControls = controls,
            UserLevelBudgets = userBudgets,
            SpendingBudgets = spending,
            EnterpriseBudgetExcludedCostCenterIds = excluded,
            PaidUsage = overrides.PaidUsage ?? new PaidUsageAuthorization()
        };
    }

    public ActionsGuardrailSnapshot MapActions(ActionsConfirmation input)
    {
        ImportChecks.Identifier(input.Account, "workload.actions.account");
        ImportChecks.Require(input.ApplicableBudgetsConfirmed, "actions-budgets-unconfirmed",
            "Confirm the applicable Actions budget IDs separately from the AI-credit payer.");
        ImportChecks.Items(input.ApplicableBudgetIds, "workload.actions.applicableBudgetIds");
        ImportChecks.Unique(input.ApplicableBudgetIds, "workload.actions.applicableBudgetIds");
        var budgets = input.ApplicableBudgetIds.Order(StringComparer.Ordinal).Select(id =>
        {
            var source = snapshot.Budgets.SingleOrDefault(budget => ImportChecks.Same(budget.Id, id));
            ImportChecks.Require(source is not null && IsActionsBudget(source), "actions-budget-unsupported",
                $"Actions budget '{id}' is missing or is not a supported Actions product/standard Linux SKU budget.");
            var confirmation = ImportChecks.Find(overrides.Budgets, source!.Id);
            _usedBudgets.Add(source.Id);
            ImportChecks.Require(Expiration(source) is null || Expiration(source) > _timestamp,
                "actions-budget-expired", $"Actions budget '{id}' is expired and cannot be confirmed as applicable.");
            ImportChecks.Require(confirmation?.TrackingStartedAt is null || confirmation.TrackingStartedAt <= _timestamp,
                "actions-budget-not-started", $"Actions budget '{id}' tracking has not started at capture time.");
            WarnAlerts(source);
            return new ActionsSpendingBudget
            {
                Id = source.Id,
                LimitUsd = ImportChecks.Amount(confirmation?.LimitUsd ?? source.BudgetAmount, $"budgets.{id}.limitUsd"),
                ConsumedUsd = ImportChecks.Amount(confirmation?.ConsumedUsd ?? source.ConsumedAmount, $"budgets.{id}.consumedUsd"),
                Enforcement = Enforcement(source, confirmation)
            };
        }).ToArray();
        return new ActionsGuardrailSnapshot
        {
            IncludedMinutes = ImportChecks.Amount(input.IncludedMinutesRemaining, "workload.actions.includedMinutesRemaining"),
            Budgets = budgets,
            ActionsEnabled = input.ActionsEnabled, RunnerAvailable = input.RunnerAvailable,
            WorkflowApproved = input.WorkflowApproved, RepositoryRulesPermitRun = input.RepositoryRulesPermitRun
        };
    }

    private string CostCenterId(string? name)
    {
        ImportChecks.Identifier(name, "budget.entityName");
        var matches = snapshot.CostCenters.Where(center => ImportChecks.Same(center.Id, name) || ImportChecks.Same(center.Name, name)).ToArray();
        ImportChecks.Require(matches.Length == 1, "budget-cost-center-unresolved", $"Budget target '{name}' is not an unambiguous observed cost center.");
        return matches[0].Id;
    }

    public void ReportUnusedConfirmations()
    {
        foreach (var id in overrides.Budgets.Keys.Where(id => !_usedBudgets.Contains(id)).Order(StringComparer.Ordinal))
            diagnostics.Add(new("warning", "confirmation-not-applied", $"overrides.budgets.{id}", "This observed budget does not apply to the selected workload/account and was not placed in the bundle."));
        foreach (var id in overrides.IncludedControls.Keys.Where(id => !_usedControls.Contains(id)).Order(StringComparer.Ordinal))
            diagnostics.Add(new("warning", "confirmation-not-applied", $"overrides.includedControls.{id}", "This included-control confirmation is outside the selected user's effective cost center."));
    }

    private static DateTimeOffset? Expiration(BudgetObservation budget) => budget.ExpiresAt is { } expiration
        ? new DateTimeOffset(expiration.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

    private static GuardrailEnforcement Enforcement(BudgetObservation source, BudgetConfirmation? confirmation)
    {
        if (confirmation?.Enforcement is { } enforcement) return enforcement;
        ImportChecks.Require(source.PreventFurtherUsage is not null, "budget-enforcement-unconfirmed",
            $"Confirm budgets.{source.Id}.enforcement; missing preventFurtherUsage is not alert-only.");
        return source.PreventFurtherUsage == true ? GuardrailEnforcement.HardStop : GuardrailEnforcement.AlertOnly;
    }

    private void WarnAlerts(BudgetObservation budget)
    {
        if (budget.WillAlert != true)
        {
            diagnostics.Add(new("warning", "alert-preference-not-modeled", $"budgets.{budget.Id}",
                "The Engine reports modeled threshold crossings; this does not imply GitHub email alerting is enabled."));
        }
    }

    private static bool IsActionsBudget(BudgetObservation budget) =>
        (ImportChecks.Same(budget.BudgetType, "ProductPricing") && ImportChecks.Same(budget.ProductSku, "actions")) ||
        (ImportChecks.Same(budget.BudgetType, "SkuPricing") && ImportChecks.Same(budget.ProductSku, "actions_linux"));

    private bool IsAiBudget(BudgetObservation budget)
    {
        ImportChecks.Identifier(budget.Scope, $"budgets.{budget.Id}.scope");
        ImportChecks.Identifier(budget.BudgetType, $"budgets.{budget.Id}.budgetType");
        ImportChecks.Identifier(budget.ProductSku, $"budgets.{budget.Id}.productSku");
        if (ImportChecks.Same(budget.BudgetType, "BundlePricing") && ImportChecks.Same(budget.ProductSku, "ai_credits")) return true;
        if (ImportChecks.Same(budget.BudgetType, "ProductPricing") && budget.ProductSku.ToLowerInvariant() is "copilot" or "github-copilot") return true;
        if (ImportChecks.Same(budget.BudgetType, "SkuPricing") && budget.ProductSku.ToLowerInvariant() is "copilot_ai_credits" or "copilot-ai-credits") return true;
        if (IsActionsBudget(budget) || (ImportChecks.Same(budget.BudgetType, "ProductPricing") &&
            budget.ProductSku.ToLowerInvariant() is "codespaces" or "packages" or "git_lfs" or "advanced_security" or "code_security" or "secret_protection" or "github_enterprise"))
        {
            diagnostics.Add(new("info", "non-ai-budget", $"budgets.{budget.Id}", "This budget is not an AI-credit constraint. Actions budgets are selected from the supplied account confirmation."));
            return false;
        }
        throw new ImportException("budget-product-unsupported",
            $"Budget '{budget.Id}' uses unrecognized type/product '{budget.BudgetType}/{budget.ProductSku}'. Its relevance cannot be guessed.");
    }
}