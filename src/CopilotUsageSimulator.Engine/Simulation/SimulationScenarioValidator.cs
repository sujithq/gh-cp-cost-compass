using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.Engine.Simulation;

public static class SimulationScenarioValidator
{
    public const string InvalidContractCode = "scenario-contract-invalid";

    public static void Validate(SimulationScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        RequireIdentifier(scenario.OperationId, "operationId");
        RequireIdentifier(scenario.PlanId, "planId");
        RequireIdentifier(scenario.ProductId, "productId");
        RequireIdentifier(scenario.SkuId, "skuId");
        RequireDefined(scenario.CheckScope, "checkScope");
        RequireDefined(scenario.RepositoryVisibility, "repositoryVisibility");
        Require(scenario.Calls, "calls");
        Require(scenario.AccessGates, "accessGates");
        Require(scenario.Metadata, "metadata");
        ValidateIdentifiers(scenario.AccessGates.Keys, "accessGates");

        var gateIndex = 0;
        foreach (var gate in scenario.AccessGates)
        {
            Require(gate.Value, $"accessGates[{gateIndex}]");
            gateIndex++;
        }

        for (var index = 0; index < scenario.Calls.Count; index++)
        {
            ValidateCall(Require(scenario.Calls[index], $"calls[{index}]"), index);
        }

        if (scenario.ActionsUsage is not null)
        {
            RequireIdentifier(scenario.ActionsUsage.RunnerId, "actionsUsage.runnerId");
            RequireNonNegative(scenario.ActionsUsage.Minutes, "actionsUsage.minutes");
            RequireNonNegative(
                scenario.ActionsUsage.IncludedMinutesRemaining,
                "actionsUsage.includedMinutesRemaining");
        }

        if (scenario.BillingContext is not null)
        {
            ValidateBillingContext(scenario.BillingContext);
        }

        if (scenario.Attribution is not null)
        {
            ValidateAttribution(scenario.Attribution);
        }

        if (scenario.EconomicGuardrails is not null)
        {
            ValidateEconomicGuardrails(scenario.EconomicGuardrails);
        }

        if (scenario.RuntimeGuardrails is not null)
        {
            ValidateRuntimeGuardrails(scenario.RuntimeGuardrails);
        }

        if (scenario.ActionsGuardrails is not null)
        {
            ValidateActionsGuardrails(scenario.ActionsGuardrails);
        }
    }

    private static void ValidateCall(ModelCallInput call, int index)
    {
        var path = $"calls[{index}]";
        RequireIdentifier(call.ModelId, $"{path}.modelId");
        RequireNonNegative(call.ContextTokens, $"{path}.contextTokens");
        RequireNonNegative(call.FreshInputTokens, $"{path}.freshInputTokens");
        RequireNonNegative(call.CachedInputTokens, $"{path}.cachedInputTokens");
        RequireNonNegative(call.CacheWriteTokens, $"{path}.cacheWriteTokens");
        RequireNonNegative(call.OutputTokens, $"{path}.outputTokens");
        Require(call.EnabledMultiplierIds, $"{path}.enabledMultiplierIds");
        Require(call.Metadata, $"{path}.metadata");
        ValidateIdentifiers(call.EnabledMultiplierIds, $"{path}.enabledMultiplierIds");
        EnsureUnique(call.EnabledMultiplierIds, $"{path}.enabledMultiplierIds");
    }

    private static void ValidateBillingContext(BillingContext context)
    {
        RequireIdentifier(context.BillingEntityId, "billingContext.billingEntityId");
        if (context.CycleEnd <= context.CycleStart)
        {
            Invalid("billingContext cycleEnd must be later than cycleStart.");
        }

        Require(context.SeatAssignments, "billingContext.seatAssignments");
        for (var index = 0; index < context.SeatAssignments.Count; index++)
        {
            var path = $"billingContext.seatAssignments[{index}]";
            var assignment = Require(context.SeatAssignments[index], path);
            RequireIdentifier(assignment.UserId, $"{path}.userId");
            RequireIdentifier(assignment.PlanId, $"{path}.planId");
            RequireOptionalIdentifier(assignment.CostCenterId, $"{path}.costCenterId");
            RequireEffectivePeriod(assignment.EffectiveFrom, assignment.EffectiveTo, path);
        }

        foreach (var userAssignments in context.SeatAssignments.GroupBy(
                     assignment => assignment.UserId,
                     StringComparer.OrdinalIgnoreCase))
        {
            var orderedAssignments = userAssignments
                .OrderBy(assignment => assignment.EffectiveFrom)
                .ToArray();
            for (var index = 1; index < orderedAssignments.Length; index++)
            {
                if (orderedAssignments[index - 1].EffectiveTo is null ||
                    orderedAssignments[index].EffectiveFrom < orderedAssignments[index - 1].EffectiveTo)
                {
                    Invalid(
                        $"billingContext.seatAssignments has overlapping effective periods for user '{userAssignments.Key}'.");
                }
            }
        }
    }

