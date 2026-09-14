using CopilotUsageSimulator.Common.Documentation;

namespace CopilotUsageSimulator.Common.Guardrails;

public static class GuardrailCategories
{
    public const string Runtime = "runtime";
    public const string UserLevelBudget = "user-level-budget";
    public const string IncludedUsageControl = "included-usage-control";
    public const string IncludedPool = "included-pool";
    public const string MeteredSpendingBudget = "metered-spending-budget";
    public const string PaidUsageAuthorization = "paid-usage-authorization";
    public const string ActionsBudget = "actions-budget";
    public const string ActionsAccess = "actions-access";
}

public static class GuardrailMetadataKeys
{
    public const string Unknown = "unknown";
    public const string RuntimeModelCalls = "runtime.model-calls";
    public const string RuntimeSubagentDepth = "runtime.subagent-depth";
    public const string RuntimeDuration = "runtime.duration";
    public const string RuntimeCliCredits = "runtime.cli-soft-credits";
    public const string UlbUniversal = "ulb.universal";
    public const string UlbCostCenter = "ulb.cost-center";
    public const string UlbIndividual = "ulb.individual";
    public const string IncludedUsageControl = GuardrailCategories.IncludedUsageControl;
    public const string IncludedPool = GuardrailCategories.IncludedPool;
    public const string PaidUsage = "paid-usage";
    public const string MeteredBudgetCostCenter = "metered-budget.cost-center";
    public const string MeteredBudgetOrganization = "metered-budget.organization";
    public const string MeteredBudgetEnterprise = "metered-budget.enterprise";
    public const string ActionsBudget = GuardrailCategories.ActionsBudget;
    public const string ActionsEnabled = "actions.enabled";
    public const string ActionsRunnerAvailable = "actions.runner-available";
    public const string ActionsWorkflowApproval = "actions.workflow-approval";
    public const string ActionsRepositoryRules = "actions.repository-rules";
}

public sealed record GuardrailMetadata(
    string Key,
    string Category,
    string Label,
    bool IsCostRelated,
    string SettingsAnchor,
    string? DocumentationUrl,
    string Unit,
    string BlockingExplanation);

public static class GuardrailMetadataCatalog
{
    public const string ScenarioJsonAnchor = "scenario-json";

    private static readonly GuardrailMetadata Unknown = new(
        GuardrailMetadataKeys.Unknown,
        "",
        "Policy guardrail",
        false,
        ScenarioJsonAnchor,
        null,
        "",
        "The first failing check below is the guardrail that stopped the simulation.");

