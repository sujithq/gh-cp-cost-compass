using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Reference;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine;

public sealed class CopilotUsageSimulationEngine : ICopilotUsageSimulationEngine
{
    private const decimal Million = 1_000_000m;
    private readonly EngineConfiguration _configuration;
    private readonly EconomicBalanceCalculator _balances;
    private readonly EconomicGuardrailEvaluator _economicEvaluator;
    private readonly ModelEligibilityEvaluator _pricingEvaluator;

    public CopilotUsageSimulationEngine(EngineConfiguration configuration)
    {
        EngineConfigurationValidator.Validate(configuration);
        _configuration = configuration;
        _balances = new EconomicBalanceCalculator(configuration);
        _economicEvaluator = new EconomicGuardrailEvaluator(configuration, _balances);
        _pricingEvaluator = new ModelEligibilityEvaluator(configuration);
    }

    public EngineConfiguration Configuration => _configuration;

    public SimulationResult Simulate(SimulationScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        SimulationScenarioValidator.Validate(scenario);

        var context = new SimulationPipelineContext(scenario, _balances, _configuration);
        var explanation = context.Explanation;
        var operation = Find(_configuration.Operations, scenario.OperationId, x => x.Id, "operation");
        _ = Find(_configuration.Plans, scenario.PlanId, x => x.Id, "plan");
        var costChecksOnly = scenario.CheckScope == SimulationCheckScope.CostRelatedOnly;
        var selectedPlanId = scenario.PlanId;

        if (operation.IsBilled)
        {
            if (scenario.BillingContext is null || scenario.Attribution is null ||
                scenario.EconomicGuardrails is null)
            {
                throw new SimulationException(
                    "Billed operations require BillingContext, Attribution, and EconomicGuardrails.",
                    "economic-context-required");
            }

            context.Attribution = new AttributionResolver().Resolve(
                scenario.Attribution,
                scenario.Timestamp);
            explanation.Add(Entry(
                "attribution",
                context.Attribution.Rule.ToString(),
                context.Attribution.Explanation));
            context.Trace.Record("attribution", "attribution",
                context.Attribution.Outcome == GuardrailOutcome.Indeterminate
                    ? SimulationTraceState.Indeterminate
                    : SimulationTraceState.Passed,
                context.Attribution.Explanation, context.Attribution.UserId,
                "Billing attribution");
            if (context.Attribution.Outcome == GuardrailOutcome.Indeterminate)
            {
                return context.Complete(SimulationDecision.Indeterminate, "attribution");
            }

            var selectedPlanSeat = _balances.ResolveSelectedPlanSeat(
                scenario,
                context.Attribution);
            if (selectedPlanSeat.Status == SelectedPlanSeatStatus.Conflicting)
            {
                throw new SimulationException(
                    $"Scenario plan '{scenario.PlanId}' does not match the effective seat plan '{selectedPlanSeat.Seat!.PlanId}' for user '{context.Attribution.UserId}'.",
                    SimulationScenarioValidator.InvalidContractCode);
            }

            if (selectedPlanSeat.Status is SelectedPlanSeatStatus.Missing or SelectedPlanSeatStatus.Ambiguous)
            {
                var missing = selectedPlanSeat.Status == SelectedPlanSeatStatus.Missing;
                var guardrailId = missing ? "seat-assignment.missing" : "seat-assignment.ambiguous";
                var message = missing
                    ? $"No effective seat assignment exists for user '{context.Attribution.UserId}' at the simulation timestamp."
                    : $"Multiple effective seat assignments exist for user '{context.Attribution.UserId}' at the simulation timestamp.";
                context.Remaining = _balances.CreateUnchangedRemaining(
                    scenario,
                    context.Attribution);
                explanation.Add(Entry("guardrail", guardrailId, message));
                context.Trace.Record("seat-assignment", guardrailId,
                    SimulationTraceState.Indeterminate, message, context.Attribution.UserId);
                return context.Complete(SimulationDecision.Indeterminate, guardrailId);
            }

            selectedPlanId = selectedPlanSeat.Seat!.PlanId;
            context.Trace.Record("seat-assignment", "seat-assignment", SimulationTraceState.Passed,
                "One effective seat was selected for the billed user; pooled allowances are checked separately.",
                selectedPlanSeat.Seat!.UserId, "Selected user's effective seat");
            if (scenario.Timestamp >= scenario.BillingContext.CycleStart &&
                scenario.Timestamp < scenario.BillingContext.CycleEnd)
            {
                var inventoryFailure = _balances.FindSeatInventoryFailure(
                    scenario,
                    context.Attribution);
                if (inventoryFailure is not null)
                {
                    context.Remaining = _balances.CreateUnchangedRemaining(
                        scenario,
                        context.Attribution);
                    explanation.Add(Entry(
                        "guardrail",
                        inventoryFailure.Value.GuardrailId,
                        inventoryFailure.Value.Message));
                    context.Trace.Record("seat-inventory", inventoryFailure.Value.GuardrailId,
                        SimulationTraceState.Indeterminate, inventoryFailure.Value.Message,
                        scenario.BillingContext.BillingEntityId);
                    return context.Complete(
                        SimulationDecision.Indeterminate,
                        inventoryFailure.Value.GuardrailId);
                }

                context.Trace.Record("seat-inventory", "seat-inventory", SimulationTraceState.Passed,
                    "The effective pooled seat inventory has known allowances.",
                    scenario.BillingContext.BillingEntityId, "Pooled seat inventory");
            }
            else
            {
                context.Trace.NotApplicable("seat-inventory",
                    "This precheck is deferred because the timestamp is outside the supplied billing cycle.");
            }
        }
        else
        {
            foreach (var stage in new[] { "attribution", "seat-assignment", "seat-inventory" })
            {
                context.Trace.NotApplicable(stage, "Unbilled operations do not require economic attribution.");
            }
        }

        var runtimeEvaluator = new RuntimeGuardrailEvaluator();
        if (!costChecksOnly && operation.IsBilled)
        {
            var runtimePreflight = runtimeEvaluator.EvaluateBeforeCalls(scenario);
            context.AppliedGuardrails.AddRange(runtimePreflight.AppliedGuardrails);
            context.Trace.Guardrails("runtime-preflight", runtimePreflight.AppliedGuardrails,
                runtimePreflight.Decision, context.Attribution);
            if (runtimePreflight.Decision != SimulationDecision.Allowed)
            {
                return context.Complete(
                    runtimePreflight.Decision,
                    runtimePreflight.FailingGuardrailId);
            }
        }
        else if (!operation.IsBilled)
        {
            context.Trace.NotApplicable("runtime-preflight", "Runtime controls do not apply to unbilled operations.");
        }

        var requiresActions = operation.ActionsMetering != ActionsMeteringMode.None;
        var actionsEvaluator = new ActionsGuardrailEvaluator();
        if (!costChecksOnly && requiresActions && scenario.ActionsGuardrails is not null)
        {
            var actionsAccess = actionsEvaluator.EvaluateAccess(scenario.ActionsGuardrails);
            context.AppliedGuardrails.AddRange(actionsAccess.AppliedGuardrails);
            context.Trace.Guardrails("actions-access", actionsAccess.AppliedGuardrails,
                actionsAccess.Decision, context.Attribution);
            if (actionsAccess.Decision != SimulationDecision.Allowed)
            {
                context.Alerts.AddRange(actionsAccess.Alerts);
                return context.Complete(
                    actionsAccess.Decision,
                    actionsAccess.FailingGuardrailId);
            }
        }
        else if (!costChecksOnly)
        {
            context.Trace.NotApplicable("actions-access",
                requiresActions ? "No Actions access snapshot was supplied." : "This operation has no Actions meter.");
        }

        var gateFailure = costChecksOnly ? null : EvaluateGates(operation, scenario, explanation, context.Trace);
        if (gateFailure is not null)
        {
            return context.Complete(
                SimulationDecision.Blocked,
                gateFailure.Value.GateId);
        }

        if (!operation.IsBilled)
        {
            context.CostRequirement = new SimulationCostRequirement
            {
                AiCredits = 0m,
                ModelUsd = 0m,
                IncludedCredits = 0m,
                MeteredCredits = 0m,
                AiUsd = 0m,
                ActionsUsd = requiresActions ? null : 0m
            };
            foreach (var stage in new[]
            {
                "pricing", "runtime-credits", "actions-pricing", "billing-cycle", "pool-entitlement",
                "user-level-budget", "included-usage-control", "included-pool",
                "paid-usage-authorization", "metered-spending-budget", "actions-budgets"
            })
            {
                context.Trace.NotApplicable(stage, "The unbilled operation ends before usage allocation.");
            }

            explanation.Add(Entry("usage", "unbilled-operation", $"Operation '{operation.Id}' does not consume AI credits."));
            return context.Complete(SimulationDecision.Allowed);
        }

        if (scenario.Calls.Count == 0)
        {
            context.Assumptions.Add("No model calls were supplied, so token cost could not be calculated.");
            explanation.Add(Entry("usage", "missing-calls", "The billed operation has no model-call inputs."));
            context.Trace.Record("pricing", "missing-calls", SimulationTraceState.Indeterminate,
                "No model-call inputs were supplied; cost is unknown, not zero.", label: "Model-call pricing");
            return context.Complete(SimulationDecision.PartiallySimulated);
        }

        context.Calls = scenario.Calls
            .Select((call, index) =>
            {
                var charge = CalculateCall(operation, selectedPlanId, scenario.Timestamp, call, index + 1, explanation);
                context.Trace.Record("pricing", $"pricing.call-{index + 1}", SimulationTraceState.Passed,
                    $"Effective price tier '{charge.PriceTierId}' priced this model call; this is required usage, not accepted consumption.",
                    charge.ModelId, $"Model call {index + 1}", charge.Credits);
                return charge;
            })
            .ToArray();
        var totalCredits = context.Calls.Sum(x => x.Credits);
        context.CostRequirement = context.CostRequirement with
        {
            AiCredits = totalCredits,
            ModelUsd = context.Calls.Sum(call => call.AdjustedUsd)
        };

        context.Assumptions.Add("Fractional AI credits are retained because GitHub does not document billing rounding.");
        if (!costChecksOnly)
        {
            var runtimeCredits = runtimeEvaluator.EvaluateCredits(scenario.RuntimeGuardrails, totalCredits);
            context.AppliedGuardrails.AddRange(runtimeCredits.AppliedGuardrails);
            context.Trace.Guardrails("runtime-credits", runtimeCredits.AppliedGuardrails,
                runtimeCredits.Decision, context.Attribution);
            if (runtimeCredits.Decision == SimulationDecision.SoftStopped)
            {
                return context.Complete(
                    SimulationDecision.SoftStopped,
                    runtimeCredits.FailingGuardrailId);
            }
        }

        context.ActionsUsage = CalculateActions(operation, scenario, explanation);
        context.CostRequirement = context.CostRequirement with
        {
            ActionsUsd = context.ActionsUsage?.AdditionalUsd ?? 0m
        };
        if (context.ActionsUsage is { } actionsUsage)
        {
            context.Trace.Record("actions-pricing", "actions-pricing", SimulationTraceState.Passed,
                "The supplied minutes and separate Actions allowance determine the required runner charge; job-level rounding is not performed here.",
                actionsUsage.RunnerId, "Actions runner pricing", actionsUsage.AdditionalUsd);
        }
        else
        {
            context.Trace.NotApplicable("actions-pricing", "The operation and repository visibility do not require Actions metering.");
        }

        var economicResult = _economicEvaluator.Evaluate(
            scenario,
            context.Attribution!,
            totalCredits);
        context.AppliedGuardrails.AddRange(economicResult.AppliedGuardrails);
        context.Trace.Economics(economicResult, context.Attribution!);
        context.EffectiveUlb = economicResult.EffectiveUlb;
        if (economicResult.RequiredAllocation is { } requiredAllocation)
        {
            context.CostRequirement = context.CostRequirement with
            {
                IncludedCredits = requiredAllocation.IncludedCredits,
                MeteredCredits = requiredAllocation.MeteredCredits,
                AiUsd = requiredAllocation.MeteredUsd
            };
        }
        if (economicResult.Message is not null)
        {
            explanation.Add(Entry("guardrail", economicResult.FailingGuardrailId ?? "indeterminate", economicResult.Message));
        }

        if (economicResult.Decision != SimulationDecision.Allowed)
        {
            context.Alerts.AddRange(economicResult.Alerts);
            context.Allocation = economicResult.Allocation;
            context.EffectiveUlb = economicResult.EffectiveUlb;
            context.Remaining = economicResult.Remaining;
            return context.Complete(
                economicResult.Decision,
                economicResult.FailingGuardrailId);
        }

        if (context.ActionsUsage is not null && scenario.ActionsGuardrails is not null)
        {
            var actionsBudget = actionsEvaluator.EvaluateBudgets(
                scenario.ActionsGuardrails,
                context.ActionsUsage);
            context.AppliedGuardrails.AddRange(actionsBudget.AppliedGuardrails);
            context.Trace.Guardrails("actions-budgets", actionsBudget.AppliedGuardrails,
                actionsBudget.Decision, context.Attribution);
            context.Alerts.AddRange(actionsBudget.Alerts);
            if (actionsBudget.Decision != SimulationDecision.Allowed)
            {
                return context.Complete(
                    actionsBudget.Decision,
                    actionsBudget.FailingGuardrailId);
            }
        }
        else
        {
            context.Trace.NotApplicable("actions-budgets",
                "No Actions charge or Actions budget snapshot applies.");
        }

        context.Alerts.InsertRange(0, economicResult.Alerts);
        context.Allocation = economicResult.Allocation;
        context.EffectiveUlb = economicResult.EffectiveUlb;
        context.Remaining = _balances.ApplyActionsUsage(
            economicResult.Remaining,
            scenario,
            context.ActionsUsage);

        explanation.Add(Entry("result", "allowed", "All access and budget checks passed."));
        return context.Complete(SimulationDecision.Allowed);
    }