    private static void ValidateAttribution(AttributionInput attribution)
    {
        RequireIdentifier(attribution.UserId, "attribution.userId");
        RequireOptionalIdentifier(
            attribution.CycleSelectedLicensingOrganizationId,
            "attribution.cycleSelectedLicensingOrganizationId");
        Require(attribution.DirectAssignments, "attribution.directAssignments");
        Require(attribution.TeamAssignments, "attribution.teamAssignments");
        Require(attribution.LicensingOrganizationIds, "attribution.licensingOrganizationIds");
        Require(attribution.OrganizationAssignments, "attribution.organizationAssignments");
        ValidateIdentifiers(
            attribution.LicensingOrganizationIds,
            "attribution.licensingOrganizationIds");

        for (var index = 0; index < attribution.DirectAssignments.Count; index++)
        {
            var path = $"attribution.directAssignments[{index}]";
            var assignment = Require(attribution.DirectAssignments[index], path);
            RequireIdentifier(assignment.CostCenterId, $"{path}.costCenterId");
            RequireEffectivePeriod(assignment.EffectiveFrom, assignment.EffectiveTo, path);
        }

        for (var index = 0; index < attribution.TeamAssignments.Count; index++)
        {
            var path = $"attribution.teamAssignments[{index}]";
            var assignment = Require(attribution.TeamAssignments[index], path);
            RequireIdentifier(assignment.TeamId, $"{path}.teamId");
            RequireIdentifier(assignment.CostCenterId, $"{path}.costCenterId");
            RequireEffectivePeriod(assignment.EffectiveFrom, assignment.EffectiveTo, path);
        }

        for (var index = 0; index < attribution.OrganizationAssignments.Count; index++)
        {
            var path = $"attribution.organizationAssignments[{index}]";
            var assignment = Require(attribution.OrganizationAssignments[index], path);
            RequireIdentifier(assignment.OrganizationId, $"{path}.organizationId");
            RequireIdentifier(assignment.CostCenterId, $"{path}.costCenterId");
            RequireEffectivePeriod(assignment.EffectiveFrom, assignment.EffectiveTo, path);
        }
    }

