namespace CopilotUsageSimulator.Engine.Guardrails;

public sealed record RuntimeGuardrailSnapshot
{
    public int? MaximumModelCalls { get; init; }
    public int ModelCallsConsumed { get; init; }
    public int? MaximumSubagentDepth { get; init; }
    public int RequestedSubagentDepth { get; init; }
    public TimeSpan? MaximumDuration { get; init; }
    public TimeSpan ElapsedDuration { get; init; }
    public TimeSpan RequestedDuration { get; init; }
    public decimal? CliSoftCreditLimit { get; init; }
    public decimal CliCreditsConsumed { get; init; }
}

public sealed record ActionsGuardrailSnapshot
{
    public GuardrailValue ActionsEnabled { get; init; } = GuardrailValue.Unknown;
    public GuardrailValue RunnerAvailable { get; init; } = GuardrailValue.Unknown;
    public GuardrailValue WorkflowApproved { get; init; } = GuardrailValue.Unknown;
    public GuardrailValue RepositoryRulesPermitRun { get; init; } = GuardrailValue.Unknown;
    public decimal IncludedMinutes { get; init; }
    public decimal ConsumedIncludedMinutes { get; init; }
    public IReadOnlyList<ActionsSpendingBudget> Budgets { get; init; } = [];
}

public sealed record ActionsSpendingBudget
{
    public required string Id { get; init; }
    public decimal LimitUsd { get; init; }
    public decimal ConsumedUsd { get; init; }
    public GuardrailEnforcement Enforcement { get; init; } = GuardrailEnforcement.AlertOnly;
}
