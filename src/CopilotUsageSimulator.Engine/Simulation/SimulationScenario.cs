using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.Engine.Simulation;

public sealed record SimulationScenario
{
    public required string OperationId { get; init; }
    public required string PlanId { get; init; }
    public string ProductId { get; init; } = "github-copilot";
    public string SkuId { get; init; } = "copilot-ai-credits";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public SimulationCheckScope CheckScope { get; init; } = SimulationCheckScope.All;
    public RepositoryVisibility RepositoryVisibility { get; init; } = RepositoryVisibility.Private;
    public IReadOnlyList<ModelCallInput> Calls { get; init; } = [];
    public IReadOnlyDictionary<string, AccessGateState> AccessGates { get; init; } =
        new Dictionary<string, AccessGateState>();
    public ActionsUsageInput? ActionsUsage { get; init; }
    public BillingContext? BillingContext { get; init; }
    public AttributionInput? Attribution { get; init; }
    public EconomicGuardrailSnapshot? EconomicGuardrails { get; init; }
    public RuntimeGuardrailSnapshot? RuntimeGuardrails { get; init; }
    public ActionsGuardrailSnapshot? ActionsGuardrails { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

public enum SimulationCheckScope
{
    All,
    CostRelatedOnly
}

public enum RepositoryVisibility
{
    Public,
    Private,
    Internal
}

public sealed record ModelCallInput
{
    public required string ModelId { get; init; }
    public long ContextTokens { get; init; }
    public long FreshInputTokens { get; init; }
    public long CachedInputTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public long OutputTokens { get; init; }
    public IReadOnlyList<string> EnabledMultiplierIds { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

public sealed record AccessGateState
{
    public bool Passed { get; init; }
    public string? Reason { get; init; }
    public string? Remediation { get; init; }
}

public sealed record ActionsUsageInput
{
    public required string RunnerId { get; init; }
    public decimal Minutes { get; init; }
    public decimal IncludedMinutesRemaining { get; init; }
}
