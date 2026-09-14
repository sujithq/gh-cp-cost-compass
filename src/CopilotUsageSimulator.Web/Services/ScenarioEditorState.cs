using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Web.Services;

public sealed class ScenarioEditorState
{
    public WorkloadEditorState Workload { get; init; } = new();
    public AttributionEditorState Attribution { get; init; } = new();
    public EconomicEditorState Economic { get; init; } = new();
    public RuntimeEditorState Runtime { get; init; } = new();
    public ActionsEditorState Actions { get; init; } = new();
}

public sealed class WorkloadEditorState
{
    public string Task { get; set; } = "";
    public string OperationId { get; set; } = "cloud-agent";
    public string PlanId { get; set; } = "business";
    public bool CostChecksOnly { get; set; } = true;
    public RepositoryVisibility RepositoryVisibility { get; set; } = RepositoryVisibility.Private;
    public string ModelId { get; set; } = "gpt-5.6-luna";
    public long ContextTokens { get; set; }
    public long FreshInputTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public long OutputTokens { get; set; }
    public int RepeatCount { get; set; } = 1;
}

public sealed class AttributionEditorState
{
    public string CostCenterId { get; set; } = "";
    public int? DirectAssignmentIndex { get; set; }
    public string OrganizationId { get; set; } = "";
    public int? LicensingOrganizationIndex { get; set; }
}

public sealed class EconomicEditorState
{
    public decimal PoolConsumed { get; set; }
    public bool UseUniversalUlb { get; set; }
    public string? UniversalUlbId { get; set; }
    public decimal UniversalUlbLimit { get; set; }
    public decimal UniversalUlbConsumed { get; set; }
    public bool UseCostCenterUlb { get; set; }
    public string? CostCenterUlbId { get; set; }
    public decimal CostCenterUlbLimit { get; set; }
    public decimal CostCenterUlbConsumed { get; set; }
    public bool UseIndividualUlb { get; set; }
    public string? IndividualUlbId { get; set; }
    public decimal UlbLimit { get; set; }
    public decimal UlbConsumed { get; set; }
    public bool UseIncludedControl { get; set; }
    public string? IncludedControlId { get; set; }
    public decimal IncludedControlConsumed { get; set; }
    public IncludedOverflowBehavior IncludedOverflow { get; set; }
    public GuardrailValue PaidUsage { get; set; }
    public string PaidUsageProductIds { get; set; } = "";
    public string PaidUsageSkuIds { get; set; } = "";
    public bool UseCostCenterBudget { get; set; }
    public string? CostCenterBudgetId { get; set; }
    public decimal CostCenterBudgetLimit { get; set; }
    public decimal CostCenterBudgetConsumed { get; set; }
    public GuardrailEnforcement CostCenterBudgetEnforcement { get; set; }
    public bool UseOrganizationBudget { get; set; }
    public string? OrganizationBudgetId { get; set; }
    public decimal OrganizationBudgetLimit { get; set; }
    public decimal OrganizationBudgetConsumed { get; set; }
    public GuardrailEnforcement OrganizationBudgetEnforcement { get; set; }
    public bool UseEnterpriseBudget { get; set; }
    public string? EnterpriseBudgetId { get; set; }
    public decimal EnterpriseBudgetLimit { get; set; }
    public decimal EnterpriseBudgetConsumed { get; set; }
    public GuardrailEnforcement EnterpriseBudgetEnforcement { get; set; }
}

public sealed class RuntimeEditorState
{
    public bool Enabled { get; set; }
    public int? MaximumModelCalls { get; set; }
    public int ModelCallsConsumed { get; set; }
    public int? MaximumSubagentDepth { get; set; }
    public int RequestedSubagentDepth { get; set; }
    public decimal? MaximumDurationMinutes { get; set; }
    public decimal ElapsedDurationMinutes { get; set; }
    public decimal RequestedDurationMinutes { get; set; }
    public decimal? CliSoftCreditLimit { get; set; }
    public decimal CliCreditsConsumed { get; set; }
}

public sealed class ActionsEditorState
{
    public decimal Minutes { get; set; }
    public bool UseBudget { get; set; }
    public string? BudgetId { get; set; }
    public decimal BudgetLimit { get; set; }
    public decimal BudgetConsumed { get; set; }
    public GuardrailEnforcement BudgetEnforcement { get; set; }
}
