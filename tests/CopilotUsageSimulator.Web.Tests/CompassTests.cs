using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using CopilotUsageSimulator.BundleTool;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;
using CopilotUsageSimulator.Web.Pages;
using CopilotUsageSimulator.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotUsageSimulator.Web.Tests;

public sealed class CompassTests : BunitContext
{
    public CompassTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string>("costCompass.saveClientProfile", _ => true).SetResult("saved");
        Services.AddSingleton(EngineConfigurationLoader.LoadDefault());
        Services.AddSingleton<ScenarioJson>();
        Services.AddSingleton<WorkloadEditorAdapter>();
        Services.AddSingleton<AttributionEditorAdapter>();
        Services.AddSingleton<EconomicEditorAdapter>();
        Services.AddSingleton<RuntimeEditorAdapter>();
        Services.AddSingleton<ActionsEditorAdapter>();
        Services.AddSingleton<ScenarioEditorAdapter>();
        Services.AddSingleton<CompassBundleCodec>();
        Services.AddSingleton<CompassClientProfileCodec>();
        Services.AddScoped<CompassBrowserPersistence>();
        Services.AddScoped<CompassPageModel>();
    }

    [Fact]
    public void DefaultDemoShowsActualBlockerRequiredCreditsAndZeroAcceptedConsumption()
    {
        var cut = Render<Compass>();

        Assert.Equal(3, cut.FindAll("main.cp-workspace > section").Count);
        Assert.Contains("Blocked", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.Contains("paid-usage", cut.Find("[data-testid=compass-blocker]").TextContent);
        Assert.Equal("100", cut.Find("[data-testid=required-credits]").TextContent);
        Assert.Equal("0", cut.Find("[data-testid=accepted-credits]").TextContent);
        Assert.Contains("60 / 60", cut.Markup);
        Assert.Contains("11 Sep 2026", cut.Markup);
        Assert.DoesNotContain("data-testid=\"compass-actions\"", cut.Markup);
    }

    [Fact]
    public void MultipleClientProfilesRequireExplicitSelectionAndKeepSelectedUserFixed()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var firstBundle = CreateClientProfileBundle(model, "contoso", "2026-09-14T10:00:00Z");
        var secondBundle = CreateClientProfileBundle(model, "fabrikam", "2026-09-14T11:00:00Z");

        Assert.Null(model.SelectedClientProfileId);
        Assert.Empty(model.ClientProfiles);
        Assert.Equal("No client profile selected", cut.Find("#cc-client-profile option").TextContent);

        var profileInput = cut.FindComponents<InputFile>().Single(component => component.Find("input").Id == "cc-profile-file");
        profileInput.UploadFiles(InputFileContent.CreateFromText(firstBundle, "contoso.compass.json"));
        profileInput.UploadFiles(InputFileContent.CreateFromText(secondBundle, "fabrikam.compass.json"));

        Assert.Equal(2, model.ClientProfiles.Count);
        Assert.Null(model.SelectedClientProfile);
        Assert.Equal(3, cut.FindAll("#cc-client-profile option").Count);
        var contoso = model.ClientProfiles.Single(profile => profile.DisplayName == "contoso");

        cut.Find("#cc-client-profile").Change(contoso.Id);

        Assert.Equal(contoso.Id, model.SelectedClientProfileId);
        Assert.Equal("contoso", model.Scenario.BillingContext!.BillingEntityId);
        Assert.True(cut.Find("#cc-user").HasAttribute("disabled"));
        Assert.Equal("user-1", model.Scenario.Attribution!.UserId);

        var originalContosoId = model.SelectedClientProfileId;
        profileInput.UploadFiles(InputFileContent.CreateFromText(
            CreateClientProfileBundle(model, "contoso", "2026-09-14T12:00:00Z"), "contoso-latest.compass.json"));

        Assert.Equal(2, model.ClientProfiles.Count);
        Assert.Equal(originalContosoId, model.SelectedClientProfileId);
        Assert.Equal(DateTimeOffset.Parse("2026-09-14T12:00:00Z"), model.SelectedClientProfile!.CapturedAt);
        Assert.Contains(model.ClientProfiles, profile => profile.DisplayName == "fabrikam");

        profileInput.UploadFiles(InputFileContent.CreateFromText(
            CreateClientProfileBundle(model, "contoso", "2026-09-14T12:00:00Z"), "renamed.compass.json"));
        Assert.Equal(2, model.ClientProfiles.Count);
        Assert.Null(model.Error);

        profileInput.UploadFiles(InputFileContent.CreateFromText(
            CreateClientProfileBundle(model, "contoso", "2026-09-14T11:00:00Z"), "contoso-older.compass.json"));
        Assert.Equal(DateTimeOffset.Parse("2026-09-14T12:00:00Z"), model.SelectedClientProfile!.CapturedAt);
        Assert.Contains("newer capture", model.Notice);

        cut.Find("#cc-remove-profile").Click();

        Assert.Single(model.ClientProfiles);
        Assert.Null(model.SelectedClientProfileId);
        Assert.Equal(2, cut.FindAll("#cc-client-profile option").Count);
    }

    [Fact]
    public void StoredClientProfilesReloadWithoutAutomaticSelection()
    {
        var catalog = Services.GetRequiredService<EngineConfiguration>();
        var template = CompassPresetFactory.Create(catalog, "chat", CompassProblem.PaidUsageDisabled);
        var bundle = CreateClientProfileBundle(template, catalog, "contoso", "2026-09-14T10:00:00Z");
        var codec = Services.GetRequiredService<CompassClientProfileCodec>();
        var stored = codec.Export([codec.Create(bundle, "contoso.compass.json", catalog)]);
        var profile = Assert.Single(codec.Import(stored, catalog));
        var document = BrowserRecord(profile, stored);
        JSInterop.Setup<ClientProfileBrowserRecord[]>(
            "costCompass.loadClientProfiles", CompassBrowserPersistence.ClientProfileStoragePrefix)
            .SetResult([document]);

        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();

        Assert.Single(model.ClientProfiles);
        Assert.Null(model.SelectedClientProfileId);
        Assert.NotEqual("contoso", model.Scenario.BillingContext!.BillingEntityId);
        Assert.Equal("", cut.Find("#cc-client-profile").GetAttribute("value"));
    }

    [Fact]
    public void MalformedStoredProfileDoesNotHideValidProfiles()
    {
        var catalog = Services.GetRequiredService<EngineConfiguration>();
        var template = CompassPresetFactory.Create(catalog, "chat", CompassProblem.PaidUsageDisabled);
        var bundle = CreateClientProfileBundle(template, catalog, "contoso", "2026-09-14T10:00:00Z");
        var codec = Services.GetRequiredService<CompassClientProfileCodec>();
        var stored = codec.Export([codec.Create(bundle, "contoso.compass.json", catalog)]);
        var profile = Assert.Single(codec.Import(stored, catalog));
        JSInterop.Setup<ClientProfileBrowserRecord[]>(
            "costCompass.loadClientProfiles", CompassBrowserPersistence.ClientProfileStoragePrefix)
            .SetResult([
                new ClientProfileBrowserRecord
                {
                    Key = $"{CompassBrowserPersistence.ClientProfileStoragePrefix}malformed",
                    Document = "{"
                },
                BrowserRecord(profile, stored)
            ]);

        Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();

        Assert.Single(model.ClientProfiles);
        Assert.Null(model.SelectedClientProfileId);
        Assert.Contains("1 invalid client profile storage record", model.Error);
    }

    [Fact]
    public void MalformedFutureStorageRecordCannotBlockAValidProfile()
    {
        var catalog = Services.GetRequiredService<EngineConfiguration>();
        var template = CompassPresetFactory.Create(catalog, "chat", CompassProblem.PaidUsageDisabled);
        var bundle = CreateClientProfileBundle(template, catalog, "contoso", "2026-09-14T10:00:00Z");
        var codec = Services.GetRequiredService<CompassClientProfileCodec>();
        var profile = codec.Create(bundle, "contoso.compass.json", catalog);
        var malformedKey = $"{CompassBrowserPersistence.ClientProfileStoragePrefix}{profile.Id}.9999999999999999999.{new string('f', 64)}";
        JSInterop.Setup<ClientProfileBrowserRecord[]>(
            "costCompass.loadClientProfiles", CompassBrowserPersistence.ClientProfileStoragePrefix)
            .SetResult([new ClientProfileBrowserRecord { Key = malformedKey, Document = "{" }]);
        var cut = Render<Compass>();

        var profileInput = cut.FindComponents<InputFile>().Single(component => component.Find("input").Id == "cc-profile-file");
        profileInput.UploadFiles(InputFileContent.CreateFromText(bundle, "contoso.compass.json"));

        var model = Services.GetRequiredService<CompassPageModel>();
        Assert.Single(model.ClientProfiles);
        Assert.Null(model.SelectedClientProfileId);
        Assert.Null(model.Error);
        var save = JSInterop.Invocations.Last(invocation => invocation.Identifier == "costCompass.saveClientProfile");
        Assert.Contains(malformedKey, Assert.IsAssignableFrom<IEnumerable<string>>(save.Arguments[6]));
    }

    [Fact]
    public void WorkloadPresetRetainsSelectedClientEnvironment()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var source = CompassPresetFactory.Create(model.Configuration, "cloud-agent") with
        {
            CheckScope = SimulationCheckScope.CostRelatedOnly
        };
        var bundle = CreateClientProfileBundle(source, model.Configuration, "contoso", "2026-09-14T10:00:00Z");
        var profileInput = cut.FindComponents<InputFile>().Single(component => component.Find("input").Id == "cc-profile-file");
        profileInput.UploadFiles(InputFileContent.CreateFromText(bundle, "contoso.compass.json"));
        var profile = Assert.Single(model.ClientProfiles);
        cut.Find("#cc-client-profile").Change(profile.Id);
        var configuration = model.Configuration;
        var timestamp = model.Scenario.Timestamp;
        var billing = model.Scenario.BillingContext;
        var attribution = model.Scenario.Attribution;
        var runtime = model.Scenario.RuntimeGuardrails;
        var actions = model.Scenario.ActionsGuardrails;

        cut.Find("[data-preset=chat]").Click();
        cut.Find("#cc-pool-used").Input("1234");
        cut.Find("[data-preset=cloud-agent]").Click();
        cut.Find("#cc-actions-used").Input("3");
        cut.Find("[data-preset=chat]").Click();
        cut.Find("[data-preset=cloud-agent]").Click();

        Assert.Equal(profile.Id, model.SelectedClientProfileId);
        Assert.Same(configuration, model.Configuration);
        Assert.Equal(timestamp, model.Scenario.Timestamp);
        Assert.Same(billing, model.Scenario.BillingContext);
        Assert.Same(attribution, model.Scenario.Attribution);
        Assert.Same(runtime, model.Scenario.RuntimeGuardrails);
        Assert.Equal(1234m, model.Scenario.EconomicGuardrails!.EnterprisePoolConsumedCredits);
        var currentActions = Assert.IsType<ActionsGuardrailSnapshot>(model.Scenario.ActionsGuardrails);
        Assert.Equivalent(actions! with { ConsumedIncludedMinutes = 3m }, currentActions, strict: true);
        Assert.Equal(SimulationCheckScope.CostRelatedOnly, model.Scenario.CheckScope);
        Assert.Equal("cloud-agent", model.Scenario.OperationId);
        Assert.Equal(currentActions.IncludedMinutes - currentActions.ConsumedIncludedMinutes,
            model.Scenario.ActionsUsage!.IncludedMinutesRemaining);
        Assert.True(cut.Find("#cc-problem").HasAttribute("disabled"));

        cut.Find("#cc-simulate").Click();

        Assert.Null(model.Error);
        Assert.NotNull(model.Preview);
    }

    [Fact]
    public void CloudAgentPresetRequiresClientActionsEvidence()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var bundle = CreateClientProfileBundle(model, "contoso", "2026-09-14T10:00:00Z");
        var profileInput = cut.FindComponents<InputFile>().Single(component => component.Find("input").Id == "cc-profile-file");
        profileInput.UploadFiles(InputFileContent.CreateFromText(bundle, "contoso.compass.json"));
        var profile = Assert.Single(model.ClientProfiles);
        cut.Find("#cc-client-profile").Change(profile.Id);

        cut.Find("[data-preset=cloud-agent]").Click();

        Assert.Equal(profile.Id, model.SelectedClientProfileId);
        Assert.Equal("chat", model.Scenario.OperationId);
        Assert.Contains("no reconciled Actions", model.Error);
    }

    [Fact]
    public void SimulateIsRepeatableAndDoesNotAdvanceBalances()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var json = Services.GetRequiredService<ScenarioJson>();
        var before = json.Serialize(model.Scenario);

        cut.Find("#cc-simulate").Click();
        cut.Find("#cc-simulate").Click();

        Assert.Equal(before, json.Serialize(model.Scenario));
        Assert.Equal(5_740m, model.Scenario.EconomicGuardrails!.EnterprisePoolConsumedCredits);
        Assert.Equal("100", cut.Find("[data-testid=required-credits]").TextContent);
    }

    [Fact]
    public void FirstBlockerHighlightsPaidSettingAndClearsWhenEdited()
    {
        var cut = Render<Compass>();

        Assert.Equal("cc-paid", cut.Find(".cp-field-blocking select").Id);
        Assert.Equal("cc-paid-blocker", cut.Find("#cc-paid").GetAttribute("aria-describedby"));
        Assert.Contains("First blocker", cut.Find("#cc-paid-blocker").TextContent);

        cut.Find("#cc-paid").Change("Enabled");

        Assert.Empty(cut.FindAll(".cp-field-blocking, .cp-setting-blocker"));
        cut.Find("#cc-simulate").Click();
        Assert.Contains("Allowed", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.Empty(cut.FindAll(".cp-field-blocking, .cp-setting-blocker"));
    }

    [Theory]
    [InlineData("UserBudget", "cc-ulb-limit", "cc-financial-settings")]
    [InlineData("AiBudget", "cc-budget-limit", "cc-financial-settings")]
    [InlineData("ActionsBudget", "cc-actions-budget", "cc-actions-settings")]
    public void BudgetBlockerHighlightsItsLimitAndExpandsItsSection(string problem, string fieldId, string sectionId)
    {
        var cut = Render<Compass>();

        cut.Find("#cc-problem").Change(problem);
        cut.Find("#cc-simulate").Click();

        Assert.Equal(fieldId, cut.Find(".cp-field-blocking input").Id);
        Assert.Contains($"{fieldId}-blocker", cut.Find($"#{fieldId}").GetAttribute("aria-describedby"));
        Assert.True(cut.Find($"#{sectionId}").HasAttribute("open"));
        Assert.Single(cut.FindAll(".cp-setting-blocker"));
    }

    [Fact]
    public void AccessBlockerExpandsAndHighlightsTheFailingGate()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var gate = model.Configuration.Gates.First(candidate => candidate.ApplicableOperationIds.Count == 0 ||
            candidate.ApplicableOperationIds.Contains("chat"));

        cut.Find($"[id='cc-gate-{gate.Id}']").Change(false);
        cut.Find("#cc-simulate").Click();

        Assert.Equal(gate.Id, model.Preview!.Result!.FirstFailingGate);
        Assert.Equal($"cc-gate-{gate.Id}", model.BlockingSettingId);
        Assert.True(cut.Find("#cc-access-settings").HasAttribute("open"));
        Assert.Equal($"cc-gate-{gate.Id}", cut.Find(".cp-field-blocking input").Id);
    }

    [Fact]
    public void ReusedRecordIdHighlightsFailedBudgetInsteadOfEarlierPassedUserLimit()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var scenario = CompassPresetFactory.Create(model.Configuration, "chat", CompassProblem.AiBudget);
        var budgetId = scenario.EconomicGuardrails!.SpendingBudgets.Single(budget => budget.Scope == SpendingBudgetScope.CostCenter).Id;
        ImportLegacy(cut, scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails with
            {
                UserLevelBudgets = scenario.EconomicGuardrails.UserLevelBudgets.Select(budget =>
                    budget.Kind == UserLevelBudgetKind.Individual && budget.TargetId == "user-1" ? budget with { Id = budgetId } : budget).ToArray()
            }
        });

        cut.Find("#cc-simulate").Click();

        Assert.Equal(2, model.Preview!.Result!.AppliedGuardrails.Count(guardrail => guardrail.Id == budgetId));
        Assert.Equal(GuardrailOutcome.Blocked, model.BlockingGuardrail!.Outcome);
        Assert.Equal("cc-budget-limit", cut.Find(".cp-field-blocking input").Id);
        Assert.Contains(model.BlockingGuardrail.Message, cut.Find("[data-testid=compass-blocker]").TextContent);
    }

    [Fact]
    public void ReviewBlockerRequestsFocusWithoutChangingTheScenario()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var serializer = Services.GetRequiredService<ScenarioJson>();
        var before = serializer.Serialize(model.Scenario);
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "costCompass.revealSetting");

        cut.Find("#cc-review-blocker").Click();

        var invocation = Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier == "costCompass.revealSetting");
        Assert.Equal("cc-paid", Assert.Single(invocation.Arguments));
        Assert.Equal(before, serializer.Serialize(model.Scenario));
        Assert.Equal("paid-usage", model.Preview!.Result!.FirstFailingGate);
    }

    [Fact]
    public void SnapshotOnlyBlockerShowsItsExactRecordAndCanOpenTheBundleEditor()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var scenario = CompassPresetFactory.Create(model.Configuration, "chat", CompassProblem.AiBudget);
        ImportLegacy(cut, scenario with
        {
            EconomicGuardrails = scenario.EconomicGuardrails! with
            {
                SpendingBudgets = [new SpendingBudget { Id = "imported-finance-stop", Scope = SpendingBudgetScope.Enterprise, LimitUsd = .1m, ConsumedUsd = 0m, Enforcement = GuardrailEnforcement.HardStop }]
            }
        });
        cut.Find("#cc-simulate").Click();
        var before = model.CreateBundle();

        var highlighted = cut.Find(".cp-field-blocking");
        Assert.Equal("cc-blocking-setting", highlighted.Id);
        Assert.Contains("Enterprise budget", highlighted.TextContent);
        Assert.Contains("imported-finance-stop", highlighted.TextContent);
        Assert.Contains("0.1 USD", highlighted.TextContent);
        Assert.Empty(cut.FindAll(".cp-field-blocking input, .cp-field-blocking select"));

        cut.Find("#cc-review-blocker").Click();
        cut.Find("#cc-inspect-blocker").Click();

        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "costCompass.revealSetting" &&
            Equals(invocation.Arguments.Single(), "cc-blocking-setting"));
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "costCompass.revealSetting" &&
            Equals(invocation.Arguments.Single(), "cc-bundle-text"));
        Assert.Contains("imported-finance-stop", cut.Find("#cc-bundle-text").GetAttribute("value"));
        Assert.Equal(before, model.CreateBundle());
    }

    [Fact]
    public void LaterActionsBlockerDoesNotHighlightTheFirstEditableBudget()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var scenario = CompassPresetFactory.Create(model.Configuration, "cloud-agent");
        ImportLegacy(cut, scenario with
        {
            ActionsGuardrails = scenario.ActionsGuardrails! with
            {
                Budgets = [new ActionsSpendingBudget { Id = "first-budget", LimitUsd = 100m }, new ActionsSpendingBudget { Id = "actual-actions-stop", LimitUsd = 0m, Enforcement = GuardrailEnforcement.HardStop }]
            }
        });

        cut.Find("#cc-simulate").Click();

        Assert.Equal("actual-actions-stop", model.Preview!.Result!.FirstFailingGate);
        Assert.Equal("cc-blocking-setting", model.BlockingSettingId);
        Assert.Contains("actual-actions-stop", cut.Find(".cp-field-blocking").TextContent);
        Assert.Empty(cut.FindAll(".cp-field-blocking #cc-actions-budget"));
    }

    [Fact]
    public void PaidProductRestrictionDoesNotMisidentifyTheEnabledPolicyDropdown()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        ImportLegacy(cut, model.Scenario with
        {
            EconomicGuardrails = model.Scenario.EconomicGuardrails! with
            {
                PaidUsage = new PaidUsageAuthorization { State = GuardrailValue.Enabled, ProductIds = new HashSet<string> { "another-product" } }
            }
        });

        cut.Find("#cc-simulate").Click();

        Assert.Equal("paid-usage.not-applicable", model.Preview!.Result!.FirstFailingGate);
        Assert.Equal("cc-blocking-setting", model.BlockingSettingId);
        Assert.Empty(cut.FindAll(".cp-field-blocking #cc-paid"));
        Assert.Contains("product and SKU", cut.Find(".cp-field-blocking").TextContent);
    }

    [Fact]
    public async Task ToolOutputImportsThroughPrimaryFileWorkflowWithoutAdvancingBalances()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "EnterpriseImport");
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await new BundleToolApplication().RunAsync([
            "create", "--snapshot", Path.Combine(directory, "snapshot.json"),
            "--workload", Path.Combine(directory, "workload.json"),
            "--overrides", Path.Combine(directory, "overrides.json"), "--output", "-"
        ], output, error);
        Assert.True(exitCode == 0, error.ToString());
        var bundle = output.ToString();
        var expected = BundleInspector.Validate(bundle);
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        Assert.True(model.Import(bundle));
        var profile = Services.GetRequiredService<CompassClientProfileCodec>()
            .Create(bundle, "enterprise-compass.json", model.Configuration);
        Assert.Equal(expected.Enterprise, profile.DisplayName);
        Assert.Equal(expected.User, profile.SelectedUser);
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(bundle, "enterprise-compass.json"));
        Assert.Null(model.Error);
        Assert.Null(model.EvidenceWarning);
        Assert.Equal(expected.CatalogSha256, model.CatalogFingerprint);
        Assert.Equal("alice", model.Scenario.Attribution!.UserId);
        Assert.Equal(2, model.Scenario.BillingContext!.SeatAssignments.Count);
        Assert.Contains(model.Scenario.BillingContext.SeatAssignments, seat => seat.CostCenterId == "cc-research");
        var before = Services.GetRequiredService<ScenarioJson>().Serialize(model.Scenario);

        cut.Find("#cc-simulate").Click();
        cut.Find("#cc-simulate").Click();

        Assert.Equal(before, Services.GetRequiredService<ScenarioJson>().Serialize(model.Scenario));
        Assert.Equal(expected.PreviewDecision, model.Preview!.Result!.Decision.ToString());
        Assert.Equal("cc-spend", model.Preview.Result.FirstFailingGate);
        Assert.Equal(200m, Assert.Single(model.Scenario.EconomicGuardrails!.UserLevelBudgets).ConsumedCredits);
        Assert.Equal(5740m, model.Scenario.EconomicGuardrails.EnterprisePoolConsumedCredits);
        Assert.Equal(.8m, model.Scenario.EconomicGuardrails.SpendingBudgets.Single(budget => budget.Id == "cc-spend").ConsumedUsd);
        Assert.Equal("100", cut.Find("[data-testid=required-credits]").TextContent);
        Assert.Equal("0", cut.Find("[data-testid=accepted-credits]").TextContent);
    }

    [Fact]
    public void DecisionFlowIsEmbeddedInPrimaryCompassPage()
    {
        var cut = Render<Compass>();

        Assert.NotNull(cut.Find("#cc-decision-flow"));
        Assert.Equal("#cc-decision-flow", cut.Find("nav[aria-label='Compass navigation'] a").GetAttribute("href"));
        Assert.Equal("AI-credit decision flow", cut.Find("#cc-flow-title").TextContent);

        cut.Find("nav[aria-label='Compass navigation'] a").Click();

        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "decisionFlow.reveal");
    }

    [Fact]
    public void LegacyPageLinksAreDisabledWhileCompassNavigationRemainsAvailable()
    {
        var cut = Render<Compass>();

        Assert.Empty(cut.FindAll("a[href='advanced'], a[href='guide']"));
        var disabledLinks = cut.FindAll("a[role='link'][aria-disabled='true']");
        Assert.Equal(3, disabledLinks.Count);
        Assert.All(disabledLinks, link =>
        {
            Assert.False(link.HasAttribute("href"));
            Assert.False(link.HasAttribute("tabindex"));
            Assert.False(link.HasAttribute("onclick"));
            Assert.NotEmpty(link.GetAttribute("title")!);
        });
        Assert.Equal(["Advanced simulator", "User guide"], cut.FindAll("nav[aria-label='Compass navigation'] [aria-disabled='true']")
            .Select(link => link.TextContent).ToArray());
        Assert.NotNull(cut.Find("a[href='#cc-decision-flow']"));
        Assert.NotNull(cut.Find("a[href='#cc-evidence']"));
    }

    [Fact]
    public void EditingChildConfigurationInvalidatesParentVerdictAndComparisons()
    {
        var cut = Render<Compass>();
        cut.Find("#cc-compare").Click();

        cut.Find("#cc-output").Input("24000");

        Assert.Contains("Needs simulation", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=required-credits]"));
        Assert.Empty(cut.FindAll("[data-comparison]"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("999999999999999999999999999999")]
    public void InvalidTokenInputDoesNotSilentlyReuseASuccessfulValue(string value)
    {
        var cut = Render<Compass>();
        cut.Find("#cc-paid").Change("Enabled");
        cut.Find("#cc-simulate").Click();
        Assert.Contains("Allowed", cut.Find("[data-testid=compass-verdict]").TextContent);

        cut.Find("#cc-input").Input(value);

        Assert.Equal("true", cut.Find("#cc-input").GetAttribute("aria-invalid"));
        Assert.True(cut.Find("#cc-simulate").HasAttribute("disabled"));
        Assert.Contains("Needs simulation", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.NotEmpty(cut.FindAll("[role=alert]"));

        cut.Find("#cc-input").Input("30000");
        Assert.False(cut.Find("#cc-simulate").HasAttribute("disabled"));
    }

    [Fact]
    public void FullyIncludedRequestIsNotBlockedMerelyBecausePaidUsageIsDisabled()
    {
        var cut = Render<Compass>();

        cut.Find("#cc-input").Input("30000");
        cut.Find("#cc-output").Input("24000");
        cut.Find("#cc-simulate").Click();

        Assert.Contains("Allowed", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.Equal("60", cut.Find("[data-testid=required-credits]").TextContent);
        Assert.Equal("Disabled", cut.Find("#cc-paid").GetAttribute("value"));
    }

    [Fact]
    public void EnablingPaidUsageReevaluatesTheLaterBudgetInsteadOfPromisingSuccess()
    {
        var cut = Render<Compass>();
        cut.Find("#cc-budget-limit").Input("0.20");
        cut.Find("#cc-budget-used").Input("0");
        cut.Find("#cc-simulate").Click();
        Assert.Contains("paid-usage", cut.Find("[data-testid=compass-blocker]").TextContent);
        Assert.Equal("cc-paid", cut.Find(".cp-field-blocking select").Id);

        cut.Find("#cc-paid").Change("Enabled");
        cut.Find("#cc-simulate").Click();

        Assert.Contains("budget-cost-center", cut.Find("[data-testid=compass-blocker]").TextContent);
        Assert.Equal("cc-budget-limit", cut.Find(".cp-field-blocking input").Id);
        Assert.Equal("0", cut.Find("[data-testid=accepted-credits]").TextContent);
        Assert.Equal("$0.4000", cut.Find("[data-testid=potential-usd]").TextContent);
    }

    [Fact]
    public void RaisingSpendingBudgetDoesNotResolveAnEarlierUserLimit()
    {
        var cut = Render<Compass>();
        cut.Find("#cc-problem").Change("UserBudget");
        cut.Find("#cc-budget-limit").Input("99999");
        cut.Find("#cc-simulate").Click();

        Assert.Contains("ulb-user-1", cut.Find("[data-testid=compass-blocker]").TextContent);
    }

    [Fact]
    public void AlternativesAreVerifiedAndAppliedOnlyToTheLocalDraft()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var serializer = Services.GetRequiredService<ScenarioJson>();
        var baseline = serializer.Serialize(model.Scenario);

        cut.Find("#cc-compare").Click();

        Assert.Equal(baseline, serializer.Serialize(model.Scenario));
        var smaller = model.Comparisons.First(comparison =>
            comparison.Resolved &&
            comparison.CandidateScenario.EconomicGuardrails!.PaidUsage.State == GuardrailValue.Disabled);
        cut.Find($"[data-comparison='{smaller.Id}'] button").Click();

        Assert.Contains("Needs simulation", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.NotEqual(baseline, serializer.Serialize(model.Scenario));
        Assert.Equal(5_740m, model.Scenario.EconomicGuardrails!.EnterprisePoolConsumedCredits);
        cut.Find("#cc-simulate").Click();
        Assert.Contains("Allowed", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.Equal("$0.0000", cut.Find("[data-testid=potential-usd]").TextContent);
    }

    [Fact]
    public void ExplicitlyOmittingAnInvalidOptionalControlRemovesItsValidationError()
    {
        var cut = Render<Compass>();
        cut.Find("#cc-ulb-limit").Input("");
        Assert.True(cut.Find("#cc-simulate").HasAttribute("disabled"));

        cut.Find("#cc-use-ulb").Change(false);

        Assert.False(cut.Find("#cc-simulate").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#cc-ulb-limit"));
    }

    [Fact]
    public void CostReducingCandidateForAnAlreadyAllowedRequestIsNotLabeledBlocked()
    {
        var cut = Render<Compass>();
        cut.Find("#cc-paid").Change("Enabled");
        cut.Find("#cc-simulate").Click();

        cut.Find("#cc-compare").Click();

        var comparison = cut.Find("[data-comparison=reduce-to-included]");
        Assert.Contains("Allowed alternative", comparison.TextContent);
        Assert.DoesNotContain("Still limited", comparison.TextContent);
    }

    [Fact]
    public void CloudAgentAndCliPresetControlsUseDifferentMeters()
    {
        var cut = Render<Compass>();

        cut.Find("[data-preset=cloud-agent]").Click();
        Assert.Single(cut.FindAll("[data-testid=compass-actions]"));
        Assert.Contains("already-accounted", cut.Markup);
        cut.Find("#cc-simulate").Click();
        Assert.Contains("Allowed", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.Equal("$0.0480", cut.Find("[data-testid=potential-usd]").TextContent);

        cut.Find("[data-preset=cli]").Click();
        Assert.Empty(cut.FindAll("[data-testid=compass-actions]"));
        cut.Find("#cc-simulate").Click();
        Assert.Contains("Allowed", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.Equal("$0.0000", cut.Find("[data-testid=potential-usd]").TextContent);
    }

    [Fact]
    public void PlanAndUserSelectionKeepTheSameSeatIdentityAndOtherUsersRecords()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();

        cut.Find("#cc-user").Change("user-2");
        Assert.Equal("enterprise", model.Form.Workload.PlanId);
        Assert.False(model.Form.Economic.UseIndividualUlb);
        cut.Find("#cc-plan").Change("business");
        cut.Find("#cc-simulate").Click();

        Assert.Equal("user-2", model.Scenario.Attribution!.UserId);
        Assert.Equal("business", model.Scenario.PlanId);
        Assert.All(model.Scenario.BillingContext!.SeatAssignments, seat => Assert.Equal("business", seat.PlanId));
        Assert.Contains(model.Scenario.EconomicGuardrails!.UserLevelBudgets,
            budget => budget.Id == "ulb-user-1" && budget.TargetId == "user-1" && budget.ConsumedCredits == 100m);
    }

    [Fact]
    public void CostOnlyTraceExplicitlyExcludesAccessAndRuntime()
    {
        var cut = Render<Compass>();

        cut.Find("#cc-cost-only").Change(true);
        cut.Find("#cc-simulate").Click();

        Assert.NotEmpty(cut.FindAll("[data-state=Excluded]"));
        Assert.Contains("Excluded (cost-only)", cut.Markup);
        Assert.NotEmpty(cut.FindAll("[data-state=NotEvaluated]"));
    }

    [Fact]
    public void CostCenterEditsKeepTheDisplayedControlAndSelectedSeatAligned()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();

        cut.Find("#cc-cost-center").Input("cc-research");
        cut.Find("#cc-simulate").Click();

        Assert.Equal("cc-research", model.Scenario.Attribution!.DirectAssignments.Single().CostCenterId);
        Assert.Equal("cc-research", model.Scenario.BillingContext!.SeatAssignments.Single(seat => seat.UserId == "user-1").CostCenterId);
        Assert.Equal("cc-engineering", model.Scenario.BillingContext.SeatAssignments.Single(seat => seat.UserId == "user-2").CostCenterId);
        Assert.Equal("cc-research", model.Scenario.EconomicGuardrails!.SpendingBudgets.Single(budget => budget.Id == "budget-cost-center").ScopeId);
        Assert.Equal("cc-research", model.Preview!.Result!.Attribution!.CostCenterId);
    }

    [Fact]
    public void ExportCapturesUnsimulatedFormChangesAndMatchingReference()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        cut.Find("#cc-input").Input("12345");

        cut.Find("#cc-inspect-bundle").Click();

        var bundle = cut.Find("#cc-bundle-text").GetAttribute("value") ?? cut.Find("#cc-bundle-text").TextContent;
        var imported = Services.GetRequiredService<CompassBundleCodec>().Import(bundle, model.Configuration);
        Assert.Equal(12_345, imported.Scenario.Calls[0].FreshInputTokens);
        Assert.Equal(model.Configuration.Version, imported.Catalog.Version);
        Assert.Null(model.Preview);
    }

    [Fact]
    public void MalformedBundlePreservesScenarioAndCatalogButInvalidatesVerdict()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var serializer = Services.GetRequiredService<ScenarioJson>();
        var before = serializer.Serialize(model.Scenario);
        var fingerprint = model.CatalogFingerprint;

        cut.Find("#cc-bundle-text").Input("{not valid");
        cut.Find("#cc-import-text").Click();

        Assert.Equal(before, serializer.Serialize(model.Scenario));
        Assert.Equal(fingerprint, model.CatalogFingerprint);
        Assert.Contains("preserved", cut.Find("[role=alert]").TextContent);
        Assert.Contains("Needs simulation", cut.Find("[data-testid=compass-verdict]").TextContent);
    }

    [Fact]
    public void MismatchedCatalogBundleCannotReplaceWorkingState()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        var originalCatalog = model.Configuration;
        var bundle = JsonNode.Parse(model.CreateBundle()!)!;
        bundle["catalog"]!["usdPerCredit"] = .02m;

        cut.Find("#cc-bundle-text").Input(bundle.ToJsonString());
        cut.Find("#cc-import-text").Click();

        Assert.Same(originalCatalog, model.Configuration);
        Assert.Contains("fingerprint", cut.Find("[role=alert]").TextContent);
        Assert.Null(model.Preview);
    }

    [Fact]
    public void ImportedMissingCallsRemainPartiallySimulatedInsteadOfManufacturingAFreeCall()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        ImportLegacy(cut, model.Scenario with { Calls = [] });

        cut.Find("#cc-simulate").Click();

        Assert.Empty(model.Scenario.Calls);
        Assert.Equal(SimulationDecision.PartiallySimulated, model.Preview!.Result!.Decision);
        Assert.Contains("Partially simulated", cut.Find("[data-testid=compass-verdict]").TextContent);
        Assert.Equal("Not priced", cut.Find("[data-testid=required-credits]").TextContent);
    }

    [Fact]
    public void ImportedMissingSeatIsNotAutomaticallyAssignedOnPreview()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        ImportLegacy(cut, model.Scenario with
        {
            BillingContext = model.Scenario.BillingContext! with
            {
                SeatAssignments = model.Scenario.BillingContext!.SeatAssignments
                    .Where(seat => seat.UserId != "user-1").ToArray()
            }
        });

        cut.Find("#cc-output").Input("24000");
        cut.Find("#cc-simulate").Click();

        Assert.DoesNotContain(model.Scenario.BillingContext!.SeatAssignments, seat => seat.UserId == "user-1");
        Assert.Equal(SimulationDecision.Indeterminate, model.Preview!.Result!.Decision);
        Assert.Equal("seat-assignment.missing", model.Preview.Result.FirstFailingGate);
    }

    [Fact]
    public void ImportedOrganizationAttributionDoesNotBecomeAnUnrequestedDirectAssignment()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        ImportLegacy(cut, model.Scenario with
        {
            Attribution = model.Scenario.Attribution! with
            {
                DirectAssignments = [],
                OrganizationAssignments =
                [
                    new EffectiveOrganizationCostCenterAssignment
                    {
                        OrganizationId = "org-engineering",
                        CostCenterId = "cc-engineering"
                    }
                ]
            }
        });

        cut.Find("#cc-budget-limit").Input("50");
        cut.Find("#cc-simulate").Click();

        Assert.Empty(model.Scenario.Attribution!.DirectAssignments);
        Assert.Equal(AttributionRule.LicensingOrganization, model.Preview!.Result!.Attribution!.Rule);
    }

    [Fact]
    public void ImportedMissingActionsPermissionsAreNotReplacedWithPassingDefaults()
    {
        var cut = Render<Compass>();
        var model = Services.GetRequiredService<CompassPageModel>();
        ImportLegacy(cut, CompassPresetFactory.Create(model.Configuration, "cloud-agent") with { ActionsGuardrails = null });
        cut.Find("#cc-actions-minutes").Input("20");

        cut.Find("#cc-simulate").Click();

        Assert.Null(model.Scenario.ActionsGuardrails);
        Assert.NotNull(model.Preview!.ErrorCode);
        Assert.Null(model.Preview.Result);
        Assert.Contains("No supported estimate", cut.Find("[data-testid=compass-verdict]").TextContent);
    }

    [Fact]
    public void ResetClearsInvalidFieldsAndRestoresPinnedDemoWithoutTouchingBrowserSave()
    {
        var cut = Render<Compass>();
        cut.Find("#cc-input").Input("");

        cut.Find("#cc-reset").Click();
        cut.Find("#cc-simulate").Click();

        Assert.Empty(cut.FindAll("[role=alert]"));
        Assert.False(cut.Find("#cc-simulate").HasAttribute("disabled"));
        Assert.Equal("100", cut.Find("[data-testid=required-credits]").TextContent);
        Assert.Contains("paid-usage", cut.Find("[data-testid=compass-blocker]").TextContent);
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "localStorage.setItem");
    }

    [Fact]
    public void AllEditableControlsHaveAssociatedLabels()
    {
        var cut = Render<Compass>();

        foreach (var input in cut.FindAll("input, select, textarea"))
        {
            var id = input.Id;
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.NotEmpty(cut.FindAll($"label[for='{id}']"));
        }
    }

    [Fact]
    public void CompassOwnsRootAndOriginalSimulatorHasAdvancedRoute()
    {
        var compassRoutes = typeof(Compass).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>();
        var advancedRoutes = typeof(Home).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>();

        Assert.Contains(compassRoutes, route => route.Template == "/");
        Assert.Contains(compassRoutes, route => route.Template == "/compass");
        Assert.Contains(advancedRoutes, route => route.Template == "/advanced");
        Assert.DoesNotContain(advancedRoutes, route => route.Template == "/");
    }

    private void ImportLegacy(IRenderedComponent<Compass> cut, SimulationScenario scenario)
    {
        cut.Find("#cc-bundle-text").Input(Services.GetRequiredService<ScenarioJson>().Serialize(scenario));
        cut.Find("#cc-import-text").Click();
    }

    private string CreateClientProfileBundle(CompassPageModel model, string enterprise, string capturedAt)
        => CreateClientProfileBundle(model.Scenario, model.Configuration, enterprise, capturedAt);

    private string CreateClientProfileBundle(
        SimulationScenario source,
        EngineConfiguration configuration,
        string enterprise,
        string capturedAt)
    {
        var timestamp = DateTimeOffset.Parse(capturedAt);
        var metadata = new Dictionary<string, string>(source.Metadata)
        {
            [CompassClientProfileMetadata.ContractKey] = CompassClientProfileMetadata.Contract,
            [CompassClientProfileMetadata.EnterpriseKey] = enterprise,
            [CompassClientProfileMetadata.SelectedUserKey] = source.Attribution!.UserId,
            [CompassClientProfileMetadata.CaptureStartedAtKey] = timestamp.AddMinutes(-1).ToString("O"),
            [CompassClientProfileMetadata.CaptureCompletedAtKey] = timestamp.ToString("O"),
            [CompassClientProfileMetadata.SnapshotSha256Key] = new string('a', 64),
            [CompassClientProfileMetadata.OverridesSha256Key] = new string('b', 64),
            [CompassClientProfileMetadata.ScopeKey] = CompassClientProfileMetadata.Scope
        };
        var scenario = source with
        {
            Timestamp = timestamp,
            BillingContext = source.BillingContext! with { BillingEntityId = enterprise },
            Metadata = metadata
        };
        var bundles = Services.GetRequiredService<CompassBundleCodec>();
        return bundles.Export(
            CompassClientProfileMetadata.AddIntegrityDigest(scenario, configuration, bundles), configuration);
    }

    private static ClientProfileBrowserRecord BrowserRecord(CompassClientProfile profile, string document) => new()
    {
        Key = CompassBrowserPersistence.RecordKey(profile),
        Document = document
    };
}
