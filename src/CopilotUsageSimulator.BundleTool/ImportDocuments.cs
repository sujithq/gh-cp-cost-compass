using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.BundleTool;

public sealed record EnterpriseSnapshot
{
    public const string Format = "compass-enterprise-snapshot";
    public required string Schema { get; init; }
    public required int SchemaVersion { get; init; }
    public required string Enterprise { get; init; }
    public required string SelectedUser { get; init; }
    public string? ApiVersion { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required DateTimeOffset CaptureCompletedAt { get; init; }
    public required IReadOnlyList<SourceCoverage> Sources { get; init; }
    public required IReadOnlyList<SeatGrant> Seats { get; init; }
    public required IReadOnlyList<CostCenterObservation> CostCenters { get; init; }
    public required IReadOnlyList<TeamObservation> Teams { get; init; }
    public required IReadOnlyList<BudgetObservation> Budgets { get; init; }
    public required IReadOnlyList<UserBudgetObservation> UserBudgetStates { get; init; }
    public UserBudgetObservation? EffectiveUserBudget { get; init; }
    public IReadOnlyList<UsageObservation> UsageReports { get; init; } = [];
}

public sealed record SourceCoverage
{
    public required string Dataset { get; init; }
    public required string Endpoint { get; init; }
    public required bool Complete { get; init; }
    public required int Pages { get; init; }
    public int? HttpStatus { get; init; }
    public string? Problem { get; init; }
}

public sealed record SeatGrant
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public string? PlanType { get; init; }
    public string? Organization { get; init; }
    public string? AssigningTeam { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateOnly? PendingCancellationDate { get; init; }
}

public sealed record CostCenterObservation
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string State { get; init; }
    public required IReadOnlyList<CostCenterResource> Resources { get; init; }
    public bool? AiCreditPoolEnabled { get; init; }
    public decimal? TargetAmount { get; init; }
    public decimal? CurrentAmount { get; init; }
}

public sealed record CostCenterResource
{
    public required string Type { get; init; }
    public required string Name { get; init; }
}

public sealed record TeamObservation
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<string> Members { get; init; }
}

public sealed record BudgetObservation
{
    public required string Id { get; init; }
    public required string Scope { get; init; }
    public required string BudgetType { get; init; }
    public required string ProductSku { get; init; }
    public string? EntityName { get; init; }
    public string? User { get; init; }
    public decimal? BudgetAmount { get; init; }
    public decimal? ConsumedAmount { get; init; }
    public bool? PreventFurtherUsage { get; init; }
    public bool? WillAlert { get; init; }
    public DateOnly? ExpiresAt { get; init; }
}

public sealed record UserBudgetObservation
{
    public required string BudgetId { get; init; }
    public required string User { get; init; }
    public decimal? ConsumedAmount { get; init; }
    public decimal? TargetAmount { get; init; }
    public string? OverrideBudgetId { get; init; }
}

public sealed record UsageObservation
{
    public required string Kind { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public string? User { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
    public DateTimeOffset? ReportedThrough { get; init; }
    public required IReadOnlyList<UsageItemObservation> Items { get; init; }
}

public sealed record UsageItemObservation
{
    public required string Product { get; init; }
    public required string Sku { get; init; }
    public required string UnitType { get; init; }
    public string? Model { get; init; }
    public decimal? PricePerUnit { get; init; }
    public decimal? GrossQuantity { get; init; }
    public decimal? GrossAmount { get; init; }
    public decimal? DiscountQuantity { get; init; }
    public decimal? DiscountAmount { get; init; }
    public decimal? NetQuantity { get; init; }
    public decimal? NetAmount { get; init; }
}

public sealed record WorkloadInput
{
    public required int SchemaVersion { get; init; }
    public required string OperationId { get; init; }
    public required IReadOnlyList<ModelCallInput> Calls { get; init; }
    public SimulationCheckScope CheckScope { get; init; } = SimulationCheckScope.CostRelatedOnly;
    public IReadOnlyDictionary<string, AccessGateState> AccessGates { get; init; } =
        new Dictionary<string, AccessGateState>();
    public RuntimeGuardrailSnapshot? RuntimeGuardrails { get; init; }
    public ActionsConfirmation? Actions { get; init; }
}

public sealed record ActionsConfirmation
{
    public required string Account { get; init; }
    public required decimal Minutes { get; init; }
    public required decimal IncludedMinutesRemaining { get; init; }
    public required IReadOnlyList<string> ApplicableBudgetIds { get; init; }
    public required bool ApplicableBudgetsConfirmed { get; init; }
    public GuardrailValue ActionsEnabled { get; init; } = GuardrailValue.Unknown;
    public GuardrailValue RunnerAvailable { get; init; } = GuardrailValue.Unknown;
    public GuardrailValue WorkflowApproved { get; init; } = GuardrailValue.Unknown;
    public GuardrailValue RepositoryRulesPermitRun { get; init; } = GuardrailValue.Unknown;
}

public sealed record ImportOverrides
{
    public required int SchemaVersion { get; init; }
    public decimal? EnterprisePoolConsumedCredits { get; init; }
    public decimal? ExpectedPoolEntitlementCredits { get; init; }
    public IReadOnlyList<string>? EnterpriseBudgetExcludedCostCenterIds { get; init; }
    public PaidUsageAuthorization? PaidUsage { get; init; }
    public IReadOnlyDictionary<string, SeatConfirmation> Seats { get; init; } =
        new Dictionary<string, SeatConfirmation>();
    public IReadOnlyDictionary<string, IncludedControlConfirmation> IncludedControls { get; init; } =
        new Dictionary<string, IncludedControlConfirmation>();
    public IReadOnlyDictionary<string, BudgetConfirmation> Budgets { get; init; } =
        new Dictionary<string, BudgetConfirmation>();
}

public sealed record SeatConfirmation
{
    public string? PlanId { get; init; }
    public string? LicensingOrganization { get; init; }
}

public sealed record IncludedControlConfirmation
{
    public bool? Enabled { get; init; }
    public decimal? EntitlementCredits { get; init; }
    public decimal? ConsumedCredits { get; init; }
    public IncludedOverflowBehavior? OverflowBehavior { get; init; }
}

public sealed record BudgetConfirmation
{
    public decimal? LimitUsd { get; init; }
    public decimal? ConsumedUsd { get; init; }
    public GuardrailEnforcement? Enforcement { get; init; }
    public DateTimeOffset? TrackingStartedAt { get; init; }
}

public sealed record ImportDiagnostic(string Severity, string Code, string Path, string Message);

public sealed record ImportReport
{
    public string Schema { get; init; } = "compass-import-report";
    public int SchemaVersion { get; init; } = 1;
    public required string Command { get; init; }
    public string? Enterprise { get; init; }
    public string? User { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public string? PreviewDecision { get; init; }
    public string? FirstFailingGate { get; init; }
    public string? CatalogSha256 { get; init; }
    public ImportOverrides? Confirmations { get; init; }
    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }
}

public sealed class ImportException(string code, string message, int exitCode = 4, int? httpStatus = null) : Exception(message)
{
    public string Code { get; } = code;
    public int ExitCode { get; } = exitCode;
    public int? HttpStatus { get; } = httpStatus;
}