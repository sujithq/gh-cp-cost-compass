using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

internal static class CompassFixture
{
    public const string ModelId = "mai-code-1.1-flash";
    public static readonly DateTimeOffset Timestamp = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    public static EngineConfiguration Configuration()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        return configuration with
        {
            Version = "compass-synthetic-arithmetic-fixture",
            ReferenceSnapshot = configuration.ReferenceSnapshot! with
            {
                CatalogVersion = "compass-synthetic-arithmetic-fixture",
                Assumptions = [.. configuration.ReferenceSnapshot.Assumptions, "Synthetic test-only pricing is not a published provider quote."]
            },
            Models = configuration.Models.Select(model => model.Id == ModelId
                ? model with
                {
                    PricePeriods =
                    [
                        new()
                        {
                            EffectiveFrom = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                            Tiers =
                            [
                                new()
                                {
                                    Id = "synthetic-one-dollar",
                                    InputUsdPerMillion = 1m,
                                    CachedInputUsdPerMillion = 1m,
                                    OutputUsdPerMillion = 1m
                                }
                            ]
                        }
                    ]
                }
                : model).ToArray()
        };
    }

    public static SimulationScenario Scenario(long freshTokens = 1_000_000) => new()
    {
        OperationId = "chat",
        PlanId = "business",
        Timestamp = Timestamp,
        Calls =
        [
            new()
            {
                ModelId = ModelId,
                ContextTokens = 100_000,
                FreshInputTokens = freshTokens,
                Metadata = new Dictionary<string, string> { ["fixture"] = "synthetic" }
            }
        ],
        AccessGates = new Dictionary<string, AccessGateState>
        {
            ["policy"] = new() { Passed = true, Reason = "Supplied test policy." }
        },
        BillingContext = new()
        {
            BillingEntityId = "enterprise-1",
            CycleStart = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            CycleEnd = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            SeatAssignments = [new() { UserId = "user-1", PlanId = "business", CostCenterId = "cc-1" }]
        },
        Attribution = new()
        {
            UserId = "user-1",
            LicensingOrganizationIds = ["org-1"],
            DirectAssignments = [new() { CostCenterId = "cc-1" }]
        },
        EconomicGuardrails = new()
        {
            EnterprisePoolConsumedCredits = 1_840m,
            UserLevelBudgets =
            [
                new()
                {
                    Id = "ulb-user-1",
                    Kind = UserLevelBudgetKind.Individual,
                    TargetId = "user-1",
                    LimitCredits = 1_000m
                }
            ],
            PaidUsage = new()
            {
                State = GuardrailValue.Disabled,
                ProductIds = new HashSet<string> { "github-copilot" },
                SkuIds = new HashSet<string> { "copilot-ai-credits" }
            },
            SpendingBudgets =
            [
                new()
                {
                    Id = "enterprise-spend",
                    Scope = SpendingBudgetScope.Enterprise,
                    LimitUsd = 100m,
                    Enforcement = GuardrailEnforcement.HardStop,
                    ProductIds = new HashSet<string> { "github-copilot" }
                }
            ]
        },
        Metadata = new Dictionary<string, string> { ["fixture"] = "synthetic" }
    };

    public static SimulationScenario Cloud(SimulationScenario scenario) => scenario with
    {
        OperationId = "cloud-agent",
        RepositoryVisibility = RepositoryVisibility.Private,
        ActionsUsage = new() { RunnerId = "linux-2-core", Minutes = 100m, IncludedMinutesRemaining = 10m },
        ActionsGuardrails = new()
        {
            ActionsEnabled = GuardrailValue.Enabled,
            RunnerAvailable = GuardrailValue.Enabled,
            WorkflowApproved = GuardrailValue.Enabled,
            RepositoryRulesPermitRun = GuardrailValue.Enabled,
            IncludedMinutes = 10m,
            Budgets = [new() { Id = "actions-spend", LimitUsd = 100m, Enforcement = GuardrailEnforcement.HardStop }]
        }
    };
}
