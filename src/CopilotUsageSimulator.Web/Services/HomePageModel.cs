using System.Text.Json;
using CopilotUsageSimulator.Engine;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;
using Microsoft.AspNetCore.Components.Forms;

namespace CopilotUsageSimulator.Web.Services;

public sealed class HomePageModel
{
    private readonly ICopilotUsageSimulationEngine defaultEngine;
    private readonly EngineConfiguration defaultConfiguration;
    private readonly ScenarioJson scenarioJson;
    private readonly ScenarioEditorAdapter editorAdapter;
    private readonly SimulationSessionRunner sessionRunner;
    private readonly BrowserScenarioPersistence browserPersistence;
    private EngineConfiguration activeConfiguration;
    private ICopilotUsageSimulationEngine activeEngine;
    private string? selectedTemplateOperationId;
    private ExampleScenarioVariant selectedTemplateVariant;

    public HomePageModel(
        ICopilotUsageSimulationEngine defaultEngine,
        EngineConfiguration defaultConfiguration,
        ScenarioJson scenarioJson,
        ScenarioEditorAdapter editorAdapter,
        SimulationSessionRunner sessionRunner,
        BrowserScenarioPersistence browserPersistence)
    {
        this.defaultEngine = defaultEngine;
        this.defaultConfiguration = defaultConfiguration;
        this.scenarioJson = scenarioJson;
        this.editorAdapter = editorAdapter;
        this.sessionRunner = sessionRunner;
        this.browserPersistence = browserPersistence;
        activeEngine = defaultEngine;
        activeConfiguration = defaultConfiguration;
    }

    public ScenarioEditorState Form { get; private set; } = new();
    public SimulationResultsState Results { get; } = new();
    public string ScenarioJsonText { get; set; } = "";
    public string CatalogJson { get; set; } = "";
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public string? ScenarioSourceLabel { get; private set; }
    public bool IsCustomized { get; private set; }
    public EngineConfiguration ActiveConfiguration => activeConfiguration;
    public OperationDefinition? SelectedOperation => ActiveConfiguration.Operations.FirstOrDefault(
        operation => string.Equals(operation.Id, Form.Workload.OperationId, StringComparison.OrdinalIgnoreCase));
    public IEnumerable<OperationDefinition> ExampleOperations =>
        ActiveConfiguration.Operations.Where(operation => !string.IsNullOrWhiteSpace(operation.ExampleLabel));
    public IReadOnlyList<CostBlockedExample> CostBlockedExamples
    {
        get
        {
            var operation = ActiveConfiguration.Operations.FirstOrDefault(candidate =>
                    candidate.IsBilled &&
                    string.Equals(
                        candidate.Id,
                        ActiveConfiguration.ExampleScenario.PreferredOperationId,
                        StringComparison.OrdinalIgnoreCase))
                ?? ActiveConfiguration.Operations.FirstOrDefault(candidate =>
                    candidate.IsBilled && !string.IsNullOrWhiteSpace(candidate.ExampleLabel));
            if (operation is null)
            {
                return [];
            }

            var actionsOperation = operation.ActionsMetering != ActionsMeteringMode.None
                ? operation
                : ActiveConfiguration.Operations.FirstOrDefault(candidate =>
                    candidate.IsBilled &&
                    candidate.ActionsMetering != ActionsMeteringMode.None &&
                    !string.IsNullOrWhiteSpace(candidate.ExampleLabel));
            var examples = new List<CostBlockedExample>
            {
                new("User-level budget exceeded", operation.Id, ExampleScenarioVariant.UserLevelBudgetExceeded),
                new("Included-use overflow prohibited", operation.Id, ExampleScenarioVariant.IncludedUseOverflowProhibited),
                new("Paid usage not authorized for GitHub Copilot", operation.Id, ExampleScenarioVariant.PaidUsageNotApplicable),
                new("Paid usage disabled", operation.Id, ExampleScenarioVariant.PaidUsageDisabled),
                new("AI spending budget exceeded", operation.Id, ExampleScenarioVariant.AiSpendingBudgetExceeded)
            };
            if (actionsOperation is not null)
            {
                examples.Add(new(
                    "Actions spending budget exceeded",
                    actionsOperation.Id,
                    ExampleScenarioVariant.ActionsSpendingBudgetExceeded));
            }

            return examples;
        }
    }
    public bool OperationConsumesAiCredits => SelectedOperation?.IsBilled == true;
    public bool OperationUsesActions => SelectedOperation?.ActionsMetering != ActionsMeteringMode.None;
    public bool OperationUsesRepositoryVisibility =>
        SelectedOperation?.ActionsMetering == ActionsMeteringMode.PrivateRepositories;
    public string OperationContext => SelectedOperation switch
    {
        null => "This operation is not present in the active catalog.",
        { IsBilled: false } =>
            "This operation does not consume AI credits. Token, billing, runtime, and spending controls are hidden and not evaluated.",
        { ActionsMetering: ActionsMeteringMode.Always } =>
            "This operation consumes AI credits and always uses GitHub Actions. Repository visibility does not change runner metering.",
        { ActionsMetering: ActionsMeteringMode.PrivateRepositories } =>
            "This operation consumes AI credits. GitHub Actions charges apply to private and internal repositories, so repository visibility is required.",
        _ =>
            "This operation consumes AI credits but does not use GitHub Actions. Runner and repository controls are hidden and not evaluated."
    };