    private static void ValidateEconomicGuardrails(EconomicGuardrailSnapshot guardrails)
    {
        RequireNonNegative(
            guardrails.EnterprisePoolConsumedCredits,
            "economicGuardrails.enterprisePoolConsumedCredits");
        Require(guardrails.UserLevelBudgets, "economicGuardrails.userLevelBudgets");
        Require(guardrails.IncludedUsageControls, "economicGuardrails.includedUsageControls");
        Require(guardrails.PaidUsage, "economicGuardrails.paidUsage");
        Require(guardrails.SpendingBudgets, "economicGuardrails.spendingBudgets");
        Require(
            guardrails.EnterpriseBudgetExcludedCostCenterIds,
            "economicGuardrails.enterpriseBudgetExcludedCostCenterIds");
        ValidateIdentifiers(
            guardrails.EnterpriseBudgetExcludedCostCenterIds,
            "economicGuardrails.enterpriseBudgetExcludedCostCenterIds");

        for (var index = 0; index < guardrails.UserLevelBudgets.Count; index++)
        {
            var path = $"economicGuardrails.userLevelBudgets[{index}]";
            var budget = Require(guardrails.UserLevelBudgets[index], path);
            RequireIdentifier(budget.Id, $"{path}.id");
            RequireDefined(budget.Kind, $"{path}.kind");
            RequireOptionalIdentifier(budget.TargetId, $"{path}.targetId");
            if (budget.Kind is not UserLevelBudgetKind.Universal &&
                string.IsNullOrWhiteSpace(budget.TargetId))
            {
                Invalid($"{path}.targetId is required for {budget.Kind} budgets.");
            }

            RequireNonNegative(budget.LimitCredits, $"{path}.limitCredits");
            RequireNonNegative(budget.ConsumedCredits, $"{path}.consumedCredits");
            RequireEffectivePeriod(budget.EffectiveFrom, budget.EffectiveTo, path);
        }
        EnsureUnique(
            guardrails.UserLevelBudgets.Select(budget => budget.Id),
            "economicGuardrails.userLevelBudgets");

        for (var index = 0; index < guardrails.IncludedUsageControls.Count; index++)
        {
            var path = $"economicGuardrails.includedUsageControls[{index}]";
            var control = Require(guardrails.IncludedUsageControls[index], path);
            RequireIdentifier(control.Id, $"{path}.id");
            RequireIdentifier(control.CostCenterId, $"{path}.costCenterId");
            RequireNonNegative(control.ConsumedCredits, $"{path}.consumedCredits");
            RequireDefined(control.OverflowBehavior, $"{path}.overflowBehavior");
            RequireEffectivePeriod(control.EffectiveFrom, control.EffectiveTo, path);
        }
        EnsureUnique(
            guardrails.IncludedUsageControls.Select(control => control.Id),
            "economicGuardrails.includedUsageControls");

        ValidatePaidUsage(guardrails.PaidUsage);

        for (var index = 0; index < guardrails.SpendingBudgets.Count; index++)
        {
            var path = $"economicGuardrails.spendingBudgets[{index}]";
            var budget = Require(guardrails.SpendingBudgets[index], path);
            RequireIdentifier(budget.Id, $"{path}.id");
            RequireDefined(budget.Scope, $"{path}.scope");
            RequireOptionalIdentifier(budget.ScopeId, $"{path}.scopeId");
            if (budget.Scope is not SpendingBudgetScope.Enterprise &&
                string.IsNullOrWhiteSpace(budget.ScopeId))
            {
                Invalid($"{path}.scopeId is required for {budget.Scope} budgets.");
            }

            RequireNonNegative(budget.LimitUsd, $"{path}.limitUsd");
            RequireNonNegative(budget.ConsumedUsd, $"{path}.consumedUsd");
            RequireDefined(budget.Enforcement, $"{path}.enforcement");
            Require(budget.ProductIds, $"{path}.productIds");
            Require(budget.SkuIds, $"{path}.skuIds");
            ValidateIdentifiers(budget.ProductIds, $"{path}.productIds");
            ValidateIdentifiers(budget.SkuIds, $"{path}.skuIds");
            RequireEffectivePeriod(budget.EffectiveFrom, budget.EffectiveTo, path);
        }
        EnsureUnique(
            guardrails.SpendingBudgets.Select(budget => budget.Id),
            "economicGuardrails.spendingBudgets");
    }

    private static void ValidatePaidUsage(PaidUsageAuthorization paidUsage)
    {
        const string path = "economicGuardrails.paidUsage";
        RequireDefined(paidUsage.State, $"{path}.state");
        Require(paidUsage.ProductIds, $"{path}.productIds");
        Require(paidUsage.SkuIds, $"{path}.skuIds");
        ValidateIdentifiers(paidUsage.ProductIds, $"{path}.productIds");
        ValidateIdentifiers(paidUsage.SkuIds, $"{path}.skuIds");
    }

    private static void ValidateRuntimeGuardrails(RuntimeGuardrailSnapshot guardrails)
    {
        RequireNonNegative(guardrails.MaximumModelCalls, "runtimeGuardrails.maximumModelCalls");
        RequireNonNegative(guardrails.ModelCallsConsumed, "runtimeGuardrails.modelCallsConsumed");
        RequireNonNegative(
            guardrails.MaximumSubagentDepth,
            "runtimeGuardrails.maximumSubagentDepth");
        RequireNonNegative(
            guardrails.RequestedSubagentDepth,
            "runtimeGuardrails.requestedSubagentDepth");
        RequireNonNegative(guardrails.MaximumDuration, "runtimeGuardrails.maximumDuration");
        RequireNonNegative(guardrails.ElapsedDuration, "runtimeGuardrails.elapsedDuration");
        RequireNonNegative(guardrails.RequestedDuration, "runtimeGuardrails.requestedDuration");
        RequireNonNegative(guardrails.CliSoftCreditLimit, "runtimeGuardrails.cliSoftCreditLimit");
        RequireNonNegative(
            guardrails.CliCreditsConsumed,
            "runtimeGuardrails.cliCreditsConsumed");
    }

