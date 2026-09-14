using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class SimulationEngineTests
{
    private static readonly DateTimeOffset CatalogDate = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly CopilotUsageSimulationEngine _engine =
        new(EngineConfigurationLoader.LoadDefault());

    [Fact]
    public void DefaultCatalogLoadsCompleteCoreDefinitions()
    {
        Assert.Equal("2026-09-11", _engine.Configuration.Version);
        Assert.Equal(7, _engine.Configuration.Plans.Count);
        Assert.True(_engine.Configuration.Models.Count >= 30);
        Assert.Contains(_engine.Configuration.Operations, x => x.Id == "cloud-agent");
    }

    [Fact]
    public void CalculatesAllTokenClassesAndRetainsFractionalCredits()
    {
        var result = _engine.Simulate(Scenario(
            Call(
                model: "gpt-5.6-luna",
                fresh: 1_000_000,
                cached: 1_000_000,
                cacheWrite: 1_000_000,
                output: 1_000_000)));

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal(1.67m, result.Calls.Single().RawUsd);
        Assert.Equal(167m, result.Allocation.TotalCredits);
        Assert.Equal(167m, result.Allocation.IncludedCredits);
    }

    [Fact]
    public void AppliesConfiguredMultipliersInRequestedOrder()
    {
        var call = Call(
            model: "gpt-5.6-luna",
            fresh: 1_000_000,
            multipliers:
            [
                "auto-model-selection",
                "data-residency-fedramp"
            ]);

        var result = _engine.Simulate(Scenario(call));

        Assert.Equal(0.198m, result.Calls.Single().AdjustedUsd);
        Assert.Equal(19.8m, result.Allocation.TotalCredits);
    }

    [Fact]
    public void SelectsLongContextTier()
    {
        var result = _engine.Simulate(Scenario(
            Call(model: "gpt-5.6-luna", context: 200_001, fresh: 1_000_000)));

        Assert.Equal("long-context", result.Calls.Single().PriceTierId);
        Assert.Equal(40m, result.Allocation.TotalCredits);
    }

    [Fact]
    public void StopsAtFirstFailingAccessGate()
    {
        var scenario = Scenario(Call()) with
        {
            AccessGates = new Dictionary<string, AccessGateState>
            {
                ["network"] = new()
                {
                    Passed = false,
                    Reason = "Proxy timeout",
                    Remediation = "Allow the Copilot endpoint."
                },
                ["authentication"] = new() { Passed = false, Reason = "Expired token" }
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("network", result.FirstFailingGate);
        Assert.Empty(result.Calls);
        Assert.Contains(result.Explanation, x => x.Message.Contains("Proxy timeout"));
    }

    [Fact]
    public void UnknownActiveSeatPlanIsIndeterminateBeforeAnAccessFailure()
    {
        var scenario = Scenario(Call()) with
        {
            BillingContext = Scenario().BillingContext! with
            {
                SeatAssignments =
                [
                    new EffectiveSeatAssignment
                    {
                        UserId = "user-1",
                        PlanId = "unknown-plan",
                        CostCenterId = "cc-1"
                    }
                ]
            },
            AccessGates = new Dictionary<string, AccessGateState>
            {
                ["network"] = new() { Passed = false }
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Indeterminate, result.Decision);
        Assert.Equal("pool.seat-inventory", result.FirstFailingGate);
        Assert.Empty(result.Calls);
        Assert.Equal(0m, result.Remaining.IncludedPoolCredits);
        Assert.Null(result.Remaining.ActionsIncludedMinutes);
        Assert.Contains(
            result.Explanation,
            entry => entry.Code == "pool.seat-inventory");
    }

    [Fact]
    public void MissingEffectivePlanAllowanceIsIndeterminate()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        configuration = configuration with
        {
            Plans = configuration.Plans
                .Select(plan => plan.Id == "business"
                    ? plan with
                    {
                        AllowancePeriods =
                        [
                            new PlanAllowancePeriod
                            {
                                EffectiveFrom = CatalogDate.AddDays(1),
                                IncludedCreditsPerUser = 1_900m
                            }
                        ]
                    }
                    : plan)
                .ToArray()
        };
        var engine = new CopilotUsageSimulationEngine(configuration);

        var result = engine.Simulate(Scenario(Call()));

        Assert.Equal(SimulationDecision.Indeterminate, result.Decision);
        Assert.Equal("pool.seat-inventory", result.FirstFailingGate);
        Assert.Empty(result.Calls);
    }

    [Fact]
    public void OutOfCycleAccessFailureRetainsKnownPooledSeatBalanceWhenAnotherPlanIsUnknown()
    {
        var scenario = Scenario(Call()) with
        {
            Timestamp = CatalogDate.AddMonths(1),
            BillingContext = Scenario().BillingContext! with
            {
                SeatAssignments =
                [
                    .. Scenario().BillingContext!.SeatAssignments,
                    new EffectiveSeatAssignment
                    {
                        UserId = "user-2",
                        PlanId = "unknown-plan",
                        CostCenterId = "cc-1"
                    }
                ]
            },
            AccessGates = new Dictionary<string, AccessGateState>
            {
                ["network"] = new() { Passed = false }
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("network", result.FirstFailingGate);
        Assert.Equal(1_900m, result.Remaining.IncludedPoolCredits);
    }

    [Fact]
    public void UsesCanonicalGateIdWhenStrictGateIsUnspecified()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        var strictConfiguration = configuration with
        {
            Gates = configuration.Gates
                .Select(x => x.Id == "network" ? x with { PassWhenUnspecified = false } : x)
                .ToArray()
        };
        var engine = new CopilotUsageSimulationEngine(strictConfiguration);

        var result = engine.Simulate(Scenario(Call()));

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("network", result.FirstFailingGate);
    }

    [Fact]
    public void UserBudgetAlwaysBlocksBeforePoolAllocation()
    {
        var scenario = Scenario(Call(fresh: 1_000_000)) with
        {
            EconomicGuardrails = Economy(
                userBudgets:
                [
                    new UserLevelBudget
                    {
                        Id = "individual-ulb",
                        Kind = UserLevelBudgetKind.Individual,
                        TargetId = "user-1",
                        LimitCredits = 10m
                    }
                ])
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("individual-ulb", result.FirstFailingGate);
        Assert.Equal(1_900m, result.Remaining.IncludedPoolCredits);
    }

    [Fact]
    public void SplitsRequestBetweenPoolAndMeteredUsage()
    {
        var scenario = Scenario(Call(fresh: 1_000_000)) with
        {
            EconomicGuardrails = Economy(
                poolConsumed: 1_890m,
                paidUsage: GuardrailValue.Enabled,
                spendingBudgets:
                [
                    new SpendingBudget
                    {
                        Id = "enterprise-budget",
                        Scope = SpendingBudgetScope.Enterprise,
                        LimitUsd = 10m,
                        Enforcement = GuardrailEnforcement.HardStop
                    }
                ])
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal(10m, result.Allocation.IncludedCredits);
        Assert.Equal(10m, result.Allocation.MeteredCredits);
        Assert.Equal(0.10m, result.Allocation.MeteredUsd);
        Assert.Equal(9.90m, result.Allocation.MeteredBudgetRemainingUsd["enterprise-budget"]);
        Assert.Equal(9.90m, result.Remaining.SpendingBudgetRemainingUsd["enterprise-budget"]);
    }

    [Fact]
    public void CanMeterEntireRequestWhenPoolCannotCoverIt()
    {
        var configuration = EngineConfigurationLoader.LoadDefault() with
        {
            PoolOverflowBehavior = PoolOverflowBehavior.MeterEntireRequest
        };
        var engine = new CopilotUsageSimulationEngine(configuration);
        var scenario = Scenario(Call(fresh: 1_000_000)) with
        {
            EconomicGuardrails = Economy(
                poolConsumed: 1_890m,
                paidUsage: GuardrailValue.Enabled)
        };

        var result = engine.Simulate(scenario);

        Assert.Equal(0m, result.Allocation.IncludedCredits);
        Assert.Equal(20m, result.Allocation.MeteredCredits);
        Assert.Equal(10m, result.Remaining.IncludedPoolCredits);
    }

    [Fact]
    public void IncludedUsageControlCanBlockOverflow()
    {
        var scenario = Scenario(Call(fresh: 1_000_000)) with
        {
            EconomicGuardrails = Economy(
                includedControls:
                [
                    new CostCenterIncludedUsageControl
                    {
                        Id = "cost-center-control",
                        CostCenterId = "cc-1",
                        ConsumedCredits = 1_890m,
                        OverflowBehavior = IncludedOverflowBehavior.Block
                    }
                ])
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("cost-center-control", result.FirstFailingGate);
    }

    [Fact]
    public void AllocationIdentifiesAppliedIncludedUsageControl()
    {
        var scenario = Scenario(Call(fresh: 1_000)) with
        {
            EconomicGuardrails = Economy(
                includedControls:
                [
                    new CostCenterIncludedUsageControl
                    {
                        Id = "cost-center-control",
                        CostCenterId = "cc-1"
                    }
                ])
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal("cost-center-control", result.Allocation.IncludedUsageControlId);
    }

    [Fact]
    public void AlertOnlyMeteredBudgetAllowsOverspend()
    {
        var scenario = Scenario(Call(fresh: 1_000_000)) with
        {
            EconomicGuardrails = Economy(
                poolConsumed: 1_900m,
                paidUsage: GuardrailValue.Enabled,
                spendingBudgets:
                [
                    new SpendingBudget
                    {
                        Id = "alert-only",
                        Scope = SpendingBudgetScope.Enterprise,
                        LimitUsd = 0.05m,
                        Enforcement = GuardrailEnforcement.AlertOnly
                    }
                ])
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal(-0.15m, result.Allocation.MeteredBudgetRemainingUsd["alert-only"]);
        Assert.Equal(-0.15m, result.Remaining.SpendingBudgetRemainingUsd["alert-only"]);
    }

    [Fact]
    public void AddsActionsCostForPrivateRepositoryCodeReview()
    {
        var scenario = Scenario(Call(), operation: "code-review") with
        {
            ActionsUsage = new ActionsUsageInput
            {
                RunnerId = "linux-2-core",
                Minutes = 10m,
                IncludedMinutesRemaining = 4m
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.NotNull(result.ActionsUsage);
        Assert.Equal(6m, result.ActionsUsage.BillableMinutes);
        Assert.Equal(0.036m, result.ActionsUsage.AdditionalUsd);
        Assert.Equal(0m, result.Remaining.ActionsIncludedMinutes);
    }

    [Fact]
    public void UnbilledOperationDoesNotRequireModelCalls()
    {
        var result = _engine.Simulate(Scenario(operation: "code-completion"));

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Empty(result.Calls);
        Assert.Equal(0m, result.Allocation.TotalCredits);
    }

    [Fact]
    public void BilledOperationRequiresCompleteEconomicContext()
    {
        var scenario = new SimulationScenario
        {
            OperationId = "chat",
            PlanId = "business",
            Timestamp = CatalogDate,
            Calls = [Call()]
        };

        var exception = Assert.Throws<SimulationException>(() => _engine.Simulate(scenario));

        Assert.Equal("economic-context-required", exception.Code);
    }

    [Fact]
    public void DirectSimulationRejectsNullCollectionsWithDomainError()
    {
        var scenario = Scenario() with { Calls = null! };

        var exception = Assert.Throws<SimulationException>(() => _engine.Simulate(scenario));

        Assert.Equal(SimulationScenarioValidator.InvalidContractCode, exception.Code);
        Assert.Contains("calls", exception.Message);
    }

    [Fact]
    public void UnbilledOperationIgnoresBillingAndRuntimeGuardrails()
    {
        var scenario = Scenario(operation: "code-completion") with
        {
            BillingContext = new BillingContext
            {
                BillingEntityId = "enterprise-1",
                CycleStart = CatalogDate.AddDays(-1),
                CycleEnd = CatalogDate.AddDays(1)
            },
            RuntimeGuardrails = new RuntimeGuardrailSnapshot
            {
                MaximumDuration = TimeSpan.Zero,
                RequestedDuration = TimeSpan.FromMinutes(1)
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Null(result.FirstFailingGate);
        Assert.Empty(result.AppliedGuardrails);
    }

    [Fact]
    public void UnbilledOperationStillEnforcesAccessGatesInFullMode()
    {
        var scenario = Scenario(operation: "code-completion") with
        {
            AccessGates = new Dictionary<string, AccessGateState>
            {
                ["network"] = new() { Passed = false, Reason = "Network unavailable." }
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("network", result.FirstFailingGate);
    }

    [Fact]
    public void DerivesDefaultPoolFromPlanAllowance()
    {
        var scenario = Scenario(Call(fresh: 1_000_000)) with
        {
            EconomicGuardrails = Economy()
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal(20m, result.Allocation.IncludedCredits);
        Assert.Equal(1_880m, result.Remaining.IncludedPoolCredits);
    }

    [Theory]
    [InlineData(SimulationCheckScope.All)]
    [InlineData(SimulationCheckScope.CostRelatedOnly)]
    public void MatchingEffectiveSeatSupportsSelectedPlanInEveryScope(
        SimulationCheckScope checkScope)
    {
        var scenario = Scenario(Call()) with { CheckScope = checkScope };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
    }

    [Fact]
    public void EffectiveSeatPlanMatchesSelectedPlanCaseInsensitively()
    {
        var scenario = Scenario(Call()) with
        {
            BillingContext = Scenario().BillingContext! with
            {
                SeatAssignments =
                [
                    Scenario().BillingContext!.SeatAssignments[0] with
                    {
                        PlanId = "BUSINESS"
                    }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
    }

    [Fact]
    public void ConflictingEffectiveSeatPlanIsRejectedAsInvalidContract()
    {
        var scenario = Scenario(Call()) with
        {
            PlanId = "enterprise"
        };

        var exception = Assert.Throws<SimulationException>(() => _engine.Simulate(scenario));

        Assert.Equal(SimulationScenarioValidator.InvalidContractCode, exception.Code);
        Assert.Contains("enterprise", exception.Message);
        Assert.Contains("business", exception.Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("2026-09-02T00:00:00+00:00", null)]
    [InlineData(null, "2026-08-01T00:00:00+00:00")]
    public void MissingOrIneffectiveSelectedUserSeatIsIndeterminate(
        string? effectiveFrom,
        string? effectiveTo)
    {
        var seats = effectiveFrom is null && effectiveTo is null
            ? Array.Empty<EffectiveSeatAssignment>()
            :
            [
                Scenario().BillingContext!.SeatAssignments[0] with
                {
                    EffectiveFrom = effectiveFrom is null
                        ? DateTimeOffset.MinValue
                        : DateTimeOffset.Parse(effectiveFrom),
                    EffectiveTo = effectiveTo is null
                        ? null
                        : DateTimeOffset.Parse(effectiveTo)
                }
            ];
        var scenario = Scenario(Call()) with
        {
            BillingContext = Scenario().BillingContext! with { SeatAssignments = seats }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Indeterminate, result.Decision);
        Assert.Equal("seat-assignment.missing", result.FirstFailingGate);
    }

    [Fact]
    public void SeatForAnotherUserDoesNotSatisfySelectedUserInvariant()
    {
        var scenario = Scenario(Call()) with
        {
            BillingContext = Scenario().BillingContext! with
            {
                SeatAssignments =
                [
                    Scenario().BillingContext!.SeatAssignments[0] with
                    {
                        UserId = "user-2"
                    }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Indeterminate, result.Decision);
        Assert.Equal("seat-assignment.missing", result.FirstFailingGate);
    }

    [Fact]
    public void OverlappingSelectedUserSeatsAreInvalid()
    {
        var seat = Scenario().BillingContext!.SeatAssignments[0];
        var scenario = Scenario(Call()) with
        {
            BillingContext = Scenario().BillingContext! with
            {
                SeatAssignments = [seat, seat with { CostCenterId = "cc-2" }]
            }
        };

        var exception = Assert.Throws<SimulationException>(() => _engine.Simulate(scenario));

        Assert.Equal(SimulationScenarioValidator.InvalidContractCode, exception.Code);
        Assert.Contains("overlapping effective periods", exception.Message);
    }

    [Fact]
    public void RejectsDuplicateConfigurationIds()
    {
        var configuration = EngineConfigurationLoader.LoadDefault() with
        {
            Operations =
            [
                new() { Id = "chat" },
                new() { Id = "CHAT" }
            ]
        };

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationValidator.Validate(configuration));

        Assert.Contains("Duplicate operation", exception.Message);
    }

    private static SimulationScenario Scenario(
        ModelCallInput? call = null,
        string operation = "chat") =>
        new()
        {
            OperationId = operation,
            PlanId = "business",
            Timestamp = CatalogDate,
            RepositoryVisibility = RepositoryVisibility.Private,
            Calls = call is null ? [] : [call],
            BillingContext = new BillingContext
            {
                BillingEntityId = "enterprise-1",
                CycleStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                CycleEnd = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                SeatAssignments =
                [
                    new EffectiveSeatAssignment
                    {
                        UserId = "user-1",
                        PlanId = "business",
                        CostCenterId = "cc-1"
                    }
                ]
            },
            Attribution = new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1"],
                DirectAssignments = [new EffectiveCostCenterAssignment { CostCenterId = "cc-1" }]
            },
            EconomicGuardrails = Economy()
        };

    private static EconomicGuardrailSnapshot Economy(
        decimal poolConsumed = 0m,
        GuardrailValue paidUsage = GuardrailValue.Disabled,
        IReadOnlyList<UserLevelBudget>? userBudgets = null,
        IReadOnlyList<CostCenterIncludedUsageControl>? includedControls = null,
        IReadOnlyList<SpendingBudget>? spendingBudgets = null) =>
        new()
        {
            EnterprisePoolConsumedCredits = poolConsumed,
            PaidUsage = new PaidUsageAuthorization { State = paidUsage },
            UserLevelBudgets = userBudgets ?? [],
            IncludedUsageControls = includedControls ?? [],
            SpendingBudgets = spendingBudgets ?? []
        };

    private static ModelCallInput Call(
        string model = "gpt-5.6-luna",
        long context = 1_000,
        long fresh = 1_000,
        long cached = 0,
        long cacheWrite = 0,
        long output = 0,
        IReadOnlyList<string>? multipliers = null) =>
        new()
        {
            ModelId = model,
            ContextTokens = context,
            FreshInputTokens = fresh,
            CachedInputTokens = cached,
            CacheWriteTokens = cacheWrite,
            OutputTokens = output,
            EnabledMultiplierIds = multipliers ?? []
        };
}
