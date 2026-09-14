using System.Globalization;
using System.Text.Json;
using CopilotUsageSimulator.Common.Guardrails;
using CopilotUsageSimulator.Engine;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Reference;
using CopilotUsageSimulator.Engine.Simulation;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace CopilotUsageSimulator.Web.Services;

public sealed class CompassPageModel(
    EngineConfiguration defaultConfiguration,
    ScenarioEditorAdapter editor,
    ScenarioJson scenarioJson,
    CompassBundleCodec bundles,
    CompassBrowserPersistence persistence)
{
    private readonly EngineConfiguration _defaultConfiguration = defaultConfiguration;
    private readonly Dictionary<string, string> _fieldErrors = new(StringComparer.Ordinal);
    private bool? _autoOverride;
    private decimal _actionsAllowance;
    private decimal _actionsConsumed;
    private ModelEligibilityEvaluator? _eligibility;
    private ScenarioEditorState _baselineForm = new();

    public EngineConfiguration Configuration { get; private set; } = defaultConfiguration;
    public SimulationScenario Scenario { get; private set; } =
        CompassPresetFactory.Create(defaultConfiguration, "chat", CompassProblem.PaidUsageDisabled);
    public ScenarioEditorState Form { get; private set; } = new();
    public SimulationPreview? Preview { get; private set; }
    public AppliedGuardrail? BlockingGuardrail => Preview is { ErrorCode: null, Result: { FirstFailingGate: { } gate } result }
        ? result.AppliedGuardrails.FirstOrDefault(guardrail => string.Equals(guardrail.Id, gate, StringComparison.OrdinalIgnoreCase) &&
            guardrail.Outcome is GuardrailOutcome.Blocked or GuardrailOutcome.Indeterminate or GuardrailOutcome.SoftStopped or GuardrailOutcome.Waiting)
        : null;
    public CompassBlockingSetting? BlockingSetting
    {
        get
        {
            if (Preview is not { ErrorCode: null, Result: { FirstFailingGate: { } gate } }) return null;
            var blocker = BlockingGuardrail;
            if (blocker is null && Configuration.Gates.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, gate, StringComparison.OrdinalIgnoreCase)) is { } accessGate)
                return new($"cc-gate-{accessGate.Id}", accessGate.Id, "cc-access-settings");

            var metadata = GuardrailMetadataCatalog.Resolve(blocker?.MetadataKey, gate, blocker?.Category);
            return metadata.Key switch
            {
                GuardrailMetadataKeys.PaidUsage when gate is "paid-usage" or "paid-usage.unknown" =>
                    new("cc-paid", "Paid AI usage"),
                GuardrailMetadataKeys.IncludedPool when gate == "included-pool" =>
                    new("cc-pool-used", "Pooled credits already used"),
                GuardrailMetadataKeys.UlbIndividual when Matches(Form.Economic.IndividualUlbId) && Form.Economic.UseIndividualUlb =>
                    new("cc-ulb-limit", "Individual user limit", "cc-financial-settings"),
                GuardrailMetadataKeys.MeteredBudgetCostCenter when Matches(Form.Economic.CostCenterBudgetId) && Form.Economic.UseCostCenterBudget =>
                    new("cc-budget-limit", "Cost-center AI budget", "cc-financial-settings"),
                GuardrailMetadataKeys.ActionsBudget when Matches(Form.Actions.BudgetId) && Form.Actions.UseBudget && UsesActions =>
                    new("cc-actions-budget", "Actions budget", "cc-actions-settings"),
                _ => new("cc-blocking-setting", metadata.Label, RequiresBundleEditor: true, Unit: metadata.Unit)
            };

            bool Matches(string? recordId) => string.Equals(recordId, gate, StringComparison.OrdinalIgnoreCase);
        }
    }
    public string? BlockingSettingId => BlockingSetting?.Id;
    public IReadOnlyList<SimulationComparison> Comparisons { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public string? EvidenceWarning { get; private set; }
    public string SelectedPreset { get; private set; } = "chat";
    public CompassProblem SelectedProblem { get; private set; } = CompassProblem.PaidUsageDisabled;
    public string SourceLabel { get; private set; } = "Paid usage disabled demo";
    public bool IsCustomized { get; private set; }
    public bool HasRun { get; private set; }
    public bool NeedsSimulation => Preview is null;
    public int FormRevision { get; private set; }
    public bool CanSimulate => _fieldErrors.Count == 0;
    public IReadOnlyDictionary<string, string> FieldErrors => _fieldErrors;
    public string CatalogFingerprint { get; private set; } = "";
    public string ReferenceVerifiedLabel => Configuration.ReferenceSnapshot?.VerifiedOn
        .ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "No verification metadata";
    public IReadOnlyList<ReferenceSourceDefinition> ReferenceSources =>
        Configuration.ReferenceSnapshot?.Sources ?? [];
    public IReadOnlyList<string> ReferenceAssumptions => Configuration.ReferenceSnapshot?.Assumptions ??
        ["This catalog has no verified reference snapshot. Catalog presence alone is not evidence of supported pricing or eligibility."];
    public IReadOnlyList<ModelDefinition> ModelChoices => Eligibility.GetSelectableModels(
        Form.Workload.PlanId, Form.Workload.OperationId, Scenario.Timestamp);
    public string SimulationDateText => Scenario.Timestamp.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public bool UsesActions => Form.Workload.OperationId == "cloud-agent";
    public decimal ActionsAllowance => _actionsAllowance;
    public decimal ActionsConsumed => _actionsConsumed;
    public bool AutoEnabled => _autoOverride ?? Scenario.Calls.All(call =>
        call.EnabledMultiplierIds.Contains("auto-model-selection", StringComparer.OrdinalIgnoreCase));
    public IEnumerable<string> Users => Scenario.BillingContext?.SeatAssignments
        .Select(seat => seat.UserId).Distinct(StringComparer.OrdinalIgnoreCase) ?? [];

    public string ModelLabel(ModelDefinition model) => model.DisplayName ?? model.Id;

    public void Initialize()
    {
        Rehydrate();
        Simulate();
    }

    public void MarkEdited()
    {
        Invalidate();
        IsCustomized = true;
        Error = null;
        Notice = null;
        if (!Form.Economic.UseIndividualUlb)
        {
            _fieldErrors.Remove("cc-ulb-limit");
            _fieldErrors.Remove("cc-ulb-used");
        }
        if (!Form.Economic.UseCostCenterBudget)
        {
            _fieldErrors.Remove("cc-budget-limit");
            _fieldErrors.Remove("cc-budget-used");
        }
    }

    public void SetFieldError(string id, string? message)
    {
        MarkEdited();
        if (message is null)
        {
            _fieldErrors.Remove(id);
        }
        else
        {
            _fieldErrors[id] = message;
        }
    }

    public void SelectPreset(string operationId)
    {
        var problem = operationId == "chat" ? CompassProblem.PaidUsageDisabled : CompassProblem.Standard;
        LoadPreset(operationId, problem);
    }

    public void SelectProblem(string value)
    {
        if (!Enum.TryParse<CompassProblem>(value, out var problem) || !Enum.IsDefined(problem))
        {
            Fail("Select a supported demonstration.");
            return;
        }

        LoadPreset(problem == CompassProblem.ActionsBudget ? "cloud-agent" : SelectedPreset, problem);
    }

    public void Reset()
    {
        Configuration = _defaultConfiguration;
        _eligibility = null;
        EvidenceWarning = null;
        LoadPreset("chat", CompassProblem.PaidUsageDisabled);
        Notice = "Reset to the pinned demo and verified default catalog. Browser saves were not changed.";
    }

    public void SetAuto(bool enabled)
    {
        _autoOverride = enabled;
        MarkEdited();
    }

    public void SetActionsAllowance(decimal value)
    {
        _actionsAllowance = value;
        MarkEdited();
    }

    public void SetActionsConsumed(decimal value)
    {
        _actionsConsumed = value;
        MarkEdited();
    }

    public void SetDate(string value)
    {
        if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            SetFieldError("simulation-date", "Enter a valid simulation date.");
            return;
        }

        SetFieldError("simulation-date", null);
        Scenario = Scenario with { Timestamp = timestamp.AddHours(12) };
    }

    public void SetGate(string gateId, bool passed)
    {
        var gates = new Dictionary<string, AccessGateState>(Scenario.AccessGates, StringComparer.OrdinalIgnoreCase)
        {
            [gateId] = new AccessGateState
            {
                Passed = passed,
                Reason = passed ? "Supplied scenario assumption: access permitted." : "Supplied scenario assumption: access denied.",
                Remediation = passed ? null : "Review this supplied access assumption. Spending changes do not override access policy."
            }
        };
        Scenario = Scenario with { AccessGates = gates };
        MarkEdited();
    }

    public void SelectUser(string userId)
    {
        if (!TryCapture(out var current) || current.Attribution is null)
        {
            return;
        }

        var selected = current with { Attribution = current.Attribution with { UserId = userId } };
        var attribution = new AttributionResolver().Resolve(selected.Attribution, selected.Timestamp);
        var seat = new EconomicBalanceCalculator(Configuration).ResolveSelectedPlanSeat(selected, attribution);
        if (seat.Seat is null || seat.Status is SelectedPlanSeatStatus.Missing or SelectedPlanSeatStatus.Ambiguous)
        {
            Fail("The selected user must have one effective seat. Update the complete scenario in Advanced.");
            return;
        }

        selected = selected with { PlanId = seat.Seat.PlanId };
        var attributionState = new AttributionEditorAdapter().MapFromScenario(selected);
        attributionState.CostCenterId = seat.Seat.CostCenterId ?? "";
        selected = new AttributionEditorAdapter().ApplyToScenario(selected, attributionState);
        Scenario = selected;
        Rehydrate();
        MarkEdited();
    }

    public void Simulate()
    {
        Invalidate();
        if (!TryCapture(out var current))
        {
            return;
        }

        Scenario = current;
        Rehydrate();
        Preview = new SimulationPreviewService(Configuration).Preview(current);
        HasRun = true;
        Error = null;
        Notice = null;
    }

    public void Compare()
    {
        if (Preview is null || !TryCapture(out var current))
        {
            return;
        }

        Comparisons = new SimulationPreviewService(Configuration).Compare(current);
        Notice = Comparisons.Count == 0
            ? "No supported automatic alternative was found. Inspect the first blocker and supplied assumptions."
            : "Alternatives were re-evaluated against every modeled gate. Nothing was changed in GitHub.";
    }

    public void ApplyComparison(string id)
    {
        var comparison = Comparisons.FirstOrDefault(candidate => candidate.Id == id);
        if (comparison is null || Preview is null)
        {
            Fail("This comparison is no longer current. Simulate and preview alternatives again.");
            return;
        }

        Scenario = scenarioJson.Deserialize(scenarioJson.Serialize(comparison.CandidateScenario));
        SourceLabel = "Local alternative";
        Rehydrate();
        MarkEdited();
        Notice = "Alternative applied to this local scenario only. Simulate to confirm the new working result.";
    }

    public string? CreateBundle()
    {
        if (!TryCapture(out var current))
        {
            return null;
        }

        return bundles.Export(current, Configuration);
    }

    public bool Import(string json, string source = "Imported bundle")
    {
        Invalidate();
        try
        {
            var imported = bundles.Import(json, Configuration);
            var nextForm = editor.MapFromScenario(imported.Scenario, imported.Catalog);
            var nextFingerprint = CompassBundleCodec.CatalogHash(imported.Catalog);
            Configuration = imported.Catalog;
            _eligibility = null;
            Scenario = imported.Scenario;
            SourceLabel = source;
            EvidenceWarning = imported.Warning;
            SelectedPreset = CompassPresetFactory.Presets.Any(p => p.OperationId == Scenario.OperationId) ? Scenario.OperationId : "";
            SelectedProblem = CompassProblem.Standard;
            Rehydrate(nextForm, nextFingerprint);
            IsCustomized = true;
            Error = null;
            Notice = imported.Warning ?? "Scenario and matching reference loaded. Simulate to evaluate this snapshot.";
            return true;
        }
        catch (Exception exception) when (IsInputException(exception))
        {
            Fail($"{Describe(exception)} Your working scenario and catalog were preserved.");
            return false;
        }
    }

    public async Task SaveAsync()
    {
        var bundle = CreateBundle();
        if (bundle is null) return;
        try
        {
            await persistence.SaveAsync(bundle);
            Notice = "Saved in this browser: current form values, scenario, catalog, and reference evidence. One Compass save slot.";
            Error = null;
        }
        catch (BrowserPersistenceException exception)
        {
            Fail(exception.Message);
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var bundle = await persistence.LoadAsync();
            if (bundle is null)
            {
                Notice = "No Compass scenario is saved in this browser. Advanced simulator saves use a separate slot.";
                return;
            }
            Import(bundle, "Browser save");
        }
        catch (BrowserPersistenceException exception)
        {
            Fail(exception.Message);
        }
    }

    public async Task ExportAsync()
    {
        var bundle = CreateBundle();
        if (bundle is null) return;
        try
        {
            await persistence.ExportAsync(bundle);
            Notice = "Exported a portable scenario and reference bundle, including current form edits.";
            Error = null;
        }
        catch (BrowserPersistenceException exception)
        {
            Fail(exception.Message);
        }
    }

    public async Task ImportFileAsync(IBrowserFile file)
    {
        try
        {
            Import(await BrowserScenarioPersistence.ReadImportAsync(file), file.Name);
        }
        catch (IOException exception)
        {
            Fail($"The file could not be imported: {exception.Message} Your working scenario was preserved.");
        }
        catch (JSException exception)
        {
            Fail($"The browser could not read the selected file: {exception.Message} Your working scenario was preserved.");
        }
    }

    public void ReportError(string message) => Fail(message);

    private void LoadPreset(string operationId, CompassProblem problem)
    {
        Invalidate();
        try
        {
            var scenario = CompassPresetFactory.Create(Configuration, operationId, problem);
            Scenario = scenario;
            SelectedPreset = operationId;
            SelectedProblem = problem;
            SourceLabel = problem switch
            {
                CompassProblem.PaidUsageDisabled => "Paid usage disabled demo",
                CompassProblem.AiBudget => "AI spending budget demo",
                CompassProblem.UserBudget => "User-level budget demo",
                CompassProblem.ActionsBudget => "Actions budget demo",
                _ => "Standard workload"
            };
            Rehydrate();
            IsCustomized = false;
            Error = null;
            Notice = null;
        }
        catch (Exception exception) when (IsInputException(exception))
        {
            Fail($"{Describe(exception)} Use Reset to restore the default catalog.");
        }
    }

    private void Rehydrate(ScenarioEditorState? preparedForm = null, string? preparedFingerprint = null)
    {
        var nextForm = preparedForm ?? editor.MapFromScenario(Scenario, Configuration);
        var fingerprint = preparedFingerprint ?? CompassBundleCodec.CatalogHash(Configuration);
        Form = nextForm;
        _baselineForm = editor.MapFromScenario(Scenario, Configuration);
        _actionsAllowance = Scenario.ActionsGuardrails?.IncludedMinutes ?? 0m;
        _actionsConsumed = Scenario.ActionsGuardrails?.ConsumedIncludedMinutes ?? 0m;
        _autoOverride = null;
        _fieldErrors.Clear();
        CatalogFingerprint = fingerprint;
        FormRevision++;
        Invalidate();
    }

    private bool TryCapture(out SimulationScenario scenario)
    {
        scenario = Scenario;
        if (!CanSimulate)
        {
            Fail("Correct the highlighted input fields before simulating, saving, or exporting.");
            return false;
        }
        try
        {
            var draft = editor.ApplyChangedSectionsToScenario(Scenario, Form, _baselineForm, Configuration);
            if (_autoOverride is bool auto)
            {
                draft = draft with
                {
                    Calls = draft.Calls.Select(call => call with
                    {
                        EnabledMultiplierIds = call.EnabledMultiplierIds.Where(id =>
                            !string.Equals(id, "auto-model-selection", StringComparison.OrdinalIgnoreCase))
                            .Concat(auto ? ["auto-model-selection"] : []).ToArray()
                    }).ToArray()
                };
            }
            if (draft.ActionsGuardrails is not null)
            {
                draft = draft with
                {
                    ActionsGuardrails = draft.ActionsGuardrails with
                    {
                        IncludedMinutes = _actionsAllowance,
                        ConsumedIncludedMinutes = _actionsConsumed
                    }
                };
            }
            SimulationScenarioValidator.Validate(draft);
            scenario = draft;
            return true;
        }
        catch (Exception exception) when (IsInputException(exception))
        {
            Fail(Describe(exception));
            return false;
        }
    }

    private void Invalidate()
    {
        Preview = null;
        Comparisons = [];
    }

    private void Fail(string message)
    {
        Invalidate();
        Error = message;
        Notice = null;
    }

    private static bool IsInputException(Exception exception) =>
        exception is JsonException or ConfigurationException or SimulationException;

    private static string Describe(Exception exception) => exception is SimulationException simulation
        ? $"{simulation.Code}: {simulation.Message}"
        : exception.Message;

    private ModelEligibilityEvaluator Eligibility => _eligibility ??= new ModelEligibilityEvaluator(Configuration);
}

public sealed record CompassBlockingSetting(string Id, string Label, string? SectionId = null, bool RequiresBundleEditor = false, string Unit = "");