    private static readonly IReadOnlyDictionary<string, GuardrailMetadata> ByKey =
        new[]
        {
            Runtime(GuardrailMetadataKeys.RuntimeModelCalls, "Agent model-call limit", "calls"),
            Runtime(GuardrailMetadataKeys.RuntimeSubagentDepth, "Agent subagent-depth limit", "levels"),
            Runtime(GuardrailMetadataKeys.RuntimeDuration, "Agent runtime duration limit", "min"),
            Runtime(GuardrailMetadataKeys.RuntimeCliCredits, "CLI session credit limit", "credits"),
            Cost(GuardrailMetadataKeys.UlbUniversal, GuardrailCategories.UserLevelBudget, "Universal ULB", "universal-ulb-settings", GitHubDocumentationLinks.CopilotBudgets, "credits",
                "The effective ULB for this user stopped the run. Cost-center controls and spending budgets are separate, later checks."),
            Cost(GuardrailMetadataKeys.UlbCostCenter, GuardrailCategories.UserLevelBudget, "Cost-center ULB", "cost-center-ulb-settings", GitHubDocumentationLinks.CopilotBudgets, "credits",
                "The effective ULB for this user stopped the run. Cost-center controls and spending budgets are separate, later checks."),
            Cost(GuardrailMetadataKeys.UlbIndividual, GuardrailCategories.UserLevelBudget, "Individual ULB", "individual-ulb-settings", GitHubDocumentationLinks.CopilotBudgets, "credits",
                "The effective ULB for this user stopped the run. Cost-center controls and spending budgets are separate, later checks."),
            Cost(GuardrailMetadataKeys.IncludedUsageControl, GuardrailCategories.IncludedUsageControl, "Cost-center included-usage control", "included-control-settings", GitHubDocumentationLinks.CopilotBudgets, "credits",
                "The attributed cost center stopped included-credit overflow. This is not an individual ULB."),
            Cost(GuardrailMetadataKeys.IncludedPool, GuardrailCategories.IncludedPool, "Enterprise included-credit pool", "pool-settings", GitHubDocumentationLinks.UsageBasedBilling, "credits",
                "The enterprise shared included-credit pool could not cover the allocation."),
            Cost(GuardrailMetadataKeys.PaidUsage, GuardrailCategories.PaidUsageAuthorization, "Paid-usage authorization", "paid-usage-settings", GitHubDocumentationLinks.CopilotBudgets, "credits",
                "Included credits were insufficient and paid usage was unavailable or not authorized."),
            Cost(GuardrailMetadataKeys.MeteredBudgetCostCenter, GuardrailCategories.MeteredSpendingBudget, "Cost-center budget", "cost-center-budget-settings", GitHubDocumentationLinks.MeteredBudgets, "USD",
                "A hard-stop spending budget could not cover the metered charge."),
            Cost(GuardrailMetadataKeys.MeteredBudgetOrganization, GuardrailCategories.MeteredSpendingBudget, "Organization budget", "organization-budget-settings", GitHubDocumentationLinks.MeteredBudgets, "USD",
                "A hard-stop spending budget could not cover the metered charge."),
            Cost(GuardrailMetadataKeys.MeteredBudgetEnterprise, GuardrailCategories.MeteredSpendingBudget, "Enterprise budget", "enterprise-budget-settings", GitHubDocumentationLinks.MeteredBudgets, "USD",
                "A hard-stop spending budget could not cover the metered charge."),
            Cost(GuardrailMetadataKeys.ActionsBudget, GuardrailCategories.ActionsBudget, "Actions budget", "actions-budget-settings", GitHubDocumentationLinks.MeteredBudgets, "USD",
                "The GitHub Actions budget stopped the runner charge; Copilot AI-credit budgets are separate."),
            ActionsAccess(GuardrailMetadataKeys.ActionsEnabled, "GitHub Actions access policy"),
            ActionsAccess(GuardrailMetadataKeys.ActionsRunnerAvailable, "GitHub Actions runner availability"),
            ActionsAccess(GuardrailMetadataKeys.ActionsWorkflowApproval, "GitHub Actions workflow approval"),
            ActionsAccess(GuardrailMetadataKeys.ActionsRepositoryRules, "GitHub Actions repository policy")
        }.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<GuardrailMetadata> All => ByKey.Values.ToArray();

    public static GuardrailMetadata Resolve(
        string? metadataKey,
        string? guardrailId = null,
        string? category = null)
    {
        if (!string.IsNullOrWhiteSpace(metadataKey) && ByKey.TryGetValue(metadataKey, out var metadata))
        {
            return metadata;
        }

        if (!string.IsNullOrWhiteSpace(guardrailId) && ByKey.TryGetValue(guardrailId, out metadata))
        {
            return metadata;
        }

        return ByKey.Values.FirstOrDefault(item =>
                   string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
               ?? Unknown with
               {
                   Category = category ?? "",
                   Label = Friendly(guardrailId)
               };
    }

    private static GuardrailMetadata Runtime(string key, string label, string unit) => new(
        key,
        GuardrailCategories.Runtime,
        label,
        false,
        "runtime-settings",
        null,
        unit,
        "This is an agent runtime guardrail, not a cost-center, ULB, or spending-budget block. Later economic checks were not reached.");

    private static GuardrailMetadata Cost(
        string key,
        string category,
        string label,
        string anchor,
        string documentationUrl,
        string unit,
        string explanation) =>
        new(key, category, label, true, anchor, documentationUrl, unit, explanation);

    private static GuardrailMetadata ActionsAccess(string key, string label) => new(
        key,
        GuardrailCategories.ActionsAccess,
        label,
        false,
        ScenarioJsonAnchor,
        GitHubDocumentationLinks.ActionsSpending,
        "",
        "A GitHub Actions access or approval policy stopped the run before economic guardrails.");

    private static string Friendly(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? Unknown.Label
            : id.Replace(".", " ").Replace("-", " ");
}