    public void Initialize()
    {
        ResetCatalog();
        LoadTemplate(InitialExampleOperationId);
    }

    public void LoadTemplate(
        string template,
        ExampleScenarioVariant variant = ExampleScenarioVariant.Standard)
    {
        var scenario = ExampleScenarioFactory.Create(ActiveConfiguration, template, variant);
        ScenarioJsonText = scenarioJson.Serialize(scenario);
        LoadForm(scenario);
        RunScenario(scenario, advanceWorkingState: false);
        selectedTemplateOperationId = scenario.OperationId;
        selectedTemplateVariant = variant;
        ScenarioSourceLabel = variant == ExampleScenarioVariant.Standard
            ? ActiveConfiguration.Operations.Single(operation =>
                string.Equals(operation.Id, scenario.OperationId, StringComparison.OrdinalIgnoreCase)).ExampleLabel
                ?? scenario.OperationId
            : CostBlockedExamples.Single(example => example.Variant == variant).Label;
        IsCustomized = false;
        Notice = null;
    }

    public bool IsSelectedTemplate(string operationId, ExampleScenarioVariant variant) =>
        string.Equals(selectedTemplateOperationId, operationId, StringComparison.OrdinalIgnoreCase) &&
        selectedTemplateVariant == variant;

    public void MarkCustomized()
    {
        if (ScenarioSourceLabel is not null)
        {
            IsCustomized = true;
        }
    }

    public void ApplyOverrides()
    {
        try
        {
            var scenario = editorAdapter.ApplyToScenario(
                scenarioJson.Deserialize(ScenarioJsonText),
                Form,
                ActiveConfiguration);
            ScenarioJsonText = scenarioJson.Serialize(scenario);
            Error = null;
            Notice = "Guided overrides applied to the complete scenario.";
        }
        catch (Exception exception) when (exception is JsonException or SimulationException)
        {
            Error = exception.Message;
            Notice = null;
        }
    }

    public void ApplyAndRun()
    {
        ApplyOverrides();
        if (Error is null)
        {
            RunJson();
        }
    }

    public void RunJson() => RunJsonCore(advanceWorkingState: true);

    public void ReloadGuidedFields()
    {
        try
        {
            var form = editorAdapter.MapFromScenario(
                scenarioJson.Deserialize(ScenarioJsonText),
                ActiveConfiguration);
            Form = form;
            Error = null;
            Notice = "Guided fields refreshed from JSON.";
        }
        catch (Exception exception) when (IsScenarioInputException(exception))
        {
            SetError(exception);
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            await browserPersistence.SaveAsync(ScenarioJsonText, CatalogJson, Results.GetPreferences());
            Notice = "Scenario saved in this browser.";
            Error = null;
        }
        catch (BrowserPersistenceException exception)
        {
            Error = $"{exception.Message} {exception.InnerException?.Message}";
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var saved = await browserPersistence.LoadAsync();
            if (saved is null)
            {
                Error = "No saved scenario was found in this browser.";
                return;
            }

            var hasSavedCatalog = !string.IsNullOrWhiteSpace(saved.CatalogJson);
            var configuration = hasSavedCatalog
                ? scenarioJson.DeserializeConfiguration(saved.CatalogJson!)
                : activeConfiguration;
            var engine = hasSavedCatalog
                ? new CopilotUsageSimulationEngine(configuration)
                : activeEngine;
            var prepared = PrepareScenario(saved.ScenarioJson, configuration, engine);

            if (saved.Preferences is not null)
            {
                var preferencesProbe = new SimulationResultsState();
                preferencesProbe.ApplyPreferences(saved.Preferences);
            }

            activeConfiguration = configuration;
            activeEngine = engine;
            if (hasSavedCatalog)
            {
                CatalogJson = saved.CatalogJson!;
            }
            if (saved.Preferences is not null)
            {
                Results.ApplyPreferences(saved.Preferences);
            }
            CommitScenario(prepared);
            selectedTemplateOperationId = null;
            ScenarioSourceLabel = "Saved scenario";
            IsCustomized = true;
            Notice = "Saved scenario loaded.";
            Error = null;
        }
        catch (Exception exception) when (
            exception is BrowserPersistenceException || IsScenarioInputException(exception))
        {
            SetError(exception);
        }
    }

    public async Task ExportAsync()
    {
        try
        {
            await browserPersistence.ExportAsync(ScenarioJsonText);
        }
        catch (BrowserPersistenceException exception)
        {
            Error = $"{exception.Message} {exception.InnerException?.Message}";
        }
    }

