using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.Engine.Simulation;

public sealed record SimulationResult
{
    public SimulationDecision Decision { get; init; }
    public string? FirstFailingGate { get; init; }
    public IReadOnlyList<ModelCallCharge> Calls { get; init; } = [];
    public CreditAllocation Allocation { get; init; } = new();
    public ActionsUsageResult? ActionsUsage { get; init; }
    public AttributionResult? Attribution { get; init; }
    public EffectiveUserLevelBudgetResult? EffectiveUlb { get; init; }
    public IReadOnlyList<AppliedGuardrail> AppliedGuardrails { get; init; } = [];
    public IReadOnlyList<SimulationTraceEntry> Trace { get; init; } = [];
    public SimulationCostRequirement CostRequirement { get; init; } = new();
    public IReadOnlyList<ThresholdEvent> Alerts { get; init; } = [];
    public RemainingState Remaining { get; init; } = new();
    public IReadOnlyList<string> Assumptions { get; init; } = [];
    public IReadOnlyList<ExplanationEntry> Explanation { get; init; } = [];
}

/// <summary>
/// Workload requirements, not accepted consumption. Values stay null until their pricing or
/// allocation stage runs. ModelUsd includes included usage; AiUsd is only additional metered spend.
/// </summary>
public sealed record SimulationCostRequirement
{
    public decimal? AiCredits { get; init; }
    public decimal? ModelUsd { get; init; }
    public decimal? IncludedCredits { get; init; }
    public decimal? MeteredCredits { get; init; }
    public decimal? AiUsd { get; init; }
    public decimal? ActionsUsd { get; init; }
    public decimal? TotalUsd => AiUsd + ActionsUsd;
}

public enum SimulationDecision
{
    Allowed,
    Blocked,
    PartiallySimulated,
    SoftStopped,
    Waiting,
    Indeterminate
}

public sealed record ModelCallCharge
{
    public int CallIndex { get; init; }
    public required string ModelId { get; init; }
    public required string PriceTierId { get; init; }
    public ModelCallPricingEvidence? Pricing { get; init; }
    public decimal FreshInputUsd { get; init; }
    public decimal CachedInputUsd { get; init; }
    public decimal CacheWriteUsd { get; init; }
    public decimal OutputUsd { get; init; }
    public decimal RawUsd { get; init; }
    public decimal AdjustedUsd { get; init; }
    public decimal Credits { get; init; }
    public IReadOnlyList<AppliedMultiplier> AppliedMultipliers { get; init; } = [];
}

/// <summary>
/// The tariff selected by actual call pricing, before multipliers. EffectiveFrom is inclusive
/// and EffectiveTo is exclusive. An explicitly unsupported token component has a null rate.
/// </summary>
public sealed record ModelCallPricingEvidence
{
    public required string PriceTierId { get; init; }
    public required DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    public long? MinimumContextTokensExclusive { get; init; }
    public long? MaximumContextTokensInclusive { get; init; }
    public decimal? InputUsdPerMillion { get; init; }
    public decimal? CachedInputUsdPerMillion { get; init; }
    public decimal? CacheWriteUsdPerMillion { get; init; }
    public decimal? OutputUsdPerMillion { get; init; }
    public IReadOnlyList<string> SourceIds { get; init; } = [];
}

public sealed record AppliedMultiplier
{
    public required string Id { get; init; }
    public decimal Factor { get; init; }
}

public sealed record CreditAllocation
{
    public decimal TotalCredits { get; init; }
    public decimal IncludedCredits { get; init; }
    public string? IncludedUsageControlId { get; init; }
    public decimal MeteredCredits { get; init; }
    public decimal MeteredUsd { get; init; }
    public string? MeteredBudgetId { get; init; }
    public IReadOnlyDictionary<string, decimal> MeteredBudgetRemainingUsd { get; init; } =
        new Dictionary<string, decimal>();
}

public sealed record EffectiveUserLevelBudgetResult
{
    public required string Id { get; init; }
    public UserLevelBudgetKind Kind { get; init; }
    public decimal LimitCredits { get; init; }
    public decimal ConsumedBeforeCredits { get; init; }
    public decimal ReservedCredits { get; init; }
    public decimal RemainingCredits { get; init; }
}

public sealed record ActionsUsageResult
{
    public required string RunnerId { get; init; }
    public decimal TotalMinutes { get; init; }
    public decimal IncludedMinutes { get; init; }
    public decimal BillableMinutes { get; init; }
    public decimal AdditionalUsd { get; init; }
}

public sealed record RemainingState
{
    public decimal IncludedPoolCredits { get; init; }
    public decimal? EffectiveUserBudgetCredits { get; init; }
    public decimal? IncludedUsageControlCredits { get; init; }
    public IReadOnlyDictionary<string, decimal> SpendingBudgetRemainingUsd { get; init; } =
        new Dictionary<string, decimal>();
    public decimal? ActionsIncludedMinutes { get; init; }
    public IReadOnlyDictionary<string, decimal> ActionsBudgetRemainingUsd { get; init; } =
        new Dictionary<string, decimal>();
}

public sealed record ExplanationEntry
{
    public required string Stage { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
}
