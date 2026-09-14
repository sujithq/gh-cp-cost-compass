using System.Text.Json;
using CopilotUsageSimulator.Engine;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;
using CopilotUsageSimulator.Web.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace CopilotUsageSimulator.Web.Tests;

public sealed class ServiceTests
{
    private readonly EngineConfiguration _configuration =
        EngineConfigurationLoader.LoadDefault();

    [Theory]
    [InlineData("cloud-agent", true)]
    [InlineData("code-review", true)]
    [InlineData("chat", false)]
    public void TemplatesProduceRunnableCostOnlyScenarios(
        string template,
        bool expectsActions)
    {
        var scenario = ExampleScenarioFactory.Create(_configuration, template);

        Assert.Equal(template, scenario.OperationId);
        Assert.Equal(SimulationCheckScope.CostRelatedOnly, scenario.CheckScope);
        Assert.Equal(expectsActions, scenario.ActionsUsage is not null);
        Assert.Equal(expectsActions, scenario.ActionsGuardrails is not null);
        Assert.NotEmpty(scenario.Calls);
        Assert.NotNull(scenario.BillingContext);
        Assert.NotNull(scenario.Attribution);
        Assert.NotNull(scenario.EconomicGuardrails);
    }

    [Theory]
    [InlineData("chat", ExampleScenarioVariant.UserLevelBudgetExceeded, "ulb-user-1")]
    [InlineData("chat", ExampleScenarioVariant.IncludedUseOverflowProhibited, "included-cc-engineering")]
    [InlineData("chat", ExampleScenarioVariant.PaidUsageNotApplicable, "paid-usage.not-applicable")]
    [InlineData("chat", ExampleScenarioVariant.PaidUsageDisabled, "paid-usage")]
    [InlineData("chat", ExampleScenarioVariant.AiSpendingBudgetExceeded, "budget-cost-center")]
    [InlineData("cloud-agent", ExampleScenarioVariant.ActionsSpendingBudgetExceeded, "actions-enterprise")]
    public void CostBlockedTemplatesFailAtExpectedGate(
        string template,
        ExampleScenarioVariant variant,
        string expectedGate)
    {
        var scenario = ExampleScenarioFactory.Create(
            _configuration,
            template,
            variant);

        var result = new CopilotUsageSimulationEngine(_configuration).Simulate(scenario);

        Assert.Equal(SimulationCheckScope.CostRelatedOnly, scenario.CheckScope);
        Assert.Equal(SimulationDecision.Blocked, result.Decision);
        Assert.Equal(expectedGate, result.FirstFailingGate);
    }

    [Fact]
    public void CustomCatalogDerivesExampleReferencesWithoutKnownIds()
    {
        var configuration = new EngineConfiguration
        {
            ExampleScenario = new ExampleScenarioDefinition
            {
                ProductId = "custom-product",
                SkuId = "custom-sku",
                PreferredOperationId = "custom-operation"
            },
            Plans =
            [
                new PlanDefinition
                {
                    Id = "custom-plan",
                    IsPooled = true,
                    AllowancePeriods =
                    [
                        new PlanAllowancePeriod
                        {
                            EffectiveFrom = DateTimeOffset.MinValue,
                            IncludedCreditsPerUser = 1_000m
                        }
                    ]
                }
            ],
            Operations =
            [
                new OperationDefinition
                {
                    Id = "custom-operation",
                    ActionsMetering = ActionsMeteringMode.Always,
                    ExampleLabel = "Custom operation",
                    ExampleTask = "Execute the custom operation."
                }
            ],
            Models =
            [
                new ModelDefinition
                {
                    Id = "custom-model",
                    PricePeriods =
                    [
                        new ModelPricePeriod
                        {
                            EffectiveFrom = DateTimeOffset.MinValue,
                            Tiers =
                            [
                                new TokenPriceTier
                                {
                                    Id = "custom-tier",
                                    InputUsdPerMillion = 1m,
                                    OutputUsdPerMillion = 1m
                                }
                            ]
                        }
                    ]
                }
            ],
            ActionsRunners =
            [
                new ActionsRunnerDefinition { Id = "custom-runner", UsdPerMinute = 0.01m }
            ]
        };
        var engine = new CopilotUsageSimulationEngine(configuration);

        var scenario = ExampleScenarioFactory.Create(configuration, "custom-operation");
        var result = engine.Simulate(scenario);

        Assert.Equal("custom-operation", scenario.OperationId);
        Assert.Equal("custom-plan", scenario.PlanId);
        Assert.Equal("custom-product", scenario.ProductId);
        Assert.Equal("custom-sku", scenario.SkuId);
        Assert.Equal("custom-model", Assert.Single(scenario.Calls).ModelId);
        Assert.Equal("custom-runner", scenario.ActionsUsage?.RunnerId);
        Assert.Equal("Execute the custom operation.", scenario.Metadata["task"]);
        Assert.Equal(SimulationDecision.Allowed, result.Decision);

        var unbilledConfiguration = configuration with
        {
            Operations =
            [
                configuration.Operations.Single() with { IsBilled = false }
            ]
        };
        Assert.Throws<ConfigurationException>(() => ExampleScenarioFactory.Create(
            unbilledConfiguration,
            "custom-operation",
            ExampleScenarioVariant.UserLevelBudgetExceeded));
    }