    public async Task ImportAsync(IBrowserFile file)
    {
        try
        {
            var importedJson = await BrowserScenarioPersistence.ReadImportAsync(file);
            var prepared = PrepareScenario(importedJson, activeConfiguration, activeEngine);
            CommitScenario(prepared);
            selectedTemplateOperationId = null;
            ScenarioSourceLabel = $"Imported: {file.Name}";
            IsCustomized = true;
            Notice = $"Imported {file.Name}.";
            Error = null;
        }
        catch (Exception exception) when (
            exception is IOException || IsScenarioInputException(exception))
        {
            SetError(exception);
        }
    }

    public void ApplyCatalog()
    {
        try
        {
            var configuration = scenarioJson.DeserializeConfiguration(CatalogJson);
            var engine = new CopilotUsageSimulationEngine(configuration);
            var scenario = ExampleScenarioFactory.Create(
                configuration,
                GetInitialExampleOperationId(configuration));
            var prepared = PrepareScenario(
                scenarioJson.Serialize(scenario),
                configuration,
                engine);

            activeConfiguration = configuration;
            activeEngine = engine;
            CommitScenario(prepared);
            selectedTemplateOperationId = scenario.OperationId;
            selectedTemplateVariant = ExampleScenarioVariant.Standard;
            ScenarioSourceLabel = configuration.Operations.Single(operation =>
                string.Equals(operation.Id, scenario.OperationId, StringComparison.OrdinalIgnoreCase)).ExampleLabel
                ?? scenario.OperationId;
            IsCustomized = false;
            Error = null;
            Notice = $"Catalog '{configuration.Version}' applied.";
        }
        catch (Exception exception) when (IsScenarioInputException(exception))
        {
            SetError(exception);
        }
    }

    public void ResetCatalog()
    {
        activeConfiguration = defaultConfiguration;
        activeEngine = defaultEngine;
        CatalogJson = scenarioJson.SerializeConfiguration(defaultConfiguration);
        Error = null;
    }

    private string InitialExampleOperationId => GetInitialExampleOperationId(ActiveConfiguration);

    private static string GetInitialExampleOperationId(EngineConfiguration configuration) =>
        configuration.ExampleScenario.PreferredOperationId
        ?? configuration.Operations.FirstOrDefault(operation =>
            !string.IsNullOrWhiteSpace(operation.ExampleLabel))?.Id
        ?? configuration.Operations.FirstOrDefault()?.Id
        ?? throw new ConfigurationException("The active catalog does not define an operation.");

    private PreparedScenarioState PrepareScenario(
        string json,
        EngineConfiguration configuration,
        ICopilotUsageSimulationEngine engine)
    {
        var scenario = scenarioJson.Deserialize(json);
        var form = editorAdapter.MapFromScenario(scenario, configuration);
        var session = sessionRunner.Run(engine, scenario, form.Workload.RepeatCount);
        return new PreparedScenarioState(json, form, session);
    }

    private void CommitScenario(PreparedScenarioState prepared)
    {
        ScenarioJsonText = prepared.Json;
        Form = prepared.Form;
        Results.SetRuns(prepared.Session.Runs);
    }

    private void SetError(Exception exception)
    {
        Error = exception switch
        {
            SimulationException simulation => $"{simulation.Code}: {simulation.Message}",
            BrowserPersistenceException persistence =>
                $"{persistence.Message} {persistence.InnerException?.Message}".Trim(),
            _ => exception.Message
        };
        Notice = null;
    }

    private static bool IsScenarioInputException(Exception exception) =>
        exception is JsonException or ConfigurationException or SimulationException or InvalidOperationException;

    private void RunJsonCore(bool advanceWorkingState)
    {
        try
        {
            RunScenario(scenarioJson.Deserialize(ScenarioJsonText), advanceWorkingState);
        }
        catch (Exception exception) when (exception is JsonException or SimulationException or InvalidOperationException)
        {
            Results.Clear();
            Error = exception is SimulationException simulation
                ? $"{simulation.Code}: {simulation.Message}"
                : exception.Message;
            Notice = null;
        }
    }

    private void RunScenario(SimulationScenario scenario, bool advanceWorkingState)
    {
        var session = sessionRunner.Run(activeEngine, scenario, Form.Workload.RepeatCount);
        Results.SetRuns(session.Runs);
        if (advanceWorkingState)
        {
            ScenarioJsonText = scenarioJson.Serialize(session.NextScenario);
            LoadForm(session.NextScenario);
            Notice = Results.CompletedRuns == 0
                ? "No balances changed because the first run was not allowed."
                : $"Working balances advanced by {Results.CompletedRuns} successful run(s).";
        }

        Error = null;
    }

    private void LoadForm(SimulationScenario scenario)
    {
        Form = editorAdapter.MapFromScenario(scenario, ActiveConfiguration);
    }

    private sealed record PreparedScenarioState(
        string Json,
        ScenarioEditorState Form,
        SimulationSessionResult Session);
}

public sealed record CostBlockedExample(
    string Label,
    string OperationId,
    ExampleScenarioVariant Variant);
