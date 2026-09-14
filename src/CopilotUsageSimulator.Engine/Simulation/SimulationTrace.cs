using CopilotUsageSimulator.Common.Guardrails;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.Engine.Simulation;

public enum SimulationTraceState
{
    Passed,
    Blocked,
    Indeterminate,
    Waiting,
    SoftStopped,
    NotApplicable,
    NotEvaluated,
    Excluded
}

public sealed record SimulationTraceEntry
{
    public required string Id { get; init; }
    public int Order { get; init; }
    public required string Stage { get; init; }
    public SimulationTraceState State { get; init; }
    public required string Label { get; init; }
    public required string Message { get; init; }
    public string? MetadataKey { get; init; }
    public string? EntityId { get; init; }
    public string? UserId { get; init; }
    public string? PlanId { get; init; }
    public string? CostCenterId { get; init; }
    public string? LicensingOrganizationId { get; init; }
    public decimal? Limit { get; init; }
    public decimal? ConsumedBefore { get; init; }
    public decimal? Requested { get; init; }
    public decimal? RemainingAfter { get; init; }
}

internal sealed class SimulationTraceRecorder
{
    private readonly SimulationScenario _scenario;
    private readonly List<Section> _sections = [];

    public SimulationTraceRecorder(EngineConfiguration configuration, SimulationScenario scenario)
    {
        _scenario = scenario;
        Add("attribution", "Billing attribution");
        Add("seat-assignment", "Selected user's effective seat");
        Add("seat-inventory", "Pooled seat inventory");
        Add("runtime-preflight", "Runtime limits",
            [GuardrailMetadataKeys.RuntimeModelCalls, GuardrailMetadataKeys.RuntimeSubagentDepth,
                GuardrailMetadataKeys.RuntimeDuration], accessCheck: true);
        Add("actions-access", "Actions access",
            [GuardrailMetadataKeys.ActionsEnabled, GuardrailMetadataKeys.ActionsRunnerAvailable,
                GuardrailMetadataKeys.ActionsWorkflowApproval, GuardrailMetadataKeys.ActionsRepositoryRules],
            accessCheck: true);
        Add("access", "Catalog access",
            configuration.Gates.OrderBy(gate => gate.Sequence).Select(gate => gate.Id),
            accessCheck: true);
        Add("pricing", "Model-call pricing");
        Add("runtime-credits", "CLI credit limit", [GuardrailMetadataKeys.RuntimeCliCredits], accessCheck: true);
        Add("actions-pricing", "Actions runner pricing");
        Add("billing-cycle", "Billing cycle", ["billing-cycle.timestamp"]);
        Add("pool-entitlement", "Pool entitlement");
        Add(GuardrailCategories.UserLevelBudget, "Effective user-level budget");
        Add(GuardrailCategories.IncludedUsageControl, "Cost-center included-usage control");
        Add(GuardrailCategories.IncludedPool, "Enterprise included-credit pool", ["enterprise-shared-pool"]);
        Add(GuardrailCategories.PaidUsageAuthorization, "Paid-usage authorization", ["paid-usage"]);
        Add(GuardrailCategories.MeteredSpendingBudget, "Metered spending budgets");
        Add("actions-budgets", "Actions spending budgets");
    }

    public void Record(
        string stage,
        string id,
        SimulationTraceState state,
        string message,
        string? entityId = null,
        string? label = null,
        decimal? requested = null)
    {
        var section = Get(stage);
        Set(section, new SimulationTraceEntry
        {
            Id = id,
            Stage = stage,
            State = state,
            Label = label ?? GuardrailMetadataCatalog.Resolve(null, id).Label,
            Message = message,
            EntityId = entityId,
            UserId = _scenario.Attribution?.UserId,
            PlanId = _scenario.PlanId,
            Requested = requested
        });
    }

    public void NotApplicable(string stage, string message)
    {
        var section = Get(stage);
        foreach (var entry in section.Entries.ToArray())
        {
            if (entry.State == SimulationTraceState.NotEvaluated)
            {
                Set(section, entry with { State = SimulationTraceState.NotApplicable, Message = message });
            }
        }
    }

    public void Guardrails(
        string stage,
        IReadOnlyList<AppliedGuardrail> applied,
        SimulationDecision decision,
        AttributionResult? attribution)
    {
        foreach (var guardrail in applied)
        {
            AddGuardrail(stage, guardrail, attribution);
        }

        if (decision == SimulationDecision.Allowed)
        {
            NotApplicable(stage, "No applicable configured check was evaluated for this item.");
        }
    }

    public void Economics(EconomicGuardrailEvaluation result, AttributionResult attribution)
    {
        foreach (var guardrail in result.AppliedGuardrails)
        {
            AddGuardrail(guardrail.Category, guardrail, attribution);
        }

        if (result.FailingGuardrailId is { } id &&
            !result.AppliedGuardrails.Any(guardrail => guardrail.Id == id))
        {
            var stage = id switch
            {
                "billing-cycle.timestamp" => "billing-cycle",
                "pool.seat-inventory" => "pool-entitlement",
                "ulb.ambiguous" => GuardrailCategories.UserLevelBudget,
                "included-control.ambiguous" or "included-control.seat-inventory" =>
                    GuardrailCategories.IncludedUsageControl,
                "paid-usage.not-applicable" or "paid-usage.unknown" =>
                    GuardrailCategories.PaidUsageAuthorization,
                _ => GuardrailCategories.MeteredSpendingBudget
            };
            Record(stage, id, State(result.Decision),
                result.Message ?? "This evaluation stopped the request.", attribution.UserId);
        }
    }