    [Fact]
    public void UnknownTemplateOperationIsRejectedInsteadOfCoerced()
    {
        var exception = Assert.Throws<ConfigurationException>(() =>
            ExampleScenarioFactory.Create(_configuration, "unknown-operation"));

        Assert.Contains("Unknown example operation", exception.Message);
    }

    [Fact]
    public void ScenarioJsonRoundTripsGuidedAndAdvancedState()
    {
        var serializer = new ScenarioJson();
        var original = ExampleScenarioFactory.Create(_configuration, "cloud-agent") with
        {
            CheckScope = SimulationCheckScope.All,
            Metadata = new Dictionary<string, string>
            {
                ["task"] = "Exercise serialization",
                ["repeatCount"] = "7"
            }
        };

        var roundTripped = serializer.Deserialize(serializer.Serialize(original));

        Assert.Equal(original.OperationId, roundTripped.OperationId);
        Assert.Equal(SimulationCheckScope.All, roundTripped.CheckScope);
        Assert.Equal("Exercise serialization", roundTripped.Metadata["task"]);
        Assert.Equal("7", roundTripped.Metadata["repeatCount"]);
        Assert.Equal(
            original.EconomicGuardrails!.EnterprisePoolConsumedCredits,
            roundTripped.EconomicGuardrails!.EnterprisePoolConsumedCredits);
        Assert.Equal(
            original.EconomicGuardrails.UserLevelBudgets,
            roundTripped.EconomicGuardrails.UserLevelBudgets);
        Assert.Equal(
            original.EconomicGuardrails.IncludedUsageControls,
            roundTripped.EconomicGuardrails.IncludedUsageControls);
        Assert.Equal(
            original.EconomicGuardrails.SpendingBudgets.Select(budget => (
                budget.Id,
                budget.Scope,
                budget.ScopeId,
                budget.LimitUsd,
                budget.ConsumedUsd,
                budget.Enforcement)),
            roundTripped.EconomicGuardrails.SpendingBudgets.Select(budget => (
                budget.Id,
                budget.Scope,
                budget.ScopeId,
                budget.LimitUsd,
                budget.ConsumedUsd,
                budget.Enforcement)));
        Assert.Equal(
            original.EconomicGuardrails.PaidUsage.State,
            roundTripped.EconomicGuardrails.PaidUsage.State);
        Assert.True(original.EconomicGuardrails.PaidUsage.ProductIds.SetEquals(
            roundTripped.EconomicGuardrails.PaidUsage.ProductIds));
        Assert.True(original.EconomicGuardrails.PaidUsage.SkuIds.SetEquals(
            roundTripped.EconomicGuardrails.PaidUsage.SkuIds));
    }

