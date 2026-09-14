using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Configuration;

namespace CopilotUsageSimulator.Engine.Simulation;

internal sealed class SimulationPipelineContext(
    SimulationScenario scenario,
    EconomicBalanceCalculator balances,
    EngineConfiguration configuration)
{
    public SimulationTraceRecorder Trace { get; } = new(configuration, scenario);
    public List<ExplanationEntry> Explanation { get; } = [];
    public List<string> Assumptions { get; } = [];
    public List<AppliedGuardrail> AppliedGuardrails { get; } = [];
    public List<ThresholdEvent> Alerts { get; } = [];
    public IReadOnlyList<ModelCallCharge> Calls { get; set; } = [];
    public CreditAllocation Allocation { get; set; } = new();
    public SimulationCostRequirement CostRequirement { get; set; } = new();
    public ActionsUsageResult? ActionsUsage { get; set; }
    public AttributionResult? Attribution { get; set; }
    public EffectiveUserLevelBudgetResult? EffectiveUlb { get; set; }
    public RemainingState? Remaining { get; set; }

    public SimulationResult Complete(
        SimulationDecision decision,
        string? failingGate = null) =>
        new()
        {
            Decision = decision,
            FirstFailingGate = failingGate,
            Calls = Calls,
            Allocation = decision == SimulationDecision.Allowed ? Allocation : new(),
            ActionsUsage = ActionsUsage,
            Attribution = Attribution,
            EffectiveUlb = decision == SimulationDecision.Allowed || EffectiveUlb is null
                ? EffectiveUlb
                : EffectiveUlb with
                {
                    ReservedCredits = 0m,
                    RemainingCredits = EffectiveUlb.LimitCredits - EffectiveUlb.ConsumedBeforeCredits
                },
            AppliedGuardrails = AppliedGuardrails.ToArray(),
            Trace = Trace.Complete(Attribution),
            CostRequirement = CostRequirement,
            Alerts = decision == SimulationDecision.Allowed ? Alerts.ToArray() : [],
            Remaining = Remaining ?? balances.CreateUnchangedRemaining(scenario, Attribution),
            Assumptions = Assumptions.ToArray(),
            Explanation = Explanation.ToArray()
        };
}