    public IReadOnlyList<SimulationTraceEntry> Complete(AttributionResult? attribution) =>
        _sections.SelectMany(section => section.Entries)
            .Select((entry, index) => entry with
            {
                Order = index + 1,
                UserId = attribution?.UserId ?? entry.UserId,
                CostCenterId = attribution?.CostCenterId,
                LicensingOrganizationId = attribution?.LicensingOrganizationId
            }).ToArray();

    public static SimulationTraceState State(SimulationDecision decision) => decision switch
    {
        SimulationDecision.Allowed => SimulationTraceState.Passed,
        SimulationDecision.Blocked => SimulationTraceState.Blocked,
        SimulationDecision.Indeterminate => SimulationTraceState.Indeterminate,
        SimulationDecision.Waiting => SimulationTraceState.Waiting,
        SimulationDecision.SoftStopped => SimulationTraceState.SoftStopped,
        _ => SimulationTraceState.NotEvaluated
    };

    private void AddGuardrail(string stage, AppliedGuardrail guardrail, AttributionResult? attribution)
    {
        var metadata = GuardrailMetadataCatalog.Resolve(
            guardrail.MetadataKey, guardrail.Id, guardrail.Category);
        var entityId = guardrail.Category switch
        {
            GuardrailCategories.UserLevelBudget => attribution?.UserId,
            GuardrailCategories.IncludedUsageControl => attribution?.CostCenterId,
            GuardrailCategories.IncludedPool or "pool-entitlement" => _scenario.BillingContext?.BillingEntityId,
            GuardrailCategories.MeteredSpendingBudget => guardrail.MetadataKey switch
            {
                GuardrailMetadataKeys.MeteredBudgetCostCenter => attribution?.CostCenterId,
                GuardrailMetadataKeys.MeteredBudgetOrganization => attribution?.LicensingOrganizationId,
                _ => _scenario.BillingContext?.BillingEntityId
            },
            GuardrailCategories.ActionsAccess or GuardrailCategories.ActionsBudget =>
                _scenario.ActionsUsage?.RunnerId,
            _ => attribution?.UserId
        };
        Set(Get(stage), new SimulationTraceEntry
        {
            Id = guardrail.Id,
            Stage = stage,
            State = guardrail.Outcome switch
            {
                GuardrailOutcome.Passed => SimulationTraceState.Passed,
                GuardrailOutcome.Blocked => SimulationTraceState.Blocked,
                GuardrailOutcome.Indeterminate => SimulationTraceState.Indeterminate,
                GuardrailOutcome.Waiting => SimulationTraceState.Waiting,
                GuardrailOutcome.SoftStopped => SimulationTraceState.SoftStopped,
                _ => SimulationTraceState.NotApplicable
            },
            Label = metadata.Label,
            Message = guardrail.Message,
            MetadataKey = guardrail.MetadataKey,
            EntityId = entityId,
            UserId = attribution?.UserId,
            PlanId = _scenario.PlanId,
            Limit = guardrail.Limit,
            ConsumedBefore = guardrail.ConsumedBefore,
            Requested = guardrail.Requested,
            RemainingAfter = guardrail.RemainingAfter
        });
    }

    private void Add(string stage, string label, IEnumerable<string>? ids = null, bool accessCheck = false)
    {
        var excluded = accessCheck && _scenario.CheckScope == SimulationCheckScope.CostRelatedOnly;
        var entries = (ids ?? [stage]).Select(id => new SimulationTraceEntry
        {
            Id = id,
            Stage = stage,
            State = excluded ? SimulationTraceState.Excluded : SimulationTraceState.NotEvaluated,
            Label = ids is null ? label : GuardrailMetadataCatalog.Resolve(null, id).Label,
            Message = excluded
                ? "Excluded by cost-related-only scope; access and runtime are not verified."
                : "Not evaluated; the pipeline has not reached this check.",
            PlanId = _scenario.PlanId,
            UserId = _scenario.Attribution?.UserId
        }).ToList();
        _sections.Add(new Section(stage, ids is null || entries.Count == 1, entries));
    }

    private Section Get(string stage) => _sections.Single(section => section.Stage == stage);

    private static void Set(Section section, SimulationTraceEntry entry)
    {
        var index = section.Entries.FindIndex(existing => existing.Id == entry.Id);
        if (index >= 0)
        {
            section.Entries[index] = entry;
            section.IsPlaceholder = false;
        }
        else
        {
            if (section.IsPlaceholder)
            {
                section.Entries.Clear();
                section.IsPlaceholder = false;
            }

            section.Entries.Add(entry);
        }
    }

    private sealed class Section(string stage, bool isPlaceholder, List<SimulationTraceEntry> entries)
    {
        public string Stage { get; } = stage;
        public bool IsPlaceholder { get; set; } = isPlaceholder;
        public List<SimulationTraceEntry> Entries { get; } = entries;
    }
}
