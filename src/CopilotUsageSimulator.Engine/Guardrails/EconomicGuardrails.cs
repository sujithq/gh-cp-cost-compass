namespace CopilotUsageSimulator.Engine.Guardrails;

public sealed record EconomicGuardrailSnapshot
{
    public IReadOnlyList<UserLevelBudget> UserLevelBudgets { get; init; } = [];
    public decimal EnterprisePoolConsumedCredits { get; init; }
    public IReadOnlyList<CostCenterIncludedUsageControl> IncludedUsageControls { get; init; } = [];
    public PaidUsageAuthorization PaidUsage { get; init; } = new();
    public IReadOnlyList<SpendingBudget> SpendingBudgets { get; init; } = [];
    public IReadOnlySet<string> EnterpriseBudgetExcludedCostCenterIds { get; init; } =
        new HashSet<string>();
}

public sealed record UserLevelBudget
{
    public required string Id { get; init; }
    public UserLevelBudgetKind Kind { get; init; }
    public string? TargetId { get; init; }
    public decimal LimitCredits { get; init; }
    public decimal ConsumedCredits { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset? EffectiveTo { get; init; }
}

public enum UserLevelBudgetKind
{
    Universal,
    CostCenter,
    Individual
}

public sealed record CostCenterIncludedUsageControl
{
    public required string Id { get; init; }
    public required string CostCenterId { get; init; }
    public decimal ConsumedCredits { get; init; }
    public IncludedOverflowBehavior OverflowBehavior { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset? EffectiveTo { get; init; }
}

public enum IncludedOverflowBehavior
{
    Block,
    PaidUsage
}

public sealed record PaidUsageAuthorization
{
    public GuardrailValue State { get; init; } = GuardrailValue.Unknown;
    public IReadOnlySet<string> ProductIds { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> SkuIds { get; init; } = new HashSet<string>();
}

public sealed record SpendingBudget
{
    public required string Id { get; init; }
    public SpendingBudgetScope Scope { get; init; }
    public string? ScopeId { get; init; }
    public decimal LimitUsd { get; init; }
    public decimal ConsumedUsd { get; init; }
    public GuardrailEnforcement Enforcement { get; init; } = GuardrailEnforcement.AlertOnly;
    public IReadOnlySet<string> ProductIds { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> SkuIds { get; init; } = new HashSet<string>();
    public DateTimeOffset EffectiveFrom { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset? EffectiveTo { get; init; }
    public DateTimeOffset? TrackingStartedAt { get; init; }
}

public enum SpendingBudgetScope
{
    Enterprise,
    Organization,
    CostCenter
}
