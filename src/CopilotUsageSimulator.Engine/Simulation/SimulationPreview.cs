namespace CopilotUsageSimulator.Engine.Simulation;

public sealed record SimulationPreview
{
    public SimulationResult? Result { get; init; }
    public RemainingState? InitialRemaining { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> Assumptions { get; init; } = [];
    public required string CatalogVersion { get; init; }
    public string? ReferenceSnapshotId { get; init; }
    public DateOnly? ReferenceVerifiedOn { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public decimal? RequiredAiCredits => Result?.CostRequirement.AiCredits;
    public decimal? RequiredModelUsd => Result?.CostRequirement.ModelUsd;
    public decimal? ProposedIncludedCredits => Result?.CostRequirement.IncludedCredits;
    public decimal? ProposedMeteredCredits => Result?.CostRequirement.MeteredCredits;
    public decimal? ProposedAiUsd => Result?.CostRequirement.AiUsd;
    public decimal? ProposedActionsUsd => Result?.CostRequirement.ActionsUsd;
    public decimal? ProposedTotalUsd => Result?.CostRequirement.TotalUsd;
    public decimal? AcceptedAiCredits => Result is null ? null :
        Result.Decision == SimulationDecision.Allowed ? Result.Allocation.TotalCredits : 0m;
    public decimal? AcceptedAiUsd => Result is null ? null :
        Result.Decision == SimulationDecision.Allowed ? Result.Allocation.MeteredUsd : 0m;
    public decimal? AcceptedActionsUsd => Result is null ? null :
        Result.Decision == SimulationDecision.Allowed ? Result.ActionsUsage?.AdditionalUsd ?? 0m : 0m;
    public decimal? AcceptedTotalUsd => AcceptedAiUsd + AcceptedActionsUsd;
}

public sealed record SimulationComparison
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required SimulationScenario CandidateScenario { get; init; }
    public required SimulationPreview Baseline { get; init; }
    public required SimulationPreview Candidate { get; init; }
    public decimal? UsdDelta => Candidate.ProposedTotalUsd - Baseline.ProposedTotalUsd;
    public bool Resolved => Baseline.ErrorCode is null && Candidate.ErrorCode is null &&
        Baseline.Result is { Decision: not SimulationDecision.Allowed } &&
        Candidate.Result is { Decision: SimulationDecision.Allowed };
}
