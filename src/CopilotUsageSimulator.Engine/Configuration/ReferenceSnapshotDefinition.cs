namespace CopilotUsageSimulator.Engine.Configuration;

public sealed record ReferenceSnapshotDefinition
{
    public required string Id { get; init; }
    public required string CatalogVersion { get; init; }
    public required DateOnly VerifiedOn { get; init; }
    public IReadOnlyList<ReferenceSourceDefinition> Sources { get; init; } = [];
    public IReadOnlyList<string> Assumptions { get; init; } = [];
}

public sealed record ReferenceSourceDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required DateOnly VerifiedOn { get; init; }
    public DateOnly? PublishedOn { get; init; }
}

public sealed record ModelAvailabilityDefinition
{
    public DateTimeOffset? AvailableFrom { get; init; }
    public DateTimeOffset? RetiredAt { get; init; }

    // Coverage of positive availability evidence, not an inferred release date.
    public DateTimeOffset? VerifiedFrom { get; init; }
    public IReadOnlyList<string>? EligiblePlanIds { get; init; }
    public bool EligiblePlansAreExhaustive { get; init; }
    public IReadOnlyList<string>? AutoModelSelectionOperationIds { get; init; }
    public IReadOnlyList<string> SourceIds { get; init; } = [];
    public string? Notes { get; init; }
}

public enum TokenComponent
{
    FreshInput,
    CachedInput,
    CacheWrite,
    Output
}