    private GateFailure? EvaluateGates(
        OperationDefinition operation,
        SimulationScenario scenario,
        List<ExplanationEntry> explanation,
        SimulationTraceRecorder trace)
    {
        foreach (var gate in _configuration.Gates.OrderBy(x => x.Sequence))
        {
            if (gate.ApplicableOperationIds.Count > 0 &&
                !gate.ApplicableOperationIds.Contains(operation.Id, StringComparer.OrdinalIgnoreCase))
            {
                explanation.Add(Entry("access", "gate-not-applicable", $"Gate '{gate.Id}' does not apply."));
                trace.Record("access", gate.Id, SimulationTraceState.NotApplicable,
                    $"The catalog excludes operation '{operation.Id}' from this gate.", operation.Id);
                continue;
            }

            var supplied = TryGet(scenario.AccessGates, gate.Id, out var state);
            var passed = supplied ? state!.Passed : gate.PassWhenUnspecified;
            if (!passed)
            {
                var reason = state?.Reason ?? $"Gate '{gate.Id}' did not pass.";
                var remediation = string.IsNullOrWhiteSpace(state?.Remediation)
                    ? string.Empty
                    : $" Remediation: {state.Remediation}";
                explanation.Add(Entry("access", gate.Id, reason + remediation));
                trace.Record("access", gate.Id, SimulationTraceState.Blocked, reason + remediation, operation.Id);
                return new GateFailure(gate.Id);
            }

            explanation.Add(Entry("access", "gate-passed", $"Gate '{gate.Id}' passed."));
            trace.Record("access", gate.Id, SimulationTraceState.Passed,
                supplied
                    ? state!.Reason ?? "The supplied scenario access state passed."
                    : "No access state was supplied; the configured PassWhenUnspecified assumption was used.",
                operation.Id);
        }

        return null;
    }

