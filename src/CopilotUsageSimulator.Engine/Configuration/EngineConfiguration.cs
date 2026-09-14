namespace CopilotUsageSimulator.Engine.Configuration;

public sealed record EngineConfiguration
{
    public string Version { get; init; } = "unspecified";
    public ReferenceSnapshotDefinition? ReferenceSnapshot { get; init; }
    public decimal UsdPerCredit { get; init; } = 0.01m;
    public PoolOverflowBehavior PoolOverflowBehavior { get; init; } = PoolOverflowBehavior.Split;
    public IReadOnlyList<PlanDefinition> Plans { get; init; } = [];
    public IReadOnlyList<ModelDefinition> Models { get; init; } = [];
    public IReadOnlyList<OperationDefinition> Operations { get; init; } = [];
    public IReadOnlyList<GateDefinition> Gates { get; init; } = [];
    public IReadOnlyList<MultiplierDefinition> Multipliers { get; init; } = [];
    public IReadOnlyList<ActionsRunnerDefinition> ActionsRunners { get; init; } = [];
    public ExampleScenarioDefinition ExampleScenario { get; init; } = new();
}

public sealed record ExampleScenarioDefinition
{
    public string ProductId { get; init; } = "github-copilot";
    public string SkuId { get; init; } = "copilot-ai-credits";
    public string? PreferredOperationId { get; init; }
    public string? PreferredPlanId { get; init; }
    public string? PreferredModelId { get; init; }
    public string? PreferredActionsRunnerId { get; init; }
}

public sealed record PlanDefinition
{
    public required string Id { get; init; }
    public IReadOnlyList<PlanAllowancePeriod> AllowancePeriods { get; init; } = [];
    public bool IsPooled { get; init; }
}

public sealed record PlanAllowancePeriod
{
    public required DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    public decimal? IncludedCreditsPerUser { get; init; }
}

public sealed record ModelDefinition
{
    public required string Id { get; init; }
    public string? DisplayName { get; init; }
    public ModelAvailabilityDefinition? Availability { get; init; }
    public IReadOnlySet<TokenComponent>? SupportedTokenComponents { get; init; }
    public IReadOnlyList<string>? SourceIds { get; init; }
    public IReadOnlyList<ModelPricePeriod> PricePeriods { get; init; } = [];
}

public sealed record ModelPricePeriod
{
    public required DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    public IReadOnlyList<string>? SourceIds { get; init; }
    public IReadOnlyList<TokenPriceTier> Tiers { get; init; } = [];
}

public enum PoolOverflowBehavior
{
    Split,
    MeterEntireRequest
}

public sealed record TokenPriceTier
{
    public required string Id { get; init; }
    public long? MinimumContextTokensExclusive { get; init; }
    public long? MaximumContextTokensInclusive { get; init; }
    public decimal InputUsdPerMillion { get; init; }
    public decimal CachedInputUsdPerMillion { get; init; }
    public decimal CacheWriteUsdPerMillion { get; init; }
    public decimal OutputUsdPerMillion { get; init; }
}

public sealed record OperationDefinition
{
    public required string Id { get; init; }
    public bool IsBilled { get; init; } = true;
    public ActionsMeteringMode ActionsMetering { get; init; }
    public string? ExampleLabel { get; init; }
    public string? ExampleTask { get; init; }
}

public enum ActionsMeteringMode
{
    None,
    PrivateRepositories,
    Always
}

public sealed record GateDefinition
{
    public required string Id { get; init; }
    public int Sequence { get; init; }
    public IReadOnlySet<string> ApplicableOperationIds { get; init; } = new HashSet<string>();
    public bool PassWhenUnspecified { get; init; } = true;
}

public sealed record MultiplierDefinition
{
    public required string Id { get; init; }
    public decimal Factor { get; init; } = 1m;
    public IReadOnlySet<string> ApplicableOperationIds { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> ApplicableModelIds { get; init; } = new HashSet<string>();
    public IReadOnlySet<string>? ApplicablePlanIds { get; init; }
    public bool? IsReferenceSupported { get; init; }
    public IReadOnlyList<string>? SourceIds { get; init; }
}

public sealed record ActionsRunnerDefinition
{
    public required string Id { get; init; }
    public decimal UsdPerMinute { get; init; }
}