    private static void ValidateActionsGuardrails(ActionsGuardrailSnapshot guardrails)
    {
        RequireDefined(guardrails.ActionsEnabled, "actionsGuardrails.actionsEnabled");
        RequireDefined(guardrails.RunnerAvailable, "actionsGuardrails.runnerAvailable");
        RequireDefined(guardrails.WorkflowApproved, "actionsGuardrails.workflowApproved");
        RequireDefined(
            guardrails.RepositoryRulesPermitRun,
            "actionsGuardrails.repositoryRulesPermitRun");
        RequireNonNegative(guardrails.IncludedMinutes, "actionsGuardrails.includedMinutes");
        RequireNonNegative(
            guardrails.ConsumedIncludedMinutes,
            "actionsGuardrails.consumedIncludedMinutes");
        Require(guardrails.Budgets, "actionsGuardrails.budgets");

        for (var index = 0; index < guardrails.Budgets.Count; index++)
        {
            var path = $"actionsGuardrails.budgets[{index}]";
            var budget = Require(guardrails.Budgets[index], path);
            RequireIdentifier(budget.Id, $"{path}.id");
            RequireNonNegative(budget.LimitUsd, $"{path}.limitUsd");
            RequireNonNegative(budget.ConsumedUsd, $"{path}.consumedUsd");
            RequireDefined(budget.Enforcement, $"{path}.enforcement");
        }
        EnsureUnique(
            guardrails.Budgets.Select(budget => budget.Id),
            "actionsGuardrails.budgets");
    }

    private static T Require<T>(T? value, string path) where T : class =>
        value ?? throw InvalidNull(path);

    private static void ValidateIdentifiers(IEnumerable<string> identifiers, string path)
    {
        var index = 0;
        foreach (var identifier in identifiers)
        {
            RequireIdentifier(identifier, $"{path}[{index}]");
            index++;
        }
    }

    private static void EnsureUnique(IEnumerable<string> identifiers, string path)
    {
        var duplicate = identifiers
            .GroupBy(identifier => identifier, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            Invalid($"{path} contains duplicate id '{duplicate.Key}'.");
        }
    }

    private static void RequireIdentifier(string? identifier, string path)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            Invalid($"{path} must be a non-empty identifier.");
        }
    }

    private static void RequireOptionalIdentifier(string? identifier, string path)
    {
        if (identifier is not null)
        {
            RequireIdentifier(identifier, path);
        }
    }

    private static void RequireNonNegative(long value, string path)
    {
        if (value < 0)
        {
            Invalid($"{path} must be nonnegative.");
        }
    }

    private static void RequireNonNegative(int value, string path) =>
        RequireNonNegative((long)value, path);

    private static void RequireNonNegative(int? value, string path)
    {
        if (value is not null)
        {
            RequireNonNegative(value.Value, path);
        }
    }

    private static void RequireNonNegative(decimal value, string path)
    {
        if (value < 0m)
        {
            Invalid($"{path} must be nonnegative.");
        }
    }

    private static void RequireNonNegative(decimal? value, string path)
    {
        if (value is not null)
        {
            RequireNonNegative(value.Value, path);
        }
    }

    private static void RequireNonNegative(TimeSpan value, string path)
    {
        if (value < TimeSpan.Zero)
        {
            Invalid($"{path} must be nonnegative.");
        }
    }

    private static void RequireNonNegative(TimeSpan? value, string path)
    {
        if (value is not null)
        {
            RequireNonNegative(value.Value, path);
        }
    }

    private static void RequireEffectivePeriod(
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string path)
    {
        if (effectiveTo <= effectiveFrom)
        {
            Invalid($"{path}.effectiveTo must be later than effectiveFrom.");
        }
    }

    private static void RequireDefined<TEnum>(TEnum value, string path)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            Invalid($"{path} has an unsupported value.");
        }
    }

    private static SimulationException InvalidNull(string path) =>
        new($"Scenario property '{path}' cannot be null.", InvalidContractCode);

    private static void Invalid(string message) =>
        throw new SimulationException(message, InvalidContractCode);
}