    private ModelCallCharge CalculateCall(
        OperationDefinition operation,
        string selectedPlanId,
        DateTimeOffset timestamp,
        ModelCallInput call,
        int index,
        List<ExplanationEntry> explanation)
    {
        var model = Find(_configuration.Models, call.ModelId, x => x.Id, "model");
        // Model evidence is enforced here; legacy configured modifiers remain independent of Compass qualification.
        var pricing = _pricingEvaluator.Evaluate(
            model.Id, selectedPlanId, operation.Id, timestamp, call with { EnabledMultiplierIds = [] });
        if (pricing.Status == ModelEligibilityStatus.Unsupported &&
            pricing.ReasonCode == ModelEligibilityReasonCodes.Unverified)
        {
            pricing = _pricingEvaluator.EvaluatePricing(model.Id, timestamp, call);
        }

        if (pricing.Status != ModelEligibilityStatus.Available)
        {
            throw new SimulationException(pricing.Message, pricing.ReasonCode);
        }

        var period = pricing.PricePeriod
            ?? throw new InvalidOperationException("Available pricing must include the selected effective period.");
        var tier = pricing.PriceTier
            ?? throw new InvalidOperationException("Available pricing must include the selected context tier.");

        var freshInputUsd = call.FreshInputTokens * tier.InputUsdPerMillion / Million;
        var cachedInputUsd = call.CachedInputTokens * tier.CachedInputUsdPerMillion / Million;
        var cacheWriteUsd = call.CacheWriteTokens * tier.CacheWriteUsdPerMillion / Million;
        var outputUsd = call.OutputTokens * tier.OutputUsdPerMillion / Million;
        var rawUsd = freshInputUsd + cachedInputUsd + cacheWriteUsd + outputUsd;
        var adjustedUsd = rawUsd;
        var multipliers = new List<AppliedMultiplier>();

        foreach (var multiplierId in call.EnabledMultiplierIds)
        {
            var multiplier = Find(_configuration.Multipliers, multiplierId, x => x.Id, "multiplier");
            if (!Applies(multiplier.ApplicableOperationIds, operation.Id) ||
                !Applies(multiplier.ApplicableModelIds, model.Id) ||
                (multiplier.ApplicablePlanIds is { } plans &&
                 !plans.Contains(selectedPlanId, StringComparer.OrdinalIgnoreCase)))
            {
                throw new SimulationException(
                    $"Multiplier '{multiplier.Id}' does not apply to plan '{selectedPlanId}', operation '{operation.Id}', and model '{model.Id}'.",
                    "multiplier-not-applicable");
            }

            adjustedUsd *= multiplier.Factor;
            multipliers.Add(new AppliedMultiplier { Id = multiplier.Id, Factor = multiplier.Factor });
        }

        var credits = adjustedUsd / _configuration.UsdPerCredit;
        explanation.Add(Entry(
            "pricing",
            "model-call-priced",
            $"Call {index} used model '{model.Id}', tier '{tier.Id}', and consumed {credits:G29} credits."));

        return new ModelCallCharge
        {
            CallIndex = index,
            ModelId = model.Id,
            PriceTierId = tier.Id,
            Pricing = new ModelCallPricingEvidence
            {
                PriceTierId = tier.Id,
                EffectiveFrom = period.EffectiveFrom,
                EffectiveTo = period.EffectiveTo,
                MinimumContextTokensExclusive = tier.MinimumContextTokensExclusive,
                MaximumContextTokensInclusive = tier.MaximumContextTokensInclusive,
                InputUsdPerMillion = SupportedRate(TokenComponent.FreshInput, tier.InputUsdPerMillion),
                CachedInputUsdPerMillion = SupportedRate(TokenComponent.CachedInput, tier.CachedInputUsdPerMillion),
                CacheWriteUsdPerMillion = SupportedRate(TokenComponent.CacheWrite, tier.CacheWriteUsdPerMillion),
                OutputUsdPerMillion = SupportedRate(TokenComponent.Output, tier.OutputUsdPerMillion),
                SourceIds = (period.SourceIds ?? model.SourceIds ?? []).ToArray()
            },
            FreshInputUsd = freshInputUsd,
            CachedInputUsd = cachedInputUsd,
            CacheWriteUsd = cacheWriteUsd,
            OutputUsd = outputUsd,
            RawUsd = rawUsd,
            AdjustedUsd = adjustedUsd,
            Credits = credits,
            AppliedMultipliers = multipliers
        };

        decimal? SupportedRate(TokenComponent component, decimal rate) =>
            model.SupportedTokenComponents is { } supported && !supported.Contains(component) ? null : rate;
    }

