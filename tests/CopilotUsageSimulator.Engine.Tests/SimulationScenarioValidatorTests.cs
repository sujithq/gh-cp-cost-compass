using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class SimulationScenarioValidatorTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RejectsBlankRequiredIdentifiers()
    {
        AssertInvalid(Scenario() with { OperationId = " " }, "operationId");
        AssertInvalid(Scenario() with { PlanId = "" }, "planId");
        AssertInvalid(Scenario() with { ProductId = "\t" }, "productId");
        AssertInvalid(Scenario() with { SkuId = " " }, "skuId");
        AssertInvalid(
            Scenario() with { Calls = [new ModelCallInput { ModelId = " " }] },
            "calls[0].modelId");
        AssertInvalid(
            Scenario() with
            {
                AccessGates = new Dictionary<string, AccessGateState>
                {
                    [" "] = new()
                }
            },
            "accessGates");
        AssertInvalid(
            Scenario() with
            {
                ActionsUsage = new ActionsUsageInput { RunnerId = " " }
            },
            "actionsUsage.runnerId");
    }

    [Fact]
    public void RejectsInvalidBillingAndAttributionContracts()
    {
        AssertInvalid(
            Scenario() with
            {
                BillingContext = new BillingContext
                {
                    BillingEntityId = " ",
                    CycleStart = Timestamp,
                    CycleEnd = Timestamp.AddDays(1)
                }
            },
            "billingContext.billingEntityId");
        AssertInvalid(
            Scenario() with
            {
                BillingContext = Billing() with { CycleEnd = Timestamp }
            },
            "billingContext");
        AssertInvalid(
            Scenario() with
            {
                BillingContext = Billing() with
                {
                    SeatAssignments =
                    [
                        new EffectiveSeatAssignment
                        {
                            UserId = " ",
                            PlanId = "business"
                        }
                    ]
                }
            },
            "billingContext.seatAssignments[0].userId");
        AssertInvalid(
            Scenario() with
            {
                BillingContext = Billing() with
                {
                    SeatAssignments =
                    [
                        new EffectiveSeatAssignment
                        {
                            UserId = "user-1",
                            PlanId = "business",
                            EffectiveFrom = Timestamp,
                            EffectiveTo = Timestamp
                        }
                    ]
                }
            },
            "billingContext.seatAssignments[0].effectiveTo");
        AssertInvalid(
            Scenario() with
            {
                Attribution = new AttributionInput { UserId = " " }
            },
            "attribution.userId");
        AssertInvalid(
            Scenario() with
            {
                Attribution = new AttributionInput
                {
                    UserId = "user-1",
                    DirectAssignments =
                    [
                        new EffectiveCostCenterAssignment { CostCenterId = " " }
                    ]
                }
            },
            "attribution.directAssignments[0].costCenterId");
    }

    [Fact]
    public void RejectsPartiallyOverlappingSeatAssignmentsCaseInsensitively()
    {
        var scenario = Scenario() with
        {
            BillingContext = Billing() with
            {
                SeatAssignments =
                [
                    Seat("user-1", Timestamp, Timestamp.AddHours(8)),
                    Seat("USER-1", Timestamp.AddHours(4), Timestamp.AddHours(12))
                ]
            }
        };

        AssertInvalid(scenario, "overlapping effective periods for user 'user-1'");
    }

    [Fact]
    public void RejectsOpenEndedOverlappingSeatAssignments()
    {
        var scenario = Scenario() with
        {
            BillingContext = Billing() with
            {
                SeatAssignments =
                [
                    Seat("user-1", Timestamp, null),
                    Seat("user-1", Timestamp.AddHours(1), Timestamp.AddHours(2))
                ]
            }
        };

        AssertInvalid(scenario, "overlapping effective periods for user 'user-1'");
    }

    [Fact]
    public void AllowsAdjacentHistoricalSeatAssignmentsAndOverlapsForDistinctUsers()
    {
        var scenario = Scenario() with
        {
            BillingContext = Billing() with
            {
                SeatAssignments =
                [
                    Seat("user-1", Timestamp.AddDays(-2), Timestamp.AddDays(-1)),
                    Seat("USER-1", Timestamp.AddDays(-1), Timestamp),
                    Seat("user-1", Timestamp.AddHours(1), null),
                    Seat("user-2", Timestamp.AddDays(-1), Timestamp.AddHours(2))
                ]
            }
        };

        SimulationScenarioValidator.Validate(scenario);
    }

    [Fact]
    public void RejectsNegativeEconomicValuesAndInvalidEffectivePeriods()
    {
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with { EnterprisePoolConsumedCredits = -1m }
            },
            "economicGuardrails.enterprisePoolConsumedCredits");
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with
                {
                    UserLevelBudgets =
                    [
                        new UserLevelBudget
                        {
                            Id = "ulb",
                            Kind = UserLevelBudgetKind.Individual,
                            TargetId = "user-1",
                            LimitCredits = -1m
                        }
                    ]
                }
            },
            "economicGuardrails.userLevelBudgets[0].limitCredits");
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with
                {
                    IncludedUsageControls =
                    [
                        new CostCenterIncludedUsageControl
                        {
                            Id = "included",
                            CostCenterId = "cc-1",
                            ConsumedCredits = -1m
                        }
                    ]
                }
            },
            "economicGuardrails.includedUsageControls[0].consumedCredits");
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with
                {
                    SpendingBudgets =
                    [
                        new SpendingBudget
                        {
                            Id = "budget",
                            Scope = SpendingBudgetScope.CostCenter,
                            ScopeId = "cc-1",
                            LimitUsd = 1m,
                            ConsumedUsd = -1m
                        }
                    ]
                }
            },
            "economicGuardrails.spendingBudgets[0].consumedUsd");
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with
                {
                    UserLevelBudgets =
                    [
                        new UserLevelBudget
                        {
                            Id = "ulb",
                            Kind = UserLevelBudgetKind.Universal,
                            EffectiveFrom = Timestamp,
                            EffectiveTo = Timestamp
                        }
                    ]
                }
            },
            "economicGuardrails.userLevelBudgets[0]");
    }

    [Fact]
    public void RejectsMissingIdentifiersForScopedEconomicContracts()
    {
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with
                {
                    UserLevelBudgets =
                    [
                        new UserLevelBudget
                        {
                            Id = "ulb",
                            Kind = UserLevelBudgetKind.CostCenter
                        }
                    ]
                }
            },
            "economicGuardrails.userLevelBudgets[0].targetId");
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with
                {
                    SpendingBudgets =
                    [
                        new SpendingBudget
                        {
                            Id = "budget",
                            Scope = SpendingBudgetScope.Organization
                        }
                    ]
                }
            },
            "economicGuardrails.spendingBudgets[0].scopeId");
    }

    [Fact]
    public void RejectsInvalidRuntimeAndActionsGuardrailValues()
    {
        AssertInvalid(
            Scenario() with
            {
                RuntimeGuardrails = new RuntimeGuardrailSnapshot { MaximumModelCalls = -1 }
            },
            "runtimeGuardrails.maximumModelCalls");
        AssertInvalid(
            Scenario() with
            {
                RuntimeGuardrails = new RuntimeGuardrailSnapshot
                {
                    RequestedDuration = TimeSpan.FromSeconds(-1)
                }
            },
            "runtimeGuardrails.requestedDuration");
        AssertInvalid(
            Scenario() with
            {
                ActionsGuardrails = new ActionsGuardrailSnapshot { IncludedMinutes = -1m }
            },
            "actionsGuardrails.includedMinutes");
        AssertInvalid(
            Scenario() with
            {
                ActionsGuardrails = new ActionsGuardrailSnapshot
                {
                    Budgets =
                    [
                        new ActionsSpendingBudget
                        {
                            Id = "actions",
                            LimitUsd = -1m
                        }
                    ]
                }
            },
            "actionsGuardrails.budgets[0].limitUsd");
    }

    [Fact]
    public void RejectsDuplicateGuardrailIdsCaseInsensitively()
    {
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with
                {
                    UserLevelBudgets =
                    [
                        new UserLevelBudget { Id = "user-budget" },
                        new UserLevelBudget { Id = "USER-BUDGET" }
                    ]
                }
            },
            "economicGuardrails.userLevelBudgets");
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with
                {
                    IncludedUsageControls =
                    [
                        new CostCenterIncludedUsageControl
                        {
                            Id = "included-control",
                            CostCenterId = "cc-1"
                        },
                        new CostCenterIncludedUsageControl
                        {
                            Id = "INCLUDED-CONTROL",
                            CostCenterId = "cc-2"
                        }
                    ]
                }
            },
            "economicGuardrails.includedUsageControls");
        AssertInvalid(
            Scenario() with
            {
                EconomicGuardrails = Economy() with
                {
                    SpendingBudgets =
                    [
                        new SpendingBudget { Id = "spending-budget" },
                        new SpendingBudget { Id = "SPENDING-BUDGET" }
                    ]
                }
            },
            "economicGuardrails.spendingBudgets");
        AssertInvalid(
            Scenario() with
            {
                ActionsGuardrails = new ActionsGuardrailSnapshot
                {
                    Budgets =
                    [
                        new ActionsSpendingBudget { Id = "actions-budget" },
                        new ActionsSpendingBudget { Id = "ACTIONS-BUDGET" }
                    ]
                }
            },
            "actionsGuardrails.budgets");
    }

    [Theory]
    [InlineData("auto-model-selection", "auto-model-selection")]
    [InlineData("auto-model-selection", "AUTO-MODEL-SELECTION")]
    public void RejectsDuplicateCallMultiplierIdsCaseInsensitively(
        string firstMultiplierId,
        string secondMultiplierId)
    {
        AssertInvalid(
            Scenario() with
            {
                Calls =
                [
                    new ModelCallInput
                    {
                        ModelId = "gpt-5.6-luna",
                        EnabledMultiplierIds =
                        [
                            firstMultiplierId,
                            secondMultiplierId
                        ]
                    }
                ]
            },
            "calls[0].enabledMultiplierIds");
    }

    [Fact]
    public void AllowsDistinctCallMultiplierIds()
    {
        var scenario = Scenario() with
        {
            Calls =
            [
                new ModelCallInput
                {
                    ModelId = "gpt-5.6-luna",
                    EnabledMultiplierIds =
                    [
                        "auto-model-selection",
                        "agent-mode"
                    ]
                }
            ]
        };

        SimulationScenarioValidator.Validate(scenario);
    }

    [Fact]
    public void RejectsNegativeUsageValuesAtTheContractBoundary()
    {
        AssertInvalid(
            Scenario() with
            {
                Calls =
                [
                    new ModelCallInput
                    {
                        ModelId = "gpt-5.6-luna",
                        OutputTokens = -1
                    }
                ]
            },
            "calls[0].outputTokens");
        AssertInvalid(
            Scenario() with
            {
                ActionsUsage = new ActionsUsageInput
                {
                    RunnerId = "linux",
                    IncludedMinutesRemaining = -1m
                }
            },
            "actionsUsage.includedMinutesRemaining");
    }

    [Fact]
    public void AllowsConsumedValuesToExceedLimits()
    {
        var scenario = Scenario() with
        {
            EconomicGuardrails = Economy() with
            {
                UserLevelBudgets =
                [
                    new UserLevelBudget
                    {
                        Id = "ulb",
                        Kind = UserLevelBudgetKind.Universal,
                        LimitCredits = 1m,
                        ConsumedCredits = 2m
                    }
                ],
                SpendingBudgets =
                [
                    new SpendingBudget
                    {
                        Id = "budget",
                        Scope = SpendingBudgetScope.Enterprise,
                        LimitUsd = 1m,
                        ConsumedUsd = 2m
                    }
                ]
            },
            RuntimeGuardrails = new RuntimeGuardrailSnapshot
            {
                MaximumModelCalls = 1,
                ModelCallsConsumed = 2
            },
            ActionsGuardrails = new ActionsGuardrailSnapshot
            {
                IncludedMinutes = 1m,
                ConsumedIncludedMinutes = 2m
            }
        };

        SimulationScenarioValidator.Validate(scenario);
    }

    private static void AssertInvalid(SimulationScenario scenario, string expectedPath)
    {
        var exception = Assert.Throws<SimulationException>(
            () => SimulationScenarioValidator.Validate(scenario));

        Assert.Equal(SimulationScenarioValidator.InvalidContractCode, exception.Code);
        Assert.Contains(expectedPath, exception.Message);
    }

    private static SimulationScenario Scenario() =>
        new()
        {
            OperationId = "chat",
            PlanId = "business",
            Timestamp = Timestamp
        };

    private static BillingContext Billing() =>
        new()
        {
            BillingEntityId = "enterprise-1",
            CycleStart = Timestamp,
            CycleEnd = Timestamp.AddDays(1)
        };

    private static EffectiveSeatAssignment Seat(
        string userId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo) =>
        new()
        {
            UserId = userId,
            PlanId = "business",
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo
        };

    private static EconomicGuardrailSnapshot Economy() => new();
}
