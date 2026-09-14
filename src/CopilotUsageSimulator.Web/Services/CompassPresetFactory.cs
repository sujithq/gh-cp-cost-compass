using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public static class CompassPresetFactory
{
    public static readonly DateTimeOffset SimulationDate = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<CompassPreset> Presets { get; } =
    [
        new("chat", "Copilot Chat", "Interactive question & answer", "chat", "AI credits"),
        new("cloud-agent", "Cloud agent", "Private repo, standard runner", "agent", "AI + Actions"),
        new("cli", "Copilot CLI", "Local terminal workflow", "cli", "AI credits")
    ];

    public static SimulationScenario Create(
        EngineConfiguration configuration,
        string operationId,
        CompassProblem problem = CompassProblem.Standard)
    {
        if (!Presets.Any(preset => preset.OperationId == operationId))
        {
            throw new ConfigurationException($"'{operationId}' is not a supported Compass preset.");
        }

        var original = ExampleScenarioFactory.Create(configuration, operationId, simulationTimestamp: SimulationDate);
        var calls = operationId switch
        {
            "cloud-agent" => new[]
            {
                Call(15_000, 6_000, 5_000),
                Call(15_000, 6_000, 5_000),
                Call(15_000, 6_000, 5_000)
            },
            "cli" => [Call(12_000, 4_000, 8_000)],
            _ => [Call(50_000, 40_000)]
        };
        var scenario = original with
        {
            PlanId = "business",
            CheckScope = SimulationCheckScope.All,
            Calls = calls,
            Metadata = new Dictionary<string, string>
            {
                ["task"] = operationId switch
                {
                    "cloud-agent" => "Implement a focused change across three modeled calls; open a pull request.",
                    "cli" => "Inspect a local repository and explain a failing test.",
                    _ => "Explain a large code path and propose a detailed implementation."
                },
                ["compassPreset"] = operationId,
                ["compassProblem"] = problem.ToString(),
                ["actionsAccounting"] = "Pre-accounted, per-job rounded minutes on a private standard Linux 2-core runner.",
                ["actionsAccount"] = "demo-repository-owner",
                ["billingCohort"] = "Standard September allowance; not a historical promotional cohort."
            },
            BillingContext = original.BillingContext! with
            {
                SeatAssignments =
                [
                    new EffectiveSeatAssignment { UserId = "user-1", PlanId = "business", CostCenterId = "cc-engineering" },
                    new EffectiveSeatAssignment { UserId = "user-2", PlanId = "enterprise", CostCenterId = "cc-engineering" }
                ]
            },
            EconomicGuardrails = original.EconomicGuardrails! with
            {
                EnterprisePoolConsumedCredits = 5_700m,
                PaidUsage = original.EconomicGuardrails.PaidUsage with { State = GuardrailValue.Enabled }
            },
            ActionsUsage = original.ActionsUsage is null ? null : original.ActionsUsage with
            {
                Minutes = 18m,
                IncludedMinutesRemaining = 10m
            },
            ActionsGuardrails = original.ActionsGuardrails is null ? null : original.ActionsGuardrails with
            {
                IncludedMinutes = 10m,
                ConsumedIncludedMinutes = 0m
            }
        };

        var economic = scenario.EconomicGuardrails!;
        return problem switch
        {
            CompassProblem.PaidUsageDisabled => scenario with
            {
                Calls = [Call(50_000, 40_000)],
                EconomicGuardrails = economic with
                {
                    EnterprisePoolConsumedCredits = 5_740m,
                    PaidUsage = economic.PaidUsage with { State = GuardrailValue.Disabled }
                }
            },
            CompassProblem.AiBudget => scenario with
            {
                Calls = [Call(50_000, 40_000)],
                EconomicGuardrails = economic with
                {
                    EnterprisePoolConsumedCredits = 5_740m,
                    SpendingBudgets = economic.SpendingBudgets.Select(budget =>
                        budget.Scope == SpendingBudgetScope.CostCenter
                            ? budget with { LimitUsd = .20m, ConsumedUsd = 0m }
                            : budget).ToArray()
                }
            },
            CompassProblem.UserBudget => scenario with
            {
                EconomicGuardrails = economic with
                {
                    UserLevelBudgets = economic.UserLevelBudgets.Select(budget =>
                        budget.Kind == UserLevelBudgetKind.Individual && budget.TargetId == "user-1"
                            ? budget with { LimitCredits = 101m, ConsumedCredits = 100m }
                            : budget).ToArray()
                }
            },
            CompassProblem.ActionsBudget when scenario.ActionsGuardrails is not null => scenario with
            {
                ActionsGuardrails = scenario.ActionsGuardrails with
                {
                    Budgets = scenario.ActionsGuardrails.Budgets.Select(budget =>
                        budget with { LimitUsd = 0m, ConsumedUsd = 0m }).ToArray()
                }
            },
            CompassProblem.ActionsBudget => throw new ConfigurationException("The Actions budget demo requires the cloud-agent preset."),
            _ => scenario
        };
    }

    private static ModelCallInput Call(long input, long output, long cached = 0) => new()
    {
        ModelId = "gpt-5.6-sol",
        ContextTokens = 90_000,
        FreshInputTokens = input,
        CachedInputTokens = cached,
        OutputTokens = output
    };
}

public sealed record CompassPreset(string OperationId, string Title, string Description, string Icon, string Meter);

public enum CompassProblem
{
    Standard,
    PaidUsageDisabled,
    AiBudget,
    UserBudget,
    ActionsBudget
}