    [Fact]
    public void CatalogJsonRoundTripsDefaultConfiguration()
    {
        var serializer = new ScenarioJson();

        var roundTripped = serializer.DeserializeConfiguration(
            serializer.SerializeConfiguration(_configuration));

        Assert.Equal(_configuration.Version, roundTripped.Version);
        Assert.Equal(_configuration.Operations.Count, roundTripped.Operations.Count);
        Assert.Equal(_configuration.Models.Count, roundTripped.Models.Count);
        Assert.Equal(_configuration.Gates.Count, roundTripped.Gates.Count);
    }

    [Fact]
    public void ScenarioJsonRejectsExplicitNullCollectionsWithDomainError()
    {
        var serializer = new ScenarioJson();

        var exception = Assert.Throws<SimulationException>(() => serializer.Deserialize(
            """{"operationId":"chat","planId":"business","calls":null}"""));

        Assert.Equal(SimulationScenarioValidator.InvalidContractCode, exception.Code);
        Assert.Contains("calls", exception.Message);
    }

    [Fact]
    public void CatalogJsonRejectsExplicitNullCollectionsWithDomainError()
    {
        var serializer = new ScenarioJson();

        var exception = Assert.Throws<ConfigurationException>(() =>
            serializer.DeserializeConfiguration("""{"operations":null}"""));

        Assert.Equal(ConfigurationException.InvalidContractCode, exception.Code);
        Assert.Contains("operations", exception.Message);
    }

    [Fact]
    public void ScenarioEditorAdapterResolvesEffectiveScenarioValues()
    {
        var scenario = ExampleScenarioFactory.Create(_configuration, "cloud-agent") with
        {
            Metadata = new Dictionary<string, string>
            {
                ["task"] = "Map this scenario",
                ["repeatCount"] = "7"
            }
        };

        var state = CreateScenarioEditorAdapter().MapFromScenario(scenario, _configuration);

        Assert.Equal("Map this scenario", state.Workload.Task);
        Assert.Equal(7, state.Workload.RepeatCount);
        Assert.Equal(scenario.OperationId, state.Workload.OperationId);
        Assert.Equal(scenario.Attribution?.UserId, scenario.BillingContext?.SeatAssignments[0].UserId);
        Assert.NotNull(state.Attribution.DirectAssignmentIndex);
        Assert.NotEmpty(state.Attribution.CostCenterId);
    }