    private ActionsUsageResult? CalculateActions(
        OperationDefinition operation,
        SimulationScenario scenario,
        List<ExplanationEntry> explanation)
    {
        var shouldMeter = operation.ActionsMetering switch
        {
            ActionsMeteringMode.None => false,
            ActionsMeteringMode.Always => true,
            ActionsMeteringMode.PrivateRepositories => scenario.RepositoryVisibility != RepositoryVisibility.Public,
            _ => throw new SimulationException("Unknown Actions metering mode.", "actions-mode-invalid")
        };

        if (!shouldMeter)
        {
            return null;
        }

        var usage = scenario.ActionsUsage
            ?? throw new SimulationException(
                $"Operation '{operation.Id}' requires Actions usage input.",
                "actions-usage-required");
        var runner = Find(_configuration.ActionsRunners, usage.RunnerId, x => x.Id, "Actions runner");
        var includedRemaining = scenario.ActionsGuardrails is null
            ? usage.IncludedMinutesRemaining
            : Math.Max(
                0m,
                scenario.ActionsGuardrails.IncludedMinutes -
                scenario.ActionsGuardrails.ConsumedIncludedMinutes);
        var includedMinutes = Math.Min(usage.Minutes, includedRemaining);
        var billableMinutes = usage.Minutes - includedMinutes;
        var result = new ActionsUsageResult
        {
            RunnerId = runner.Id,
            TotalMinutes = usage.Minutes,
            IncludedMinutes = includedMinutes,
            BillableMinutes = billableMinutes,
            AdditionalUsd = billableMinutes * runner.UsdPerMinute
        };
        explanation.Add(Entry("actions", "actions-priced", $"Actions usage adds {result.AdditionalUsd:C} in runner charges."));
        return result;
    }

    private static bool Applies(IReadOnlySet<string> configuredIds, string actualId) =>
        configuredIds.Count == 0 || configuredIds.Contains(actualId, StringComparer.OrdinalIgnoreCase);

    private static T Find<T>(
        IEnumerable<T> values,
        string id,
        Func<T, string> idSelector,
        string label) =>
        values.SingleOrDefault(x => string.Equals(idSelector(x), id, StringComparison.OrdinalIgnoreCase))
        ?? throw new SimulationException($"Unknown {label} '{id}'.", $"{label.Replace(' ', '-')}-not-found");

    private static bool TryGet<T>(
        IReadOnlyDictionary<string, T> dictionary,
        string key,
        out T? value)
    {
        var match = dictionary.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        value = match.Value;
        return match.Key is not null;
    }

    private static ExplanationEntry Entry(string stage, string code, string message) =>
        new() { Stage = stage, Code = code, Message = message };

    private readonly record struct GateFailure(string GateId);
}

public sealed class SimulationException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}
