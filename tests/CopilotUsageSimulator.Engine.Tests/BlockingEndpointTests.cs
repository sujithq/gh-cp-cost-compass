using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class BlockingEndpointTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly CopilotUsageSimulationEngine _engine =
        new(EngineConfigurationLoader.LoadDefault());

    public static IEnumerable<object[]> CatalogGateEndpoints() =>
        EngineConfigurationLoader.LoadDefault().Gates.Select(gate => new object[] { gate.Id });

    [Theory]
    [MemberData(nameof(CatalogGateEndpoints))]
    public void EveryCatalogAccessGateCanBlock(string gateId)
    {
        var scenario = RichScenario(operation: "cloud-agent") with
        {
            AccessGates = new Dictionary<string, AccessGateState>
            {
                [gateId] = new() { Passed = false }
            }
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(result, SimulationDecision.Blocked, gateId);
        Assert.Empty(result.Calls);
    }

    [Theory]
    [InlineData("runtime.model-calls")]
    [InlineData("runtime.subagent-depth")]
    [InlineData("runtime.duration")]
    public void EveryRuntimeHardLimitCanBlock(string gateId)
    {
        var guardrails = gateId switch
        {
            "runtime.model-calls" => new RuntimeGuardrailSnapshot
            {
                MaximumModelCalls = 1,
                ModelCallsConsumed = 1
            },
            "runtime.subagent-depth" => new RuntimeGuardrailSnapshot
            {
                MaximumSubagentDepth = 1,
                RequestedSubagentDepth = 2
            },
            "runtime.duration" => new RuntimeGuardrailSnapshot
            {
                MaximumDuration = TimeSpan.FromMinutes(10),
                ElapsedDuration = TimeSpan.FromMinutes(8),
                RequestedDuration = TimeSpan.FromMinutes(3)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(gateId))
        };

        var result = _engine.Simulate(RichScenario() with { RuntimeGuardrails = guardrails });

        AssertTerminal(result, SimulationDecision.Blocked, gateId);
        Assert.Empty(result.Calls);
    }

    [Fact]
    public void CliCreditLimitCanSoftStop()
    {
        var scenario = RichScenario() with
        {
            RuntimeGuardrails = new RuntimeGuardrailSnapshot
            {
                CliSoftCreditLimit = 10m
            }
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(result, SimulationDecision.SoftStopped, "runtime.cli-soft-credits");
        Assert.NotEmpty(result.Calls);
        Assert.Equal(0m, result.Allocation.TotalCredits);
    }

    [Theory]
    [InlineData("actions.enabled", GuardrailValue.Disabled, SimulationDecision.Blocked)]
    [InlineData("actions.enabled", GuardrailValue.Unknown, SimulationDecision.Indeterminate)]
    [InlineData("actions.runner-available", GuardrailValue.Disabled, SimulationDecision.Blocked)]
    [InlineData("actions.runner-available", GuardrailValue.Unknown, SimulationDecision.Indeterminate)]
    [InlineData("actions.workflow-approval", GuardrailValue.Disabled, SimulationDecision.Waiting)]
    [InlineData("actions.workflow-approval", GuardrailValue.Unknown, SimulationDecision.Indeterminate)]
    [InlineData("actions.repository-rules", GuardrailValue.Disabled, SimulationDecision.Blocked)]
    [InlineData("actions.repository-rules", GuardrailValue.Unknown, SimulationDecision.Indeterminate)]
    public void EveryActionsAccessStateCanStop(
        string gateId,
        GuardrailValue value,
        SimulationDecision expectedDecision)
    {
        var guardrails = EnabledActions() with
        {
            ActionsEnabled = gateId == "actions.enabled" ? value : GuardrailValue.Enabled,
            RunnerAvailable = gateId == "actions.runner-available" ? value : GuardrailValue.Enabled,
            WorkflowApproved = gateId == "actions.workflow-approval" ? value : GuardrailValue.Enabled,
            RepositoryRulesPermitRun =
                gateId == "actions.repository-rules" ? value : GuardrailValue.Enabled
        };

        var result = _engine.Simulate(ActionsScenario(guardrails));

        AssertTerminal(result, expectedDecision, gateId);
        Assert.Empty(result.Calls);
        Assert.Equal(0m, result.Allocation.TotalCredits);
    }

    [Fact]
    public void ActionsSpendingBudgetCanBlock()
    {
        var guardrails = EnabledActions() with
        {
            Budgets =
            [
                new ActionsSpendingBudget
                {
                    Id = "actions-hard-stop",
                    LimitUsd = 0.01m,
                    ConsumedUsd = 0.01m,
                    Enforcement = GuardrailEnforcement.HardStop
                }
            ]
        };

        var result = _engine.Simulate(ActionsScenario(guardrails));

        AssertTerminal(result, SimulationDecision.Blocked, "actions-hard-stop");
        Assert.NotNull(result.ActionsUsage);
        Assert.Equal(0m, result.Allocation.TotalCredits);
        Assert.Empty(result.Alerts);
        Assert.Equal(0m, result.Remaining.ActionsBudgetRemainingUsd["actions-hard-stop"]);
    }

    [Fact]
    public void AllowedActionsProjectEveryBudgetBalance()
    {
        var guardrails = EnabledActions() with
        {
            Budgets =
            [
                new ActionsSpendingBudget
                {
                    Id = "actions-alert",
                    LimitUsd = 1m,
                    ConsumedUsd = 0.20m,
                    Enforcement = GuardrailEnforcement.AlertOnly
                },
                new ActionsSpendingBudget
                {
                    Id = "actions-hard-stop",
                    LimitUsd = 2m,
                    ConsumedUsd = 0.50m,
                    Enforcement = GuardrailEnforcement.HardStop
                }
            ]
        };

        var result = _engine.Simulate(ActionsScenario(guardrails));

        Assert.Equal(SimulationDecision.Allowed, result.Decision);
        Assert.Equal(0.74m, result.Remaining.ActionsBudgetRemainingUsd["actions-alert"]);
        Assert.Equal(1.44m, result.Remaining.ActionsBudgetRemainingUsd["actions-hard-stop"]);
    }

    [Theory]
    [InlineData("invalid-cycle-selection")]
    [InlineData("missing-cycle-selection")]
    [InlineData("duplicate-direct-assignment")]
    [InlineData("duplicate-organization-assignment")]
    public void EveryAmbiguousAttributionPathIsIndeterminate(string path)
    {
        var attribution = path switch
        {
            "invalid-cycle-selection" => new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1"],
                CycleSelectedLicensingOrganizationId = "org-other"
            },
            "missing-cycle-selection" => new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1", "org-2"]
            },
            "duplicate-direct-assignment" => new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1"],
                DirectAssignments =
                [
                    new() { CostCenterId = "cc-1" },
                    new() { CostCenterId = "cc-2" }
                ]
            },
            "duplicate-organization-assignment" => new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1"],
                OrganizationAssignments =
                [
                    new() { OrganizationId = "org-1", CostCenterId = "cc-1" },
                    new() { OrganizationId = "org-1", CostCenterId = "cc-2" }
                ]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };

        var result = _engine.Simulate(RichScenario() with { Attribution = attribution });

        AssertTerminal(result, SimulationDecision.Indeterminate, "attribution");
        Assert.NotNull(result.Attribution);
        Assert.Equal(GuardrailOutcome.Indeterminate, result.Attribution.Outcome);
    }

    [Fact]
    public void BillingTimestampOutsideCycleIsIndeterminate()
    {
        var result = _engine.Simulate(RichScenario() with
        {
            Timestamp = Timestamp.AddMonths(1)
        });

        AssertTerminal(result, SimulationDecision.Indeterminate, "billing-cycle.timestamp");
    }

    [Fact]
    public void UnknownPlanInPoolSeatInventoryIsIndeterminate()
    {
        var result = _engine.Simulate(RichScenario() with
        {
            BillingContext = Billing(new EffectiveSeatAssignment
            {
                UserId = "user-1",
                PlanId = "unknown-plan",
                CostCenterId = "cc-1"
            })
        });

        AssertTerminal(result, SimulationDecision.Indeterminate, "pool.seat-inventory");
    }

    [Fact]
    public void UnknownPlanInControlledCostCenterInventoryIsIndeterminate()
    {
        var scenario = RichScenario() with
        {
            BillingContext = Billing(new EffectiveSeatAssignment
            {
                UserId = "user-1",
                PlanId = "unknown-plan",
                CostCenterId = "cc-1"
            }),
            EconomicGuardrails = Economy() with
            {
                IncludedUsageControls =
                [
                    new CostCenterIncludedUsageControl
                    {
                        Id = "cc-control",
                        CostCenterId = "cc-1"
                    }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(
            result,
            SimulationDecision.Indeterminate,
            "included-control.seat-inventory");
    }

    [Fact]
    public void MultipleEffectiveUlbsAreIndeterminate()
    {
        var scenario = RichScenario() with
        {
            EconomicGuardrails = Economy() with
            {
                UserLevelBudgets =
                [
                    new() { Id = "universal-1", Kind = UserLevelBudgetKind.Universal },
                    new() { Id = "universal-2", Kind = UserLevelBudgetKind.Universal }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(result, SimulationDecision.Indeterminate, "ulb.ambiguous");
    }

    [Theory]
    [InlineData(UserLevelBudgetKind.Universal, null)]
    [InlineData(UserLevelBudgetKind.CostCenter, "cc-1")]
    [InlineData(UserLevelBudgetKind.Individual, "user-1")]
    public void EveryUlbScopeCanBlock(UserLevelBudgetKind kind, string? targetId)
    {
        var id = $"ulb-{kind.ToString().ToLowerInvariant()}";
        var scenario = RichScenario() with
        {
            EconomicGuardrails = Economy() with
            {
                UserLevelBudgets =
                [
                    new()
                    {
                        Id = id,
                        Kind = kind,
                        TargetId = targetId,
                        LimitCredits = 10m
                    }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(result, SimulationDecision.Blocked, id);
        Assert.Equal(kind, result.EffectiveUlb!.Kind);
    }

    [Fact]
    public void MultipleEffectiveIncludedControlsAreIndeterminate()
    {
        var scenario = RichScenario() with
        {
            EconomicGuardrails = Economy() with
            {
                IncludedUsageControls =
                [
                    new() { Id = "control-1", CostCenterId = "cc-1" },
                    new() { Id = "control-2", CostCenterId = "cc-1" }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(result, SimulationDecision.Indeterminate, "included-control.ambiguous");
    }

    [Fact]
    public void IncludedControlCanBlock()
    {
        var scenario = RichScenario() with
        {
            EconomicGuardrails = Economy() with
            {
                IncludedUsageControls =
                [
                    new()
                    {
                        Id = "included-hard-stop",
                        CostCenterId = "cc-1",
                        ConsumedCredits = 1_890m,
                        OverflowBehavior = IncludedOverflowBehavior.Block
                    }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(result, SimulationDecision.Blocked, "included-hard-stop");
    }

    [Fact]
    public void PaidUsageProductMismatchCanBlock()
    {
        var scenario = MeteredRichScenario() with
        {
            EconomicGuardrails = Economy(poolConsumed: 1_900m) with
            {
                PaidUsage = new PaidUsageAuthorization
                {
                    State = GuardrailValue.Enabled,
                    ProductIds = new HashSet<string> { "other-product" }
                }
            }
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(result, SimulationDecision.Blocked, "paid-usage.not-applicable");
    }

    [Theory]
    [InlineData(GuardrailValue.Unknown, SimulationDecision.Indeterminate, "paid-usage.unknown")]
    [InlineData(GuardrailValue.Disabled, SimulationDecision.Blocked, "paid-usage")]
    public void PaidUsageAuthorizationCanStop(
        GuardrailValue value,
        SimulationDecision expectedDecision,
        string gateId)
    {
        var scenario = MeteredRichScenario() with
        {
            EconomicGuardrails = Economy(poolConsumed: 1_900m, paidUsage: value)
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(result, expectedDecision, gateId);
    }

    [Theory]
    [InlineData(SpendingBudgetScope.CostCenter, "cc-1")]
    [InlineData(SpendingBudgetScope.Organization, "org-1")]
    [InlineData(SpendingBudgetScope.Enterprise, null)]
    public void EveryRichSpendingBudgetScopeCanBlock(
        SpendingBudgetScope scope,
        string? scopeId)
    {
        var id = $"rich-{scope.ToString().ToLowerInvariant()}-budget";
        var scenario = MeteredRichScenario() with
        {
            EconomicGuardrails = Economy(poolConsumed: 1_900m) with
            {
                SpendingBudgets =
                [
                    new()
                    {
                        Id = id,
                        Scope = scope,
                        ScopeId = scopeId,
                        LimitUsd = 0.01m,
                        Enforcement = GuardrailEnforcement.HardStop
                    }
                ]
            }
        };

        var result = _engine.Simulate(scenario);

        AssertTerminal(result, SimulationDecision.Blocked, id);
    }

    private static void AssertTerminal(
        SimulationResult result,
        SimulationDecision expectedDecision,
        string gateId)
    {
        Assert.Equal(expectedDecision, result.Decision);
        Assert.Equal(gateId, result.FirstFailingGate);
    }

    private static SimulationScenario ActionsScenario(ActionsGuardrailSnapshot guardrails) =>
        RichScenario(operation: "code-review") with
        {
            ActionsUsage = new ActionsUsageInput
            {
                RunnerId = "linux-2-core",
                Minutes = 10m
            },
            ActionsGuardrails = guardrails
        };

    private static ActionsGuardrailSnapshot EnabledActions() =>
        new()
        {
            ActionsEnabled = GuardrailValue.Enabled,
            RunnerAvailable = GuardrailValue.Enabled,
            WorkflowApproved = GuardrailValue.Enabled,
            RepositoryRulesPermitRun = GuardrailValue.Enabled
        };

    private static SimulationScenario MeteredRichScenario() =>
        RichScenario() with
        {
            EconomicGuardrails = Economy(poolConsumed: 1_900m)
        };

    private static SimulationScenario RichScenario(string operation = "chat") =>
        new()
        {
            OperationId = operation,
            PlanId = "business",
            ProductId = "github-copilot",
            SkuId = "copilot-ai-credits",
            Timestamp = Timestamp,
            Calls = [Call()],
            BillingContext = Billing(new EffectiveSeatAssignment
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

    private static ModelCallInput Call() =>
        new()
        {
            ModelId = "gpt-5.6-luna",
            ContextTokens = 1_000,
            FreshInputTokens = 1_000_000
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
        decimal poolConsumed = 0m,
        GuardrailValue paidUsage = GuardrailValue.Enabled) =>
        new()
        {
            EnterprisePoolConsumedCredits = poolConsumed,
            PaidUsage = new PaidUsageAuthorization { State = paidUsage }
        };
}