    [Fact]
    public void ScenarioEditorAdapterPreservesUnselectedAdvancedRecords()
    {
        var scenario = ExampleScenarioFactory.Create(_configuration, "cloud-agent");
        var historicalBudget = new SpendingBudget
        {
            Id = "historical-budget",
            Scope = SpendingBudgetScope.CostCenter,
            ScopeId = "historical-cost-center",
            LimitUsd = 99m,
            ConsumedUsd = 12m,
            EffectiveTo = scenario.Timestamp
        };
        scenario = scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                SpendingBudgets = [.. scenario.EconomicGuardrails.SpendingBudgets, historicalBudget]
            }
        };
        var adapter = CreateScenarioEditorAdapter();
        var state = adapter.MapFromScenario(scenario, _configuration);
        state.Workload.Task = "Updated task";
        state.Economic.PoolConsumed = 321m;

        var patched = adapter.ApplyToScenario(scenario, state, _configuration);

        Assert.Equal("Updated task", patched.Metadata["task"]);
        Assert.Equal(321m, patched.EconomicGuardrails!.EnterprisePoolConsumedCredits);
        Assert.Contains(patched.EconomicGuardrails.SpendingBudgets, budget =>
            budget == historicalBudget);
    }

    [Fact]
    public void ScenarioEditorAdapterAppliesSectionsInDependentOrder()
    {
        var scenario = ExampleScenarioFactory.Create(_configuration, "cloud-agent");
        var adapter = CreateScenarioEditorAdapter();
        var state = adapter.MapFromScenario(scenario, _configuration);
        state.Workload.Task = "Exercise every editor section";
        state.Workload.PlanId = "enterprise";
        state.Attribution.CostCenterId = "cost-center-updated";
        state.Attribution.OrganizationId = "organization-updated";
        state.Economic.PoolConsumed = 432m;
        state.Economic.UseOrganizationBudget = true;
        state.Economic.OrganizationBudgetId = "budget-organization";
        state.Actions.Minutes = 27m;
        state.Runtime.Enabled = true;
        state.Runtime.MaximumModelCalls = 12;

        var patched = adapter.ApplyToScenario(scenario, state, _configuration);

        Assert.Equal("Exercise every editor section", patched.Metadata["task"]);
        Assert.Equal("enterprise", patched.PlanId);
        Assert.Equal(
            "enterprise",
            Assert.Single(patched.BillingContext!.SeatAssignments, seat =>
                seat.UserId == patched.Attribution!.UserId).PlanId);
        Assert.Equal(
            "cost-center-updated",
            patched.Attribution!.DirectAssignments[state.Attribution.DirectAssignmentIndex!.Value].CostCenterId);
        Assert.Equal("organization-updated", patched.Attribution.LicensingOrganizationIds[
            state.Attribution.LicensingOrganizationIndex!.Value]);
        Assert.Equal(432m, patched.EconomicGuardrails!.EnterprisePoolConsumedCredits);
        Assert.Equal(
            "cost-center-updated",
            Assert.Single(patched.EconomicGuardrails.SpendingBudgets, budget =>
                budget.Id == state.Economic.CostCenterBudgetId).ScopeId);
        Assert.Equal(
            "organization-updated",
            Assert.Single(patched.EconomicGuardrails.SpendingBudgets, budget =>
                budget.Id == state.Economic.OrganizationBudgetId).ScopeId);
        Assert.Equal(27m, patched.ActionsUsage!.Minutes);
        Assert.Equal(12, patched.RuntimeGuardrails!.MaximumModelCalls);
    }

    [Fact]
    public void SectionAdaptersPreserveUnownedScenarioContracts()
    {
        var scenario = ExampleScenarioFactory.Create(_configuration, "cloud-agent");

        var workload = new WorkloadEditorAdapter();
        var workloadState = workload.MapFromScenario(scenario, _configuration);
        workloadState.Task = "Updated workload";
        var workloadPatched = workload.ApplyToScenario(scenario, workloadState);
        Assert.Same(scenario.Attribution, workloadPatched.Attribution);
        Assert.Same(scenario.EconomicGuardrails, workloadPatched.EconomicGuardrails);
        Assert.Same(scenario.ActionsUsage, workloadPatched.ActionsUsage);
        Assert.Same(scenario.ActionsGuardrails, workloadPatched.ActionsGuardrails);
        Assert.Same(scenario.RuntimeGuardrails, workloadPatched.RuntimeGuardrails);

        var attribution = new AttributionEditorAdapter();
        var attributionState = attribution.MapFromScenario(scenario);
        attributionState.CostCenterId = "cost-center-updated";
        var attributionPatched = attribution.ApplyToScenario(scenario, attributionState);
        Assert.Same(scenario.Calls, attributionPatched.Calls);
        Assert.Same(scenario.Metadata, attributionPatched.Metadata);
        Assert.Same(scenario.EconomicGuardrails, attributionPatched.EconomicGuardrails);
        Assert.Same(scenario.ActionsUsage, attributionPatched.ActionsUsage);
        Assert.Same(scenario.ActionsGuardrails, attributionPatched.ActionsGuardrails);
        Assert.Same(scenario.RuntimeGuardrails, attributionPatched.RuntimeGuardrails);

        var economic = new EconomicEditorAdapter();
        var economicState = economic.MapFromScenario(scenario);
        economicState.PoolConsumed = 321m;
        var economicPatched = economic.ApplyToScenario(scenario, economicState);
        Assert.Same(scenario.Calls, economicPatched.Calls);
        Assert.Same(scenario.Metadata, economicPatched.Metadata);
        Assert.Same(scenario.BillingContext, economicPatched.BillingContext);
        Assert.Same(scenario.Attribution, economicPatched.Attribution);
        Assert.Same(scenario.ActionsUsage, economicPatched.ActionsUsage);
        Assert.Same(scenario.ActionsGuardrails, economicPatched.ActionsGuardrails);
        Assert.Same(scenario.RuntimeGuardrails, economicPatched.RuntimeGuardrails);

        var actions = new ActionsEditorAdapter();
        var actionsState = actions.MapFromScenario(scenario);
        actionsState.Minutes = 27m;
        var actionsPatched = actions.ApplyToScenario(scenario, actionsState, _configuration);
        Assert.Same(scenario.Calls, actionsPatched.Calls);
        Assert.Same(scenario.Metadata, actionsPatched.Metadata);
        Assert.Same(scenario.BillingContext, actionsPatched.BillingContext);
        Assert.Same(scenario.Attribution, actionsPatched.Attribution);
        Assert.Same(scenario.EconomicGuardrails, actionsPatched.EconomicGuardrails);
        Assert.Same(scenario.RuntimeGuardrails, actionsPatched.RuntimeGuardrails);

        var runtime = new RuntimeEditorAdapter();
        var runtimeState = runtime.MapFromScenario(scenario);
        runtimeState.Enabled = true;
        runtimeState.MaximumModelCalls = 12;
        var runtimePatched = runtime.ApplyToScenario(scenario, runtimeState);
        Assert.Same(scenario.Calls, runtimePatched.Calls);
        Assert.Same(scenario.Metadata, runtimePatched.Metadata);
        Assert.Same(scenario.BillingContext, runtimePatched.BillingContext);
        Assert.Same(scenario.Attribution, runtimePatched.Attribution);
        Assert.Same(scenario.EconomicGuardrails, runtimePatched.EconomicGuardrails);
        Assert.Same(scenario.ActionsUsage, runtimePatched.ActionsUsage);
        Assert.Same(scenario.ActionsGuardrails, runtimePatched.ActionsGuardrails);
    }

    private static ScenarioEditorAdapter CreateScenarioEditorAdapter() =>
        new(
            new WorkloadEditorAdapter(),
            new AttributionEditorAdapter(),
            new EconomicEditorAdapter(),
            new RuntimeEditorAdapter(),
            new ActionsEditorAdapter());

    [Fact]
    public void SimulationResultsStateClearsResultAndRunHistory()
    {
        var engine = new CopilotUsageSimulationEngine(_configuration);
        var result = engine.Simulate(
            ExampleScenarioFactory.Create(_configuration, "cloud-agent"));
        var state = new SimulationResultsState();
        state.SetRuns([result]);

        state.Clear();

        Assert.Null(state.Result);
        Assert.Empty(state.Runs);
        Assert.Equal(0, state.CompletedRuns);
    }

    [Fact]
    public async Task MalformedImportPreservesExistingPageState()
    {
        var model = CreateHomePageModel(new StorageJsRuntime());
        var previous = CaptureState(model);

        await model.ImportAsync(new MemoryBrowserFile("invalid.json", "{"));

        AssertStatePreserved(model, previous);
        Assert.NotNull(model.Error);
        Assert.Null(model.Notice);
    }

    [Fact]
    public async Task BrowserPersistenceRoundTripsCompleteStateWithOneWrite()
    {
        var runtime = new AtomicStorageJsRuntime();
        var persistence = new BrowserScenarioPersistence(runtime);
        var preferences = new DisplayPreferences("custom", ["runtime", "actions"]);

        await persistence.SaveAsync("scenario", "catalog", preferences);
        var saved = await persistence.LoadAsync();

        Assert.NotNull(saved);
        Assert.Equal("scenario", saved.ScenarioJson);
        Assert.Equal("catalog", saved.CatalogJson);
        Assert.Equal("custom", saved.Preferences!.VisibilityMode);
        Assert.Equal(["runtime", "actions"], saved.Preferences.HiddenCategories);
        Assert.Equal(1, runtime.SetItemCalls);
        Assert.Equal(1, runtime.StoredItemCount);
    }

    [Fact]
    public async Task FailedBrowserSavePreservesPreviousEnvelope()
    {
        var runtime = new AtomicStorageJsRuntime();
        var persistence = new BrowserScenarioPersistence(runtime);
        await persistence.SaveAsync(
            "previous-scenario",
            "previous-catalog",
            new DisplayPreferences("summary", []));
        runtime.RejectWrites = true;

        await Assert.ThrowsAsync<BrowserPersistenceException>(
            () => persistence.SaveAsync(
                "new-scenario",
                "new-catalog",
                new DisplayPreferences("custom", ["runtime"])));
        runtime.RejectWrites = false;
        var saved = await persistence.LoadAsync();

        Assert.Equal("previous-scenario", saved!.ScenarioJson);
        Assert.Equal("previous-catalog", saved.CatalogJson);
        Assert.Equal("summary", saved.Preferences!.VisibilityMode);
    }

    [Fact]
    public async Task BrowserPersistenceReturnsNullWhenNoStateIsSaved()
    {
        var saved = await new BrowserScenarioPersistence(new AtomicStorageJsRuntime()).LoadAsync();

        Assert.Null(saved);
    }

    [Fact]
    public async Task BrowserPersistenceRejectsUnsupportedStateVersion()
    {
        var persistence = new BrowserScenarioPersistence(
            new StorageJsRuntime(SavedState("scenario", "catalog", version: 2)));

        var exception = await Assert.ThrowsAsync<JsonException>(() => persistence.LoadAsync());

        Assert.Contains("Unsupported browser state version '2'", exception.Message);
    }

    [Fact]
    public async Task BrowserPersistenceRejectsIncompleteState()
    {
        var persistence = new BrowserScenarioPersistence(
            new StorageJsRuntime(JsonSerializer.Serialize(new { Version = 1 })));

        var exception = await Assert.ThrowsAsync<JsonException>(() => persistence.LoadAsync());

        Assert.Contains("saved browser state is incomplete", exception.Message);
    }

    [Fact]
    public async Task MalformedSavedEnvelopePreservesExistingPageState()
    {
        var model = CreateHomePageModel(new StorageJsRuntime("{"));
        var previous = CaptureState(model);

        await model.LoadAsync();

        AssertStatePreserved(model, previous);
        Assert.NotNull(model.Error);
        Assert.Null(model.Notice);
    }

    [Fact]
    public async Task InvalidSavedCatalogPreservesExistingPageState()
    {
        var serializer = new ScenarioJson();
        var invalidConfiguration = _configuration with
        {
            Plans = [_configuration.Plans[0] with { Id = " " }, .. _configuration.Plans.Skip(1)]
        };
        var runtime = new StorageJsRuntime(
            SavedState(
                serializer.Serialize(ExampleScenarioFactory.Create(_configuration, "chat")),
                serializer.SerializeConfiguration(invalidConfiguration)));
        var model = CreateHomePageModel(runtime);
        var previous = CaptureState(model);

        await model.LoadAsync();

        AssertStatePreserved(model, previous);
        Assert.Contains("plan id", model.Error);
        Assert.Null(model.Notice);
    }

    [Fact]
    public async Task InvalidSavedScenarioPreservesExistingPageState()
    {
        var serializer = new ScenarioJson();
        var invalidScenario = ExampleScenarioFactory.Create(_configuration, "chat") with
        {
            OperationId = ""
        };
        var runtime = new StorageJsRuntime(
            SavedState(
                serializer.Serialize(invalidScenario),
                serializer.SerializeConfiguration(_configuration)));
        var model = CreateHomePageModel(runtime);
        var previous = CaptureState(model);

        await model.LoadAsync();

        AssertStatePreserved(model, previous);
        Assert.Contains("operationId", model.Error);
        Assert.Null(model.Notice);
    }

    private HomePageModel CreateHomePageModel(IJSRuntime jsRuntime)
    {
        var serializer = new ScenarioJson();
        var model = new HomePageModel(
            new CopilotUsageSimulationEngine(_configuration),
            _configuration,
            serializer,
            CreateScenarioEditorAdapter(),
            new SimulationSessionRunner(),
            new BrowserScenarioPersistence(jsRuntime));
        model.Initialize();
        model.Results.VisibilityMode = "custom";
        model.Results.HiddenCategories.Add("runtime");
        return model;
    }

    private static PageStateSnapshot CaptureState(HomePageModel model) =>
        new(
            model.ActiveConfiguration,
            model.CatalogJson,
            model.ScenarioJsonText,
            model.Form,
            model.Results.Result,
            model.Results.Runs.ToArray(),
            model.Results.GetPreferences());

    private static void AssertStatePreserved(HomePageModel model, PageStateSnapshot previous)
    {
        Assert.Same(previous.Configuration, model.ActiveConfiguration);
        Assert.Equal(previous.CatalogJson, model.CatalogJson);
        Assert.Equal(previous.ScenarioJson, model.ScenarioJsonText);
        Assert.Same(previous.Form, model.Form);
        Assert.Same(previous.Result, model.Results.Result);
        Assert.Equal(previous.Runs, model.Results.Runs);
        Assert.Equal(previous.Preferences.VisibilityMode, model.Results.VisibilityMode);
        Assert.Equal(previous.Preferences.HiddenCategories, model.Results.HiddenCategories);
    }

    private static string SavedState(
        string scenarioJson,
        string catalogJson,
        DisplayPreferences? preferences = null,
        int version = 1) =>
        JsonSerializer.Serialize(new
        {
            Version = version,
            ScenarioJson = scenarioJson,
            CatalogJson = catalogJson,
            Preferences = preferences ?? new DisplayPreferences("summary", [])
        });

    private sealed record PageStateSnapshot(
        EngineConfiguration Configuration,
        string CatalogJson,
        string ScenarioJson,
        ScenarioEditorState Form,
        SimulationResult? Result,
        SimulationResult[] Runs,
        DisplayPreferences Preferences);

    private sealed class StorageJsRuntime(params string?[] values) : IJSRuntime
    {
        private int index;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            var value = index < values.Length ? values[index++] : null;
            return ValueTask.FromResult((TValue)(object?)value!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            var value = index < values.Length ? values[index++] : null;
            return ValueTask.FromResult((TValue)(object?)value!);
        }
    }

    private sealed class AtomicStorageJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> values = [];

        public bool RejectWrites { get; set; }
        public int SetItemCalls { get; private set; }
        public int StoredItemCount => values.Count;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            try
            {
                return Dispatch<TValue>(identifier, args);
            }
            catch (JSException)
            {
                throw;
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            try
            {
                return Dispatch<TValue>(identifier, args);
            }
            catch (JSException)
            {
                throw;
            }
        }

        private ValueTask<TValue> Dispatch<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "localStorage.setItem")
            {
                SetItemCalls++;
                if (RejectWrites)
                {
                    throw new JSException("Storage quota exceeded.");
                }

                values[(string)args![0]!] = (string)args[1]!;
                return ValueTask.FromResult(default(TValue)!);
            }

            if (identifier == "localStorage.getItem")
            {
                values.TryGetValue((string)args![0]!, out var value);
                return ValueTask.FromResult((TValue)(object?)value!);
            }

            throw new InvalidOperationException($"Unexpected JS invocation '{identifier}'.");
        }
    }

    private sealed class MemoryBrowserFile(string name, string content) : IBrowserFile
    {
        private readonly byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);

        public string Name { get; } = name;
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
        public long Size => bytes.Length;
        public string ContentType { get; } = "application/json";

        public Stream OpenReadStream(
            long maxAllowedSize = 512000,
            CancellationToken cancellationToken = default) =>
            Size > maxAllowedSize
                ? throw new IOException("The file exceeds the permitted size.")
                : new MemoryStream(bytes, writable: false);
    }
}
