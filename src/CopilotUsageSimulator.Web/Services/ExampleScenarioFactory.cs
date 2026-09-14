using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public static class ExampleScenarioFactory
{
    public static SimulationScenario Create(
        EngineConfiguration configuration,
        string operationId,
        ExampleScenarioVariant variant = ExampleScenarioVariant.Standard,
        DateTimeOffset? simulationTimestamp = null)
    {
        var timestamp = simulationTimestamp ?? DateTimeOffset.UtcNow;
        var operation = configuration.Operations.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, operationId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ConfigurationException($"Unknown example operation '{operationId}'.");
        var plan = ResolvePlan(configuration, timestamp);
        var secondaryPlan = configuration.Plans.FirstOrDefault(candidate =>
            candidate.IsPooled && !string.Equals(candidate.Id, plan.Id, StringComparison.OrdinalIgnoreCase))
            ?? plan;
        var model = operation.IsBilled ? ResolveModel(configuration, timestamp) : null;
        var runner = operation.ActionsMetering == ActionsMeteringMode.None
            ? null
            : ResolveRunner(configuration);
        var defaults = configuration.ExampleScenario;

        var scenario = new SimulationScenario
        {
            OperationId = operation.Id,
            PlanId = plan.Id,
            ProductId = defaults.ProductId,
            SkuId = defaults.SkuId,
            Timestamp = timestamp,
            CheckScope = SimulationCheckScope.CostRelatedOnly,
            RepositoryVisibility = RepositoryVisibility.Private,
            Metadata = new Dictionary<string, string>
            {
                ["task"] = operation.ExampleTask ?? "Run the selected operation and report the result.",
                ["estimate"] = "expected"
            },
            Calls = model is null
                ? []
                :
                [
                    new ModelCallInput
                    {
                        ModelId = model.Id,
                        ContextTokens = 45_000,
                        FreshInputTokens = 30_000,
                        CachedInputTokens = 10_000,
                        CacheWriteTokens = 2_000,
                        OutputTokens = 8_000
                    }
                ],
            AccessGates = configuration.Gates.ToDictionary(
                gate => gate.Id,
                _ => new AccessGateState { Passed = true },
                StringComparer.OrdinalIgnoreCase),
            BillingContext = new BillingContext
            {
                BillingEntityId = "enterprise-1",
                CycleStart = new DateTimeOffset(timestamp.Year, timestamp.Month, 1, 0, 0, 0, TimeSpan.Zero),
                CycleEnd = new DateTimeOffset(timestamp.Year, timestamp.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1),
                SeatAssignments =
                [
                    new EffectiveSeatAssignment
                    {
                        UserId = "user-1",
                        PlanId = plan.Id,
                        CostCenterId = "cc-engineering"
                    },
                    new EffectiveSeatAssignment
                    {
                        UserId = "user-2",
                        PlanId = secondaryPlan.Id,
                        CostCenterId = "cc-engineering"
                    }
                ]
            },
            Attribution = new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-engineering"],
                DirectAssignments = [new EffectiveCostCenterAssignment { CostCenterId = "cc-engineering" }]
            },
            EconomicGuardrails = new EconomicGuardrailSnapshot
            {
                EnterprisePoolConsumedCredits = 500m,
                UserLevelBudgets =
                [
                    new UserLevelBudget
                    {
                        Id = "ulb-universal",
                        Kind = UserLevelBudgetKind.Universal,
                        LimitCredits = 2_000m,
                        ConsumedCredits = 250m
                    },
                    new UserLevelBudget
                    {
                        Id = "ulb-cost-center",
                        Kind = UserLevelBudgetKind.CostCenter,
                        TargetId = "cc-engineering",
                        LimitCredits = 1_200m,
                        ConsumedCredits = 150m
                    },
                    new UserLevelBudget
                    {
                        Id = "ulb-user-1",
                        Kind = UserLevelBudgetKind.Individual,
                        TargetId = "user-1",
                        LimitCredits = 750m,
                        ConsumedCredits = 100m
                    }
                ],
                IncludedUsageControls =
                [
                    new CostCenterIncludedUsageControl
                    {
                        Id = "included-cc-engineering",
                        CostCenterId = "cc-engineering",
                        ConsumedCredits = 400m,
                        OverflowBehavior = IncludedOverflowBehavior.PaidUsage
                    }
                ],
                PaidUsage = new PaidUsageAuthorization
                {
                    State = GuardrailValue.Enabled,
                    ProductIds = new HashSet<string> { defaults.ProductId },
                    SkuIds = new HashSet<string> { defaults.SkuId }
                },
                SpendingBudgets =
                [
                    new SpendingBudget
                    {
                        Id = "budget-cost-center",
                        Scope = SpendingBudgetScope.CostCenter,
                        ScopeId = "cc-engineering",
                        LimitUsd = 100m,
                        ConsumedUsd = 25m,
                        Enforcement = GuardrailEnforcement.HardStop
                    },
                    new SpendingBudget
                    {
                        Id = "budget-organization",
                        Scope = SpendingBudgetScope.Organization,
                        ScopeId = "org-engineering",
                        LimitUsd = 250m,
                        ConsumedUsd = 40m,
                        Enforcement = GuardrailEnforcement.AlertOnly
                    },
                    new SpendingBudget
                    {
                        Id = "budget-enterprise",
                        Scope = SpendingBudgetScope.Enterprise,
                        LimitUsd = 500m,
                        ConsumedUsd = 75m,
                        Enforcement = GuardrailEnforcement.HardStop
                    }
                ]
            },
            RuntimeGuardrails = new RuntimeGuardrailSnapshot
            {
                MaximumModelCalls = 20,
                MaximumSubagentDepth = 5,
                RequestedSubagentDepth = 2,
                MaximumDuration = TimeSpan.FromMinutes(60),
                RequestedDuration = TimeSpan.FromMinutes(10),
                CliSoftCreditLimit = 2_000m
            },
            ActionsUsage = runner is not null
                ? new ActionsUsageInput
                {
                    RunnerId = runner.Id,
                    Minutes = 10m,
                    IncludedMinutesRemaining = 1_000m
                }
                : null,
            ActionsGuardrails = runner is not null
                ? new ActionsGuardrailSnapshot
                {
                    ActionsEnabled = GuardrailValue.Enabled,
                    RunnerAvailable = GuardrailValue.Enabled,
                    WorkflowApproved = GuardrailValue.Enabled,
                    RepositoryRulesPermitRun = GuardrailValue.Enabled,
                    IncludedMinutes = 2_000m,
                    ConsumedIncludedMinutes = 500m,
                    Budgets =
                    [
                        new ActionsSpendingBudget
                        {
                            Id = "actions-enterprise",
                            LimitUsd = 100m,
                            ConsumedUsd = 10m,
                            Enforcement = GuardrailEnforcement.HardStop
                        }
                    ]
                }
                : null
        };

        if (variant == ExampleScenarioVariant.Standard)
        {
            return scenario;
        }

        if (!operation.IsBilled)
        {
            throw new ConfigurationException(
                $"Unbilled example operation '{operation.Id}' cannot use a cost-blocked variant.");
        }

        var poolEntitlement = ResolveIncludedCredits(plan, timestamp) +
            ResolveIncludedCredits(secondaryPlan, timestamp);
        var meteredScenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                EnterprisePoolConsumedCredits = poolEntitlement,
                IncludedUsageControls = scenario.EconomicGuardrails.IncludedUsageControls
                    .Select(control => control with
                    {
                        ConsumedCredits = poolEntitlement,
                        OverflowBehavior = IncludedOverflowBehavior.PaidUsage
                    })
                    .ToArray()
            }
        };

        return variant switch
        {
            ExampleScenarioVariant.UserLevelBudgetExceeded => scenario with
            {
                EconomicGuardrails = scenario.EconomicGuardrails! with
                {
                    UserLevelBudgets = scenario.EconomicGuardrails.UserLevelBudgets
                        .Select(budget => budget.Kind == UserLevelBudgetKind.Individual &&
                            string.Equals(budget.TargetId, "user-1", StringComparison.OrdinalIgnoreCase)
                                ? budget with { ConsumedCredits = budget.LimitCredits }
                                : budget)
                        .ToArray()
                }
            },
            ExampleScenarioVariant.IncludedUseOverflowProhibited => scenario with
            {
                EconomicGuardrails = scenario.EconomicGuardrails! with
                {
                    IncludedUsageControls = scenario.EconomicGuardrails.IncludedUsageControls
                        .Select(control => control with
                        {
                            ConsumedCredits = poolEntitlement,
                            OverflowBehavior = IncludedOverflowBehavior.Block
                        })
                        .ToArray()
                }
            },
            ExampleScenarioVariant.PaidUsageNotApplicable => meteredScenario with
            {
                EconomicGuardrails = meteredScenario.EconomicGuardrails! with
                {
                    PaidUsage = meteredScenario.EconomicGuardrails.PaidUsage with
                    {
                        ProductIds = new HashSet<string> { "github-actions" }
                    }
                }
            },
            ExampleScenarioVariant.PaidUsageDisabled => meteredScenario with
            {
                EconomicGuardrails = meteredScenario.EconomicGuardrails! with
                {
                    PaidUsage = meteredScenario.EconomicGuardrails.PaidUsage with
                    {
                        State = GuardrailValue.Disabled
                    }
                }
            },
            ExampleScenarioVariant.AiSpendingBudgetExceeded => meteredScenario with
            {
                EconomicGuardrails = meteredScenario.EconomicGuardrails! with
                {
                    SpendingBudgets = meteredScenario.EconomicGuardrails.SpendingBudgets
                        .Select(budget => budget.Scope == SpendingBudgetScope.CostCenter
                            ? budget with
                            {
                                ConsumedUsd = budget.LimitUsd,
                                Enforcement = GuardrailEnforcement.HardStop
                            }
                            : budget)
                        .ToArray()
                }
            },
            ExampleScenarioVariant.ActionsSpendingBudgetExceeded when runner is not null && runner.UsdPerMinute > 0m =>
                scenario with
                {
                    ActionsUsage = scenario.ActionsUsage! with { IncludedMinutesRemaining = 0m },
                    ActionsGuardrails = scenario.ActionsGuardrails! with
                    {
                        IncludedMinutes = 0m,
                        ConsumedIncludedMinutes = 0m,
                        Budgets = scenario.ActionsGuardrails.Budgets
                            .Select(budget => budget with
                            {
                                ConsumedUsd = budget.LimitUsd,
                                Enforcement = GuardrailEnforcement.HardStop
                            })
                            .ToArray()
                    }
                },
            ExampleScenarioVariant.ActionsSpendingBudgetExceeded => throw new ConfigurationException(
                $"Example operation '{operation.Id}' cannot produce billable Actions usage."),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown example scenario variant.")
        };
    }

    private static PlanDefinition ResolvePlan(
        EngineConfiguration configuration,
        DateTimeOffset timestamp)
    {
        var preferred = FindPreferred(
            configuration.Plans,
            configuration.ExampleScenario.PreferredPlanId,
            plan => plan.Id);
        return preferred is not null && HasEffectiveAllowance(preferred, timestamp)
            ? preferred
            : configuration.Plans.FirstOrDefault(plan =>
                plan.IsPooled && HasEffectiveAllowance(plan, timestamp))
                ?? configuration.Plans.FirstOrDefault()
                ?? throw new ConfigurationException("An example scenario requires at least one plan.");
    }

    private static ModelDefinition ResolveModel(
        EngineConfiguration configuration,
        DateTimeOffset timestamp)
    {
        var preferred = FindPreferred(
            configuration.Models,
            configuration.ExampleScenario.PreferredModelId,
            model => model.Id);
        if (preferred is not null && HasEffectivePrice(preferred, timestamp))
        {
            return preferred;
        }

        return configuration.Models.FirstOrDefault(model => HasEffectivePrice(model, timestamp))
            ?? throw new ConfigurationException(
                $"An example scenario requires a model with pricing effective at {timestamp:O}.");
    }

    private static ActionsRunnerDefinition ResolveRunner(EngineConfiguration configuration) =>
        FindPreferred(
            configuration.ActionsRunners,
            configuration.ExampleScenario.PreferredActionsRunnerId,
            runner => runner.Id)
        ?? configuration.ActionsRunners.FirstOrDefault()
        ?? throw new ConfigurationException(
            "An operation that uses GitHub Actions requires at least one Actions runner.");

    private static T? FindPreferred<T>(
        IEnumerable<T> values,
        string? preferredId,
        Func<T, string> idSelector) where T : class =>
        string.IsNullOrWhiteSpace(preferredId)
            ? null
            : values.SingleOrDefault(value =>
                string.Equals(idSelector(value), preferredId, StringComparison.OrdinalIgnoreCase));

    private static bool HasEffectivePrice(ModelDefinition model, DateTimeOffset timestamp) =>
        model.PricePeriods.Any(period =>
            timestamp >= period.EffectiveFrom &&
            (period.EffectiveTo is null || timestamp < period.EffectiveTo) &&
            period.Tiers.Any(tier =>
                (tier.MinimumContextTokensExclusive is null || 45_000 > tier.MinimumContextTokensExclusive) &&
                (tier.MaximumContextTokensInclusive is null || 45_000 <= tier.MaximumContextTokensInclusive)));

    private static bool HasEffectiveAllowance(PlanDefinition plan, DateTimeOffset timestamp) =>
        plan.AllowancePeriods.Any(period =>
            period.IncludedCreditsPerUser is not null &&
            period.EffectiveFrom <= timestamp &&
            (period.EffectiveTo is null || timestamp < period.EffectiveTo));

    private static decimal ResolveIncludedCredits(PlanDefinition plan, DateTimeOffset timestamp) =>
        plan.AllowancePeriods.Single(period =>
            period.IncludedCreditsPerUser is not null &&
            period.EffectiveFrom <= timestamp &&
            (period.EffectiveTo is null || timestamp < period.EffectiveTo))
            .IncludedCreditsPerUser!.Value;
}

public enum ExampleScenarioVariant
{
    Standard,
    UserLevelBudgetExceeded,
    IncludedUseOverflowProhibited,
    PaidUsageNotApplicable,
    PaidUsageDisabled,
    AiSpendingBudgetExceeded,
    ActionsSpendingBudgetExceeded
}
