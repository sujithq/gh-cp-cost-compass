using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class GuardrailEngineTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly CopilotUsageSimulationEngine _engine =
        new(EngineConfigurationLoader.LoadDefault());

    [Fact]
    public void DirectCostCenterAssignmentWinsOverTeamAndOrganization()
    {
        var scenario = RichScenario() with
        {
            Attribution = new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1"],
                DirectAssignments = [new() { CostCenterId = "cc-direct" }],
                TeamAssignments =
                [
                    new()
                    {
                        TeamId = "team-1",
                        CostCenterId = "cc-team",
                        TeamCreatedAt = Timestamp.AddYears(-1)
                    }
                ],
                OrganizationAssignments =
                [
                    new() { OrganizationId = "org-1", CostCenterId = "cc-org" }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal("cc-direct", result.Attribution!.CostCenterId);
        Assert.Equal(AttributionRule.DirectUser, result.Attribution.Rule);
    }

    [Fact]
    public void EarliestCreatedTeamWinsWhenSeveralTeamsApply()
    {
        var scenario = RichScenario() with
        {
            Attribution = new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1"],
                TeamAssignments =
                [
                    new()
                    {
                        TeamId = "newer",
                        CostCenterId = "cc-newer",
                        TeamCreatedAt = Timestamp.AddMonths(-1)
                    },
                    new()
                    {
                        TeamId = "older",
                        CostCenterId = "cc-older",
                        TeamCreatedAt = Timestamp.AddYears(-1)
                    }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal("cc-older", result.Attribution!.CostCenterId);
        Assert.Equal(AttributionRule.EnterpriseTeam, result.Attribution.Rule);
    }

    [Fact]
    public void MultipleLicensingOrganizationsWithoutCycleSelectionAreIndeterminate()
    {
        var scenario = RichScenario() with
        {
            Attribution = new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1", "org-2"]
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Indeterminate, result.Decision);
        Assert.Equal("attribution", result.FirstFailingGate);
    }

    [Fact]
    public void IndividualUlbOverridesCostCenterAndUniversalBudgets()
    {
        var scenario = RichScenario() with
        {
            EconomicGuardrails = Economy(
                userBudgets:
                [
                    new()
                    {
                        Id = "universal",
                        Kind = UserLevelBudgetKind.Universal,
                        LimitCredits = 1_000m
                    },
                    new()
                    {
                        Id = "cost-center",
                        Kind = UserLevelBudgetKind.CostCenter,
                        TargetId = "cc-1",
                        LimitCredits = 500m
                    },
                    new()
                    {
                        Id = "individual",
                        Kind = UserLevelBudgetKind.Individual,
                        TargetId = "user-1",
                        LimitCredits = 10m
                    }
                ])
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("individual", result.FirstFailingGate);
        Assert.Equal("individual", result.EffectiveUlb!.Id);
    }

    [Fact]
    public void DerivedCostCenterIncludedCapCanBlockOverflow()
    {
        var scenario = RichScenario() with
        {
            BillingContext = Billing(
                new EffectiveSeatAssignment
                {
                    UserId = "user-1",
                    PlanId = "business",
                    CostCenterId = "cc-1"
                },
                new EffectiveSeatAssignment
                {
                    UserId = "user-2",
                    PlanId = "business",
                    CostCenterId = "cc-2"
                }),
            EconomicGuardrails = Economy(
                includedControls:
                [
                    new()
                    {
                        Id = "cc-1-included",
                        CostCenterId = "cc-1",
                        ConsumedCredits = 1_890m,
                        OverflowBehavior = IncludedOverflowBehavior.Block
                    }
                ])
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("cc-1-included", result.FirstFailingGate);
        var control = Assert.Single(result.AppliedGuardrails, x => x.Id == "cc-1-included");
        Assert.Equal(1_900m, control.Limit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingSeatsCannotInflateSharedEntitlement(bool useCostCenterControl)
    {
        var includedControls = useCostCenterControl
            ? new CostCenterIncludedUsageControl[]
            {
                new()
                {
                    Id = "cc-1-included",
                    CostCenterId = "cc-1",
                    ConsumedCredits = 3_790m,
                    OverflowBehavior = IncludedOverflowBehavior.Block
                }
            }
            : [];
        var scenario = RichScenario() with
        {
            BillingContext = Billing(
                new EffectiveSeatAssignment
                {
                    UserId = "user-1",
                    PlanId = "business",
                    CostCenterId = "cc-1"
                },
                new EffectiveSeatAssignment
                {
                    UserId = "user-2",
                    PlanId = "business",
                    CostCenterId = "cc-1",
                    EffectiveTo = Timestamp.AddHours(1)
                },
                new EffectiveSeatAssignment
                {
                    UserId = "USER-2",
                    PlanId = "business",
                    CostCenterId = "cc-1",
                    EffectiveFrom = Timestamp.AddHours(-1)
                }),
            EconomicGuardrails = Economy(
                poolConsumed: useCostCenterControl ? 0m : 3_790m,
                includedControls: includedControls,
                paidUsage: GuardrailValue.Disabled)
        };

        var exception = Assert.Throws<SimulationException>(() => _engine.Simulate(scenario));

        Assert.Equal(SimulationScenarioValidator.InvalidContractCode, exception.Code);
        Assert.Contains("overlapping effective periods for user 'user-2'", exception.Message);
    }

    [Fact]
    public void LowestHeadroomAcrossCostCenterAndEnterpriseBudgetsBlocks()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "cc-budget",
                Scope = SpendingBudgetScope.CostCenter,
                ScopeId = "cc-1",
                LimitUsd = 1m,
                ConsumedUsd = 0.90m,
                Enforcement = GuardrailEnforcement.HardStop
            },
            new SpendingBudget
            {
                Id = "enterprise-budget",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 1m,
                ConsumedUsd = 0.85m,
                Enforcement = GuardrailEnforcement.HardStop
            });

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("cc-budget", result.FirstFailingGate);
        Assert.Contains(result.AppliedGuardrails, x => x.Id == "enterprise-budget");
        Assert.Equal(0.10m, result.Remaining.SpendingBudgetRemainingUsd["cc-budget"]);
        Assert.Equal(0.15m, result.Remaining.SpendingBudgetRemainingUsd["enterprise-budget"]);
    }

    [Fact]
    public void EnterpriseBudgetCanBeLowerConstraintThanCostCenter()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "cc-budget",
                Scope = SpendingBudgetScope.CostCenter,
                ScopeId = "cc-1",
                LimitUsd = 2m,
                ConsumedUsd = 1m,
                Enforcement = GuardrailEnforcement.HardStop
            },
            new SpendingBudget
            {
                Id = "enterprise-budget",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 1m,
                ConsumedUsd = 0.90m,
                Enforcement = GuardrailEnforcement.HardStop
            });

        var result = _engine.Simulate(scenario);

        Assert.Equal("enterprise-budget", result.FirstFailingGate);
    }

    [Fact]
    public void SpendingBudgetDoesNotApplyBeforeTrackingBaseline()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "tracked-budget",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 0m,
                Enforcement = GuardrailEnforcement.HardStop,
                TrackingStartedAt = Timestamp.AddDays(1)
            });

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.DoesNotContain(result.AppliedGuardrails, guardrail => guardrail.Id == "tracked-budget");
        Assert.DoesNotContain("tracked-budget", result.Remaining.SpendingBudgetRemainingUsd.Keys);
    }

    [Fact]
    public void SpendingBudgetAppliesAtTrackingBaseline()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "tracked-budget",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 0m,
                Enforcement = GuardrailEnforcement.HardStop,
                TrackingStartedAt = Timestamp
            });

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("tracked-budget", result.FirstFailingGate);
        Assert.Equal(0m, result.Remaining.SpendingBudgetRemainingUsd["tracked-budget"]);
    }

    [Fact]
    public void SpendingBudgetTrackingBaselineContinuesIntoLaterCycles()
    {
        var laterTimestamp = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "tracked-budget",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 0m,
                Enforcement = GuardrailEnforcement.HardStop,
                TrackingStartedAt = Timestamp
            });
        scenario = scenario with
        {
            Timestamp = laterTimestamp,
            BillingContext = scenario.BillingContext! with
            {
                CycleStart = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                CycleEnd = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("tracked-budget", result.FirstFailingGate);
    }

    [Fact]
    public void CostCenterExclusionRemovesEnterpriseBudgetConstraint()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "cc-budget",
                Scope = SpendingBudgetScope.CostCenter,
                ScopeId = "cc-1",
                LimitUsd = 2m,
                ConsumedUsd = 1m,
                Enforcement = GuardrailEnforcement.HardStop
            },
            new SpendingBudget
            {
                Id = "enterprise-budget",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 1m,
                ConsumedUsd = 0.90m,
                Enforcement = GuardrailEnforcement.HardStop
            }) with
        {
            EconomicGuardrails = MeteredEconomy(
                excludedCostCenters: new HashSet<string> { "cc-1" },
                new SpendingBudget
                {
                    Id = "cc-budget",
                    Scope = SpendingBudgetScope.CostCenter,
                    ScopeId = "cc-1",
                    LimitUsd = 2m,
                    ConsumedUsd = 1m,
                    Enforcement = GuardrailEnforcement.HardStop
                },
                new SpendingBudget
                {
                    Id = "enterprise-budget",
                    Scope = SpendingBudgetScope.Enterprise,
                    LimitUsd = 1m,
                    ConsumedUsd = 0.90m,
                    Enforcement = GuardrailEnforcement.HardStop
                })
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.DoesNotContain(result.AppliedGuardrails, x => x.Id == "enterprise-budget");
    }

    [Fact]
    public void UnknownPaidUsageAuthorizationIsIndeterminate()
    {
        var scenario = RichScenario() with
        {
            EconomicGuardrails = Economy(
                poolConsumed: 1_900m,
                paidUsage: GuardrailValue.Unknown,
                spendingBudgets:
                [
                    new SpendingBudget
                    {
                        Id = "enterprise-budget",
                        Scope = SpendingBudgetScope.Enterprise,
                        LimitUsd = 1m,
                        ConsumedUsd = 0.25m
                    }
                ])
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Indeterminate, result.Decision);
        Assert.Equal("paid-usage.unknown", result.FirstFailingGate);
        Assert.Equal(0.75m, result.Remaining.SpendingBudgetRemainingUsd["enterprise-budget"]);
    }

    [Fact]
    public void EmitsEveryCrossedSpendingBudgetThreshold()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "enterprise-alert",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 0.20m,
                ConsumedUsd = 0.14m,
                Enforcement = GuardrailEnforcement.AlertOnly
            });

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal([75m, 90m, 100m], result.Alerts.Select(x => x.ThresholdPercent));
    }

    [Fact]
    public void CliCreditLimitSoftStopsBeforeEconomicAllocation()
    {
        var scenario = RichScenario() with
        {
            EconomicGuardrails = MeteredEconomy(
                new HashSet<string>(),
                new SpendingBudget
                {
                    Id = "enterprise-budget",
                    Scope = SpendingBudgetScope.Enterprise,
                    LimitUsd = 1m,
                    ConsumedUsd = 0.25m
                }),
            RuntimeGuardrails = new RuntimeGuardrailSnapshot
            {
                CliSoftCreditLimit = 10m,
                CliCreditsConsumed = 0m
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.SoftStopped, result.Decision);
        Assert.Equal("runtime.cli-soft-credits", result.FirstFailingGate);
        Assert.Equal(0.75m, result.Remaining.SpendingBudgetRemainingUsd["enterprise-budget"]);
    }

    [Fact]
    public void MissingWorkflowApprovalReturnsWaiting()
    {
        var scenario = RichScenario(operation: "code-review") with
        {
            ActionsUsage = new ActionsUsageInput
            {
                RunnerId = "linux-2-core",
                Minutes = 5m
            },
            ActionsGuardrails = new ActionsGuardrailSnapshot
            {
                ActionsEnabled = GuardrailValue.Enabled,
                RunnerAvailable = GuardrailValue.Enabled,
                WorkflowApproved = GuardrailValue.Disabled,
                RepositoryRulesPermitRun = GuardrailValue.Enabled,
                Budgets =
                [
                    new ActionsSpendingBudget
                    {
                        Id = "actions-budget",
                        LimitUsd = 1m,
                        ConsumedUsd = 0.25m
                    }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Waiting, result.Decision);
        Assert.Equal("actions.workflow-approval", result.FirstFailingGate);
        Assert.Equal(0.75m, result.Remaining.ActionsBudgetRemainingUsd["actions-budget"]);
    }

    [Fact]
    public void MissingCallsReturnUnchangedApplicableBudgetBalances()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "enterprise-budget",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 1m,
                ConsumedUsd = 0.25m
            }) with
        {
            Calls = []
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.PartiallySimulated, result.Decision);
        Assert.Equal(0.75m, result.Remaining.SpendingBudgetRemainingUsd["enterprise-budget"]);
    }

    [Fact]
    public void EconomicBudgetFailurePrecedesActionsBudgetFailure()
    {
        var scenario = RichScenario(operation: "code-review") with
        {
            EconomicGuardrails = MeteredEconomy(
                new HashSet<string>(),
                new SpendingBudget
                {
                    Id = "economic-budget",
                    Scope = SpendingBudgetScope.Enterprise,
                    LimitUsd = 0m,
                    Enforcement = GuardrailEnforcement.HardStop
                }),
            ActionsUsage = new ActionsUsageInput
            {
                RunnerId = "linux-2-core",
                Minutes = 5m
            },
            ActionsGuardrails = new ActionsGuardrailSnapshot
            {
                ActionsEnabled = GuardrailValue.Enabled,
                RunnerAvailable = GuardrailValue.Enabled,
                WorkflowApproved = GuardrailValue.Enabled,
                RepositoryRulesPermitRun = GuardrailValue.Enabled,
                Budgets =
                [
                    new ActionsSpendingBudget
                    {
                        Id = "actions-budget",
                        LimitUsd = 0m,
                        Enforcement = GuardrailEnforcement.HardStop
                    }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("economic-budget", result.FirstFailingGate);
        Assert.Contains(result.AppliedGuardrails, guardrail => guardrail.Id == "economic-budget");
        Assert.DoesNotContain(result.AppliedGuardrails, guardrail => guardrail.Id == "actions-budget");
    }

    [Fact]
    public void OrganizationBudgetAppliesWhenAttributedCostCenterHasNoBudget()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "organization-budget",
                Scope = SpendingBudgetScope.Organization,
                ScopeId = "org-1",
                LimitUsd = 1m,
                ConsumedUsd = 0.90m,
                Enforcement = GuardrailEnforcement.HardStop
            });

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("organization-budget", result.FirstFailingGate);
    }

    [Fact]
    public void DirectAssignmentDoesNotHideUnresolvedLicensingOrganization()
    {
        var scenario = RichScenario() with
        {
            Attribution = new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1", "org-2"],
                DirectAssignments = [new() { CostCenterId = "cc-1" }]
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Indeterminate, result.Decision);
        Assert.Equal(AttributionRule.UnresolvedMultipleLicensingOrganizations, result.Attribution!.Rule);
    }

    [Fact]
    public void NonPooledSeatsDoNotContributeToEnterprisePool()
    {
        var scenario = RichScenario() with
        {
            PlanId = "pro",
            BillingContext = Billing(
                new EffectiveSeatAssignment
                {
                    UserId = "user-1",
                    PlanId = "pro",
                    CostCenterId = "cc-1"
                }),
            EconomicGuardrails = Economy(paidUsage: GuardrailValue.Disabled)
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("paid-usage", result.FirstFailingGate);
        Assert.Equal(0m, result.Remaining.IncludedPoolCredits);
    }

    [Fact]
    public void TimestampOutsideBillingCycleIsIndeterminate()
    {
        var scenario = RichScenario() with { Timestamp = Timestamp.AddMonths(1) };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Indeterminate, result.Decision);
        Assert.Equal("billing-cycle.timestamp", result.FirstFailingGate);
    }

    [Fact]
    public void RejectedChargeDoesNotEmitBudgetAlertsOrConsumeBalances()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "hard-budget",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 0.20m,
                ConsumedUsd = 0.14m,
                Enforcement = GuardrailEnforcement.HardStop
            });

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Empty(result.Alerts);
        Assert.Equal(0m, result.Allocation.IncludedCredits);
        Assert.Equal(0m, result.Allocation.MeteredCredits);
        Assert.Equal(0m, result.Allocation.MeteredUsd);
        Assert.Equal(0m, result.Remaining.IncludedPoolCredits);
    }

    [Fact]
    public void RequestedDurationIsIncludedInRuntimeLimit()
    {
        var scenario = RichScenario() with
        {
            RuntimeGuardrails = new RuntimeGuardrailSnapshot
            {
                MaximumDuration = TimeSpan.FromMinutes(10),
                ElapsedDuration = TimeSpan.FromMinutes(8),
                RequestedDuration = TimeSpan.FromMinutes(3)
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("runtime.duration", result.FirstFailingGate);
    }

    [Fact]
    public void ActionsDenialLeavesAiCreditAndActionsBalancesUnchanged()
    {
        var scenario = RichScenario(operation: "code-review") with
        {
            EconomicGuardrails = Economy(poolConsumed: 100m),
            ActionsUsage = new ActionsUsageInput
            {
                RunnerId = "linux-2-core",
                Minutes = 5m
            },
            ActionsGuardrails = new ActionsGuardrailSnapshot
            {
                ActionsEnabled = GuardrailValue.Enabled,
                RunnerAvailable = GuardrailValue.Enabled,
                WorkflowApproved = GuardrailValue.Disabled,
                RepositoryRulesPermitRun = GuardrailValue.Enabled,
                IncludedMinutes = 100m,
                ConsumedIncludedMinutes = 20m
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Waiting, result.Decision);
        Assert.Equal(1_800m, result.Remaining.IncludedPoolCredits);
        Assert.Equal(80m, result.Remaining.ActionsIncludedMinutes);
        Assert.Equal(0m, result.Allocation.TotalCredits);
    }

    [Fact]
    public void CostRelatedScopeSkipsOperationalAndAccessChecks()
    {
        var scenario = RichScenario(operation: "cloud-agent") with
        {
            CheckScope = SimulationCheckScope.CostRelatedOnly,
            AccessGates = new Dictionary<string, AccessGateState>
            {
                ["policy"] = new() { Passed = false }
            },
            RuntimeGuardrails = new RuntimeGuardrailSnapshot
            {
                MaximumModelCalls = 0,
                MaximumDuration = TimeSpan.Zero,
                RequestedDuration = TimeSpan.FromMinutes(1)
            },
            ActionsUsage = new ActionsUsageInput
            {
                RunnerId = "linux-2-core",
                Minutes = 5m
            },
            ActionsGuardrails = new ActionsGuardrailSnapshot
            {
                ActionsEnabled = GuardrailValue.Disabled,
                RunnerAvailable = GuardrailValue.Disabled,
                WorkflowApproved = GuardrailValue.Disabled,
                RepositoryRulesPermitRun = GuardrailValue.Disabled,
                IncludedMinutes = 100m
            }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.DoesNotContain(result.AppliedGuardrails, x => x.Category == "runtime");
        Assert.DoesNotContain(result.AppliedGuardrails, x => x.Category == "actions-access");
        Assert.Contains(result.AppliedGuardrails, x => x.Category == "included-pool");
    }

    [Fact]
    public void CostRelatedScopeStillEnforcesSpendingBudgets()
    {
        var scenario = MeteredScenario(
            new SpendingBudget
            {
                Id = "cost-limit",
                Scope = SpendingBudgetScope.Enterprise,
                LimitUsd = 0.01m,
                Enforcement = GuardrailEnforcement.HardStop
            }) with
        {
            CheckScope = SimulationCheckScope.CostRelatedOnly,
            RuntimeGuardrails = new RuntimeGuardrailSnapshot { MaximumModelCalls = 0 }
        };

        var result = _engine.Simulate(scenario);

        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal("cost-limit", result.FirstFailingGate);
    }

    private static SimulationScenario MeteredScenario(params SpendingBudget[] budgets) =>
        RichScenario() with
        {
            EconomicGuardrails = MeteredEconomy(new HashSet<string>(), budgets)
        };

    private static EconomicGuardrailSnapshot MeteredEconomy(
        IReadOnlySet<string> excludedCostCenters,
        params SpendingBudget[] budgets) =>
        Economy(
            poolConsumed: 1_900m,
            paidUsage: GuardrailValue.Enabled,
            spendingBudgets: budgets,
            excludedCostCenters: excludedCostCenters);

    private static SimulationScenario RichScenario(string operation = "chat") =>
        new()
        {
            OperationId = operation,
            PlanId = "business",
            ProductId = "github-copilot",
            SkuId = "copilot-ai-credits",
            Timestamp = Timestamp,
            Calls =
            [
                new ModelCallInput
                {
                    ModelId = "gpt-5.6-luna",
                    ContextTokens = 1_000,
                    FreshInputTokens = 1_000_000
                }
            ],
            BillingContext = Billing(
                new EffectiveSeatAssignment
                {
                    UserId = "user-1",
                    PlanId = "business",
                    CostCenterId = "cc-1"
                }),
            Attribution = new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1"],
                DirectAssignments = [new() { CostCenterId = "cc-1" }]
            },
            EconomicGuardrails = Economy()
        };

    private static BillingContext Billing(params EffectiveSeatAssignment[] seats) =>
        new()
        {
            BillingEntityId = "enterprise-1",
            CycleStart = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            CycleEnd = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            SeatAssignments = seats
        };

    private static EconomicGuardrailSnapshot Economy(
        IReadOnlyList<UserLevelBudget>? userBudgets = null,
        decimal poolConsumed = 0m,
        IReadOnlyList<CostCenterIncludedUsageControl>? includedControls = null,
        GuardrailValue paidUsage = GuardrailValue.Enabled,
        IReadOnlyList<SpendingBudget>? spendingBudgets = null,
        IReadOnlySet<string>? excludedCostCenters = null) =>
        new()
        {
            UserLevelBudgets = userBudgets ?? [],
            EnterprisePoolConsumedCredits = poolConsumed,
            IncludedUsageControls = includedControls ?? [],
            PaidUsage = new PaidUsageAuthorization { State = paidUsage },
            SpendingBudgets = spendingBudgets ?? [],
            EnterpriseBudgetExcludedCostCenterIds = excludedCostCenters ?? new HashSet<string>()
        };
}
