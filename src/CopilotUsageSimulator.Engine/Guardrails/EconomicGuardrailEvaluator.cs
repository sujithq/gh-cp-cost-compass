using CopilotUsageSimulator.Common.Guardrails;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Guardrails;

public sealed class EconomicGuardrailEvaluator(
    EngineConfiguration configuration,
    EconomicBalanceCalculator balances)
{
    private static readonly decimal[] AlertThresholds = [75m, 90m, 100m];
    private readonly EconomicGuardrailApplicabilityResolver _applicability = new();

    public EconomicGuardrailEvaluation Evaluate(
        SimulationScenario scenario,
        AttributionResult attribution,
        decimal requestedCredits)
    {
        var billing = scenario.BillingContext
            ?? throw new SimulationException("BillingContext is required for rich guardrail evaluation.", "billing-context-required");
        var snapshot = scenario.EconomicGuardrails
            ?? throw new SimulationException("EconomicGuardrails is required for rich guardrail evaluation.", "economic-guardrails-required");
        var applied = new List<AppliedGuardrail>();
        var alerts = new List<ThresholdEvent>();

        if (scenario.Timestamp < billing.CycleStart || scenario.Timestamp >= billing.CycleEnd)
        {
            return EconomicGuardrailEvaluation.Stop(
                SimulationDecision.Indeterminate,
                "billing-cycle.timestamp",
                applied,
                alerts,
                balances.CreateUnchangedRemaining(scenario, attribution),
                message: "The simulation timestamp is outside the supplied billing cycle.");
        }

        applied.Add(Observation("billing-cycle.timestamp", "billing-cycle", GuardrailOutcome.Passed,
            "The timestamp is within the supplied billing cycle."));
        var poolEntitlement = balances.CalculatePoolEntitlement(billing, scenario.Timestamp);
        if (!poolEntitlement.IsKnown)
        {
            var failure = balances.FindSeatInventoryFailure(scenario, attribution)!.Value;
            return EconomicGuardrailEvaluation.Stop(
                SimulationDecision.Indeterminate,
                failure.GuardrailId,
                applied,
                alerts,
                balances.CreateUnchangedRemaining(scenario, attribution),
                message: failure.Message);
        }

        applied.Add(Observation("pool-entitlement", "pool-entitlement", GuardrailOutcome.Passed,
            "The active pooled seats have known effective allowances.") with
        {
            Limit = poolEntitlement.Credits,
            ConsumedBefore = snapshot.EnterprisePoolConsumedCredits
        });
        var poolRemaining = EconomicBalanceCalculator.Available(
            poolEntitlement.Credits,
            snapshot.EnterprisePoolConsumedCredits);
        var unchangedRemaining = balances.CreateUnchangedRemaining(scenario, attribution);
        var ulbResolution = _applicability.ResolveEffectiveUserLevelBudget(
            snapshot,
            attribution,
            scenario.Timestamp);
        if (ulbResolution.IsAmbiguous)
        {
            return EconomicGuardrailEvaluation.Stop(
                SimulationDecision.Indeterminate,
                "ulb.ambiguous",
                applied,
                alerts,
                unchangedRemaining,
                message: "Multiple effective ULBs of the same precedence apply.");
        }

        EffectiveUserLevelBudgetResult? effectiveUlb = null;
        if (ulbResolution.Value is not null)
        {
            var budget = ulbResolution.Value;
            var remaining = EconomicBalanceCalculator.Headroom(
                budget.LimitCredits,
                budget.ConsumedCredits);
            var blocked = requestedCredits > remaining;
            applied.Add(new AppliedGuardrail
            {
                Id = budget.Id,
                MetadataKey = budget.Kind switch
                {
                    UserLevelBudgetKind.Individual => GuardrailMetadataKeys.UlbIndividual,
                    UserLevelBudgetKind.CostCenter => GuardrailMetadataKeys.UlbCostCenter,
                    _ => GuardrailMetadataKeys.UlbUniversal
                },
                Category = GuardrailCategories.UserLevelBudget,
                Enforcement = GuardrailEnforcement.HardStop,
                Outcome = blocked ? GuardrailOutcome.Blocked : GuardrailOutcome.Passed,
                Limit = budget.LimitCredits,
                ConsumedBefore = budget.ConsumedCredits,
                Requested = requestedCredits,
                RemainingAfter = blocked ? remaining : remaining - requestedCredits,
                Message = blocked
                    ? "The effective user-level budget cannot cover the request."
                    : "The effective user-level budget provisionally reserved the request credits; rejected usage releases the reservation."
            });
            effectiveUlb = new EffectiveUserLevelBudgetResult
            {
                Id = budget.Id,
                Kind = budget.Kind,
                LimitCredits = budget.LimitCredits,
                ConsumedBeforeCredits = budget.ConsumedCredits,
                ReservedCredits = blocked ? 0m : requestedCredits,
                RemainingCredits = blocked ? remaining : remaining - requestedCredits
            };
            unchangedRemaining = unchangedRemaining with
            {
                EffectiveUserBudgetCredits = remaining
            };
            if (blocked)
            {
                return EconomicGuardrailEvaluation.Stop(
                    SimulationDecision.Blocked,
                    budget.Id,
                    applied,
                    alerts,
                    unchangedRemaining,
                    effectiveUlb);
            }
        }
        else
        {
            applied.Add(Observation("user-level-budget", GuardrailCategories.UserLevelBudget,
                GuardrailOutcome.NotApplicable, "No effective ULB applies to the selected user."));
        }

        var includedAvailable = poolRemaining;
        var includedControl = _applicability.ResolveIncludedUsageControl(
            snapshot,
            attribution,
            scenario.Timestamp);
        if (includedControl.IsAmbiguous)
        {
            return EconomicGuardrailEvaluation.Stop(
                SimulationDecision.Indeterminate,
                "included-control.ambiguous",
                applied,
                alerts,
                unchangedRemaining,
                effectiveUlb,
                "Multiple included-usage controls apply.");
        }

        decimal? controlRemaining = null;
        if (includedControl.Value is not null)
        {
            var costCenterEntitlement = balances.CalculateCostCenterEntitlement(
                billing,
                attribution.CostCenterId!,
                scenario.Timestamp);
            if (!costCenterEntitlement.IsKnown)
            {
                return EconomicGuardrailEvaluation.Stop(
                    SimulationDecision.Indeterminate,
                    "included-control.seat-inventory",
                    applied,
                    alerts,
                    unchangedRemaining,
                    effectiveUlb,
                    "The cost-center seat inventory contains an unknown plan allowance.");
            }

            controlRemaining = EconomicBalanceCalculator.Available(
                costCenterEntitlement.Credits,
                includedControl.Value.ConsumedCredits);
            unchangedRemaining = unchangedRemaining with
            {
                IncludedUsageControlCredits = controlRemaining
            };
            includedAvailable = Math.Min(includedAvailable, controlRemaining.Value);
            var controlBlocks = requestedCredits > controlRemaining &&
                includedControl.Value.OverflowBehavior == IncludedOverflowBehavior.Block;
            applied.Add(new AppliedGuardrail
            {
                Id = includedControl.Value.Id,
                MetadataKey = GuardrailMetadataKeys.IncludedUsageControl,
                Category = GuardrailCategories.IncludedUsageControl,
                Enforcement = controlBlocks ? GuardrailEnforcement.HardStop : GuardrailEnforcement.ObserveOnly,
                Outcome = controlBlocks ? GuardrailOutcome.Blocked : GuardrailOutcome.Passed,
                Limit = costCenterEntitlement.Credits,
                ConsumedBefore = includedControl.Value.ConsumedCredits,
                Requested = requestedCredits,
                RemainingAfter = controlBlocks
                    ? controlRemaining
                    : Math.Max(0m, controlRemaining.Value - Math.Min(requestedCredits, includedAvailable)),
                Message = controlBlocks
                    ? "The cost-center included-usage control blocks overflow."
                    : "The cost-center included-usage control permits this allocation or paid overflow."
            });
            if (controlBlocks)
            {
                return EconomicGuardrailEvaluation.Stop(
                    SimulationDecision.Blocked,
                    includedControl.Value.Id,
                    applied,
                    alerts,
                    unchangedRemaining,
                    effectiveUlb);
            }
        }
        else
        {
            applied.Add(Observation("included-usage-control", GuardrailCategories.IncludedUsageControl,
                GuardrailOutcome.NotApplicable, "No included-usage control applies to the attributed cost center."));
        }

        var split = configuration.PoolOverflowBehavior == PoolOverflowBehavior.Split;
        var includedCredits = requestedCredits <= includedAvailable
            ? requestedCredits
            : split ? includedAvailable : 0m;
        var meteredCredits = requestedCredits - includedCredits;
        var meteredUsd = meteredCredits * configuration.UsdPerCredit;
        var requiredAllocation = new CreditAllocation
        {
            TotalCredits = requestedCredits,
            IncludedCredits = includedCredits,
            IncludedUsageControlId = includedControl.Value?.Id,
            MeteredCredits = meteredCredits,
            MeteredUsd = meteredUsd
        };

        applied.Add(new AppliedGuardrail
        {
            Id = "enterprise-shared-pool",
            MetadataKey = GuardrailMetadataKeys.IncludedPool,
            Category = GuardrailCategories.IncludedPool,
            Enforcement = GuardrailEnforcement.ObserveOnly,
            Outcome = GuardrailOutcome.Passed,
            Limit = poolEntitlement.Credits,
            ConsumedBefore = snapshot.EnterprisePoolConsumedCredits,
            Requested = includedCredits,
            RemainingAfter = poolRemaining - includedCredits,
            Message = $"Proposed {includedCredits:G29} credits from the enterprise pool; allocation is accepted only if every check permits the request."
        });

        if (meteredCredits > 0)
        {
            if (!EconomicGuardrailApplicabilityResolver.Matches(
                    snapshot.PaidUsage.ProductIds,
                    scenario.ProductId) ||
                !EconomicGuardrailApplicabilityResolver.Matches(
                    snapshot.PaidUsage.SkuIds,
                    scenario.SkuId))
            {
                applied.Add(AuthorizationGuardrail(GuardrailOutcome.Blocked) with
                {
                    Id = "paid-usage.not-applicable",
                    Message = "Paid usage is not authorized for this product and SKU."
                });
                return EconomicGuardrailEvaluation.Stop(
                    SimulationDecision.Blocked,
                    "paid-usage.not-applicable",
                    applied,
                    alerts,
                    unchangedRemaining,
                    effectiveUlb,
                    "Paid usage is not authorized for this product and SKU.") with
                {
                    RequiredAllocation = requiredAllocation
                };
            }

            if (snapshot.PaidUsage.State == GuardrailValue.Unknown)
            {
                applied.Add(AuthorizationGuardrail(GuardrailOutcome.Indeterminate) with
                {
                    Id = "paid-usage.unknown",
                    Message = "Paid-usage authorization is unknown."
                });
                return EconomicGuardrailEvaluation.Stop(
                    SimulationDecision.Indeterminate,
                    "paid-usage.unknown",
                    applied,
                    alerts,
                    unchangedRemaining,
                    effectiveUlb,
                    "Paid-usage authorization is unknown.") with
                {
                    RequiredAllocation = requiredAllocation
                };
            }

            if (snapshot.PaidUsage.State == GuardrailValue.Disabled)
            {
                applied.Add(AuthorizationGuardrail(GuardrailOutcome.Blocked));
                return EconomicGuardrailEvaluation.Stop(
                    SimulationDecision.Blocked,
                    "paid-usage",
                    applied,
                    alerts,
                    unchangedRemaining,
                    effectiveUlb) with
                {
                    RequiredAllocation = requiredAllocation
                };
            }

            applied.Add(AuthorizationGuardrail(GuardrailOutcome.Passed));
        }
        else
        {
            applied.Add(AuthorizationGuardrail(GuardrailOutcome.NotApplicable) with
            {
                Message = "The workload is fully included; paid-usage authorization is not required."
            });
        }

        var applicableBudgets = meteredCredits == 0
            ? []
            : _applicability.ResolveSpendingBudgets(
                snapshot,
                attribution,
                scenario.ProductId,
                scenario.SkuId,
                scenario.Timestamp);
        if (applicableBudgets.Count == 0)
        {
            applied.Add(Observation("metered-spending-budget", GuardrailCategories.MeteredSpendingBudget,
                GuardrailOutcome.NotApplicable,
                meteredCredits == 0
                    ? "No metered charge requires a spending budget."
                    : "No spending budget applies to the attributed user, product, SKU, and timestamp."));
        }
        var budgetAlerts = new List<ThresholdEvent>();
        var budgetRemaining = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        SpendingBudget? blockingBudget = null;
        decimal? lowestHeadroom = null;

        foreach (var budget in applicableBudgets)
        {
            var headroom = EconomicBalanceCalculator.Headroom(
                budget.LimitUsd,
                budget.ConsumedUsd);
            var remainingAfter = headroom - meteredUsd;
            var blocks = budget.Enforcement == GuardrailEnforcement.HardStop && meteredUsd > headroom;
            var outcome = blocks ? GuardrailOutcome.Blocked : GuardrailOutcome.Passed;
            applied.Add(new AppliedGuardrail
            {
                Id = budget.Id,
                MetadataKey = budget.Scope switch
                {
                    SpendingBudgetScope.CostCenter => GuardrailMetadataKeys.MeteredBudgetCostCenter,
                    SpendingBudgetScope.Organization => GuardrailMetadataKeys.MeteredBudgetOrganization,
                    _ => GuardrailMetadataKeys.MeteredBudgetEnterprise
                },
                Category = GuardrailCategories.MeteredSpendingBudget,
                Enforcement = budget.Enforcement,
                Outcome = outcome,
                Limit = budget.LimitUsd,
                ConsumedBefore = budget.ConsumedUsd,
                Requested = meteredUsd,
                RemainingAfter = remainingAfter,
                Message = blocks
                    ? "This hard-stop spending budget cannot cover the metered charge."
                    : "This spending budget permits or only observes the metered charge."
            });
            budgetRemaining[budget.Id] = remainingAfter;
            AddAlerts(budgetAlerts, budget.Id, budget.LimitUsd, budget.ConsumedUsd, meteredUsd);

            if (blocks && (lowestHeadroom is null || headroom < lowestHeadroom))
            {
                blockingBudget = budget;
                lowestHeadroom = headroom;
            }
        }

        var allocation = new CreditAllocation
        {
            TotalCredits = requestedCredits,
            IncludedCredits = includedCredits,
            IncludedUsageControlId = includedControl.Value?.Id,
            MeteredCredits = meteredCredits,
            MeteredUsd = meteredUsd,
            MeteredBudgetId = blockingBudget?.Id ?? applicableBudgets.FirstOrDefault()?.Id,
            MeteredBudgetRemainingUsd = budgetRemaining
        };
        var remainingState = unchangedRemaining with
        {
            IncludedPoolCredits = poolRemaining - includedCredits,
            EffectiveUserBudgetCredits = effectiveUlb?.RemainingCredits,
            IncludedUsageControlCredits = controlRemaining is null
                ? null
                : Math.Max(0m, controlRemaining.Value - includedCredits),
            SpendingBudgetRemainingUsd = meteredCredits == 0
                ? unchangedRemaining.SpendingBudgetRemainingUsd
                : budgetRemaining
        };

        if (blockingBudget is null)
        {
            alerts.AddRange(budgetAlerts);
        }

        if (blockingBudget is null)
        {
            return new EconomicGuardrailEvaluation(
                SimulationDecision.Allowed,
                null,
                allocation,
                remainingState,
                effectiveUlb,
                applied,
                alerts,
                null)
            {
                RequiredAllocation = allocation
            };
        }

        var rejectedUlb = effectiveUlb is null
            ? null
            : effectiveUlb with
            {
                ReservedCredits = 0m,
                RemainingCredits = effectiveUlb.LimitCredits - effectiveUlb.ConsumedBeforeCredits
            };
        return new EconomicGuardrailEvaluation(
            SimulationDecision.Blocked,
            blockingBudget.Id,
            new CreditAllocation(),
            unchangedRemaining,
            rejectedUlb,
            applied,
            alerts,
            null)
        {
            RequiredAllocation = allocation
        };

        AppliedGuardrail AuthorizationGuardrail(GuardrailOutcome outcome) =>
            new()
            {
                Id = "paid-usage",
                MetadataKey = GuardrailMetadataKeys.PaidUsage,
                Category = GuardrailCategories.PaidUsageAuthorization,
                Enforcement = GuardrailEnforcement.HardStop,
                Outcome = outcome,
                Requested = meteredCredits,
                Message = outcome == GuardrailOutcome.Passed
                    ? "Paid usage is authorized."
                    : "Paid usage is disabled."
            };
    }

    private static void AddAlerts(
        ICollection<ThresholdEvent> alerts,
        string id,
        decimal limit,
        decimal consumed,
        decimal requested)
    {
        if (limit <= 0)
        {
            return;
        }

        var before = consumed / limit * 100m;
        var after = (consumed + requested) / limit * 100m;
        foreach (var threshold in AlertThresholds.Where(x => before < x && after >= x))
        {
            alerts.Add(new ThresholdEvent
            {
                GuardrailId = id,
                ThresholdPercent = threshold,
                BeforePercent = before,
                AfterPercent = after
            });
        }
    }

    private static AppliedGuardrail Observation(string id, string category, GuardrailOutcome outcome, string message) =>
        new()
        {
            Id = id,
            MetadataKey = category,
            Category = category,
            Enforcement = GuardrailEnforcement.ObserveOnly,
            Outcome = outcome,
            Message = message
        };

}

public sealed record EconomicGuardrailEvaluation(
    SimulationDecision Decision,
    string? FailingGuardrailId,
    CreditAllocation Allocation,
    RemainingState Remaining,
    EffectiveUserLevelBudgetResult? EffectiveUlb,
    IReadOnlyList<AppliedGuardrail> AppliedGuardrails,
    IReadOnlyList<ThresholdEvent> Alerts,
    string? Message)
{
    public CreditAllocation? RequiredAllocation { get; init; }

    public static EconomicGuardrailEvaluation Stop(
        SimulationDecision decision,
        string id,
        IReadOnlyList<AppliedGuardrail> applied,
        IReadOnlyList<ThresholdEvent> alerts,
        RemainingState remaining,
        EffectiveUserLevelBudgetResult? effectiveUlb = null,
        string? message = null) =>
        new(
            decision,
            id,
            new CreditAllocation(),
            remaining,
            effectiveUlb is null ? null : effectiveUlb with
            {
                ReservedCredits = 0m,
                RemainingCredits = effectiveUlb.LimitCredits - effectiveUlb.ConsumedBeforeCredits
            },
            applied,
            alerts,
            message);
}
