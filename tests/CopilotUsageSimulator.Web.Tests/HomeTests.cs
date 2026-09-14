using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Bunit;
using CopilotUsageSimulator.Engine;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;
using CopilotUsageSimulator.Web.Pages;
using CopilotUsageSimulator.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotUsageSimulator.Web.Tests;

public sealed class HomeTests : BunitContext
{
    public HomeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var configuration = EngineConfigurationLoader.LoadDefault();
        Services.AddSingleton(configuration);
        Services.AddSingleton<ICopilotUsageSimulationEngine>(
            new CopilotUsageSimulationEngine(configuration));
        Services.AddSingleton<SimulationSessionRunner>();
        Services.AddSingleton<ScenarioJson>();
        Services.AddSingleton<WorkloadEditorAdapter>();
        Services.AddSingleton<AttributionEditorAdapter>();
        Services.AddSingleton<EconomicEditorAdapter>();
        Services.AddSingleton<RuntimeEditorAdapter>();
        Services.AddSingleton<ActionsEditorAdapter>();
        Services.AddSingleton<ScenarioEditorAdapter>();
        Services.AddScoped<BrowserScenarioPersistence>();
        Services.AddScoped<HomePageModel>();
    }

    [Fact]
    public void DefaultScenarioRendersAllowedInCostOnlyMode()
    {
        var cut = Render<Home>();

        Assert.True(cut.Find(".scope-card input[type=checkbox]").IsChecked());
        Assert.Contains("Allowed", cut.Find(".decision-banner").TextContent);
        Assert.DoesNotContain("Runtime guardrails", cut.Markup);
        Assert.Contains("Cost-related checks only", cut.Markup);
    }

    [Fact]
    public void DisablingCostOnlyModeRevealsRuntimeControls()
    {
        var cut = Render<Home>();

        cut.Find(".scope-card input[type=checkbox]").Change(false);

        Assert.Contains("Runtime guardrails", cut.Markup);
        Assert.Contains("Maximum model calls", cut.Markup);
        Assert.Contains("Agent runtime limits", cut.Markup);
    }

    [Fact]
    public void RepeatedSimulationAdvancesWorkingBalances()
    {
        var cut = Render<Home>();
        FindInputByLabel(cut, "Repeat task").Change("3");

        FindButton(cut, "Apply overrides and simulate").Click();

        Assert.Contains("3 / 3", cut.Markup);
        Assert.Contains("Working balances advanced by 3 successful run(s).", cut.Markup);
        Assert.Contains("Run history (3 evaluated)", cut.Markup);
    }

    [Fact]
    public void ChangingPlanUpdatesEffectiveSeatAndIncludedCreditEntitlement()
    {
        var serializer = new ScenarioJson();
        var cut = Render<Home>();
        FindInputByLabel(cut, "Pool consumed").Input("5800");

        FindButton(cut, "Apply overrides and simulate").Click();

        Assert.Equal("0", FindMetricValue(cut, "Included credits"));

        cut.Find("#plan").Change("enterprise");
        FindButton(cut, "Apply overrides and simulate").Click();

        var editor = Assert.IsAssignableFrom<IHtmlTextAreaElement>(
            cut.Find("textarea.json-editor:not(.catalog-editor)"));
        var json = editor.GetAttribute("value") ?? editor.TextContent;
        var scenario = serializer.Deserialize(json);
        var effectiveSeat = scenario.BillingContext!.SeatAssignments.Single(seat =>
            string.Equals(seat.UserId, scenario.Attribution!.UserId, StringComparison.OrdinalIgnoreCase) &&
            scenario.Timestamp >= seat.EffectiveFrom &&
            (seat.EffectiveTo is null || scenario.Timestamp < seat.EffectiveTo));

        Assert.Equal("enterprise", scenario.PlanId);
        Assert.Equal("enterprise", effectiveSeat.PlanId);
        Assert.NotEqual("0", FindMetricValue(cut, "Included credits"));
    }

    [Fact]
    public void BlockingUlbHighlightsItsGuidedSetting()
    {
        var cut = Render<Home>();
        FindInputByLabel(cut, "Credit limit", "#individual-ulb-settings").Input("101");

        FindButton(cut, "Apply overrides and simulate").Click();

        var individualUlb = cut.Find("#individual-ulb-settings");
        Assert.True(individualUlb.ClassList.Contains("impacted-setting"));
        Assert.Contains("Why it stopped", cut.Markup);
        Assert.Contains("ulb-user-1", cut.Find(".blocking-explanation").TextContent);
        Assert.Contains("Individual ULB", cut.Find(".blocking-explanation").TextContent);
        Assert.Contains(
            "The effective ULB for this user stopped the run.",
            cut.Find(".blocking-explanation").TextContent);
    }

    [Fact]
    public void InvalidRepeatCountShowsSimulationError()
    {
        var cut = Render<Home>();
        FindInputByLabel(cut, "Repeat task").Change("0");

        FindButton(cut, "Apply overrides and simulate").Click();

        var error = cut.Find(".message.error").TextContent;
        Assert.Contains("repeat-count-invalid", error);
        Assert.Contains("between 1 and 1000", error);
    }

    [Fact]
    public void GuidedOverridesPreserveAdvancedGuardrailConfiguration()
    {
        var serializer = new ScenarioJson();
        var original = ExampleScenarioFactory.Create(
            EngineConfigurationLoader.LoadDefault(),
            "cloud-agent");
        var timestamp = original.Timestamp;
        var primaryCall = original.Calls.Single() with
        {
            EnabledMultiplierIds = ["auto-model-selection"],
            Metadata = new Dictionary<string, string> { ["phase"] = "primary" }
        };
        var extraCall = new ModelCallInput
        {
            ModelId = "claude-sonnet-5",
            ContextTokens = 20_000,
            FreshInputTokens = 15_000,
            Metadata = new Dictionary<string, string> { ["phase"] = "secondary" }
        };
        var historicalAssignment = new EffectiveCostCenterAssignment
        {
            CostCenterId = "cc-legacy",
            EffectiveFrom = timestamp.AddMonths(-2),
            EffectiveTo = timestamp.AddMonths(-1)
        };
        var currentAssignment = new EffectiveCostCenterAssignment
        {
            CostCenterId = "cc-engineering",
            EffectiveFrom = timestamp.AddMonths(-1),
            EffectiveTo = timestamp.AddMonths(1)
        };
        var currentSeat = original.BillingContext!.SeatAssignments
            .Single(seat => seat.UserId == "user-1") with
        {
            EffectiveFrom = timestamp.AddMonths(-1),
            EffectiveTo = timestamp.AddMonths(1)
        };
        var historicalSeat = currentSeat with
        {
            CostCenterId = "cc-legacy",
            EffectiveFrom = timestamp.AddMonths(-2),
            EffectiveTo = timestamp.AddMonths(-1)
        };
        var primaryBudget = original.EconomicGuardrails!.SpendingBudgets
            .Single(budget => budget.Scope == SpendingBudgetScope.CostCenter) with
        {
            Id = "custom-cost-center-budget",
            ProductIds = new HashSet<string> { "github-copilot" },
            SkuIds = new HashSet<string> { "copilot-ai-credits" },
            EffectiveFrom = timestamp.AddDays(-10),
            EffectiveTo = timestamp.AddDays(10),
            TrackingStartedAt = timestamp.AddDays(-5)
        };
        var extraBudget = new SpendingBudget
        {
            Id = "secondary-cost-center-budget",
            Scope = SpendingBudgetScope.CostCenter,
            ScopeId = "cc-secondary",
            LimitUsd = 50m,
            ProductIds = new HashSet<string> { "another-product" },
            EffectiveFrom = timestamp.AddDays(-20)
        };
        var expiredBudget = primaryBudget with
        {
            Id = "expired-cost-center-budget",
            LimitUsd = 10m,
            EffectiveFrom = timestamp.AddDays(-20),
            EffectiveTo = timestamp.AddDays(-10)
        };
        var mismatchedBudget = primaryBudget with
        {
            Id = "mismatched-cost-center-budget",
            LimitUsd = 20m,
            ProductIds = new HashSet<string> { "another-product" }
        };
        var primaryControl = original.EconomicGuardrails.IncludedUsageControls.Single() with
        {
            Id = "custom-included-control",
            EffectiveFrom = timestamp.AddDays(-10),
            EffectiveTo = timestamp.AddDays(10)
        };
        var extraControl = new CostCenterIncludedUsageControl
        {
            Id = "secondary-included-control",
            CostCenterId = "cc-secondary",
            ConsumedCredits = 25m,
            EffectiveFrom = timestamp.AddDays(-20)
        };
        var expiredControl = primaryControl with
        {
            Id = "expired-included-control",
            ConsumedCredits = 50m,
            EffectiveFrom = timestamp.AddDays(-20),
            EffectiveTo = timestamp.AddDays(-10)
        };
        var primaryUlb = original.EconomicGuardrails.UserLevelBudgets
            .Single(budget => budget.Kind == UserLevelBudgetKind.Individual) with
        {
            Id = "custom-individual-ulb",
            EffectiveFrom = timestamp.AddDays(-10),
            EffectiveTo = timestamp.AddDays(10)
        };
        var extraUlb = new UserLevelBudget
        {
            Id = "secondary-individual-ulb",
            Kind = UserLevelBudgetKind.Individual,
            TargetId = "user-2",
            LimitCredits = 300m,
            EffectiveFrom = timestamp.AddDays(-20)
        };
        var expiredUlb = primaryUlb with
        {
            Id = "expired-individual-ulb",
            LimitCredits = 50m,
            EffectiveFrom = timestamp.AddDays(-20),
            EffectiveTo = timestamp.AddDays(-10)
        };
        var primaryActionsBudget = original.ActionsGuardrails!.Budgets.Single() with
        {
            Id = "custom-actions-budget"
        };
        var extraActionsBudget = new ActionsSpendingBudget
        {
            Id = "secondary-actions-budget",
            LimitUsd = 25m,
            Enforcement = GuardrailEnforcement.AlertOnly
        };
        var scenario = original with
        {
            Calls = [primaryCall, extraCall],
            Attribution = original.Attribution! with
            {
                LicensingOrganizationIds = ["org-engineering", "org-secondary"],
                CycleSelectedLicensingOrganizationId = "org-engineering",
                DirectAssignments = [historicalAssignment, currentAssignment]
            },
            BillingContext = original.BillingContext with
            {
                SeatAssignments =
                [
                    historicalSeat,
                    currentSeat,
                    .. original.BillingContext.SeatAssignments.Where(seat => seat.UserId != "user-1")
                ]
            },
            EconomicGuardrails = original.EconomicGuardrails with
            {
                SpendingBudgets =
                [
                    expiredBudget,
                    mismatchedBudget,
                    primaryBudget,
                    extraBudget,
                    .. original.EconomicGuardrails.SpendingBudgets
                        .Where(budget => budget.Scope != SpendingBudgetScope.CostCenter)
                ],
                IncludedUsageControls = [expiredControl, primaryControl, extraControl],
                UserLevelBudgets =
                [
                    .. original.EconomicGuardrails.UserLevelBudgets
                        .Where(budget => budget.Kind != UserLevelBudgetKind.Individual),
                    expiredUlb,
                    primaryUlb,
                    extraUlb
                ]
            },
            ActionsGuardrails = original.ActionsGuardrails with
            {
                Budgets = [primaryActionsBudget, extraActionsBudget]
            }
        };
        var cut = Render<Home>();
        var editor = Assert.IsAssignableFrom<IHtmlTextAreaElement>(
            cut.Find("textarea.json-editor:not(.catalog-editor)"));
        editor.Change(serializer.Serialize(scenario));
        FindButton(cut, "Load JSON into guided fields").Click();
        FindInputByLabel(cut, "Limit (USD)", "#cost-center-budget-settings").Input("123");

        FindButton(cut, "Apply without running").Click();

        var updatedEditor = Assert.IsAssignableFrom<IHtmlTextAreaElement>(
            cut.Find("textarea.json-editor:not(.catalog-editor)"));
        var updatedJson = updatedEditor.GetAttribute("value") ?? updatedEditor.TextContent;
        var updated = serializer.Deserialize(updatedJson);
        var updatedBudget = updated.EconomicGuardrails!.SpendingBudgets
            .Single(budget => budget.Id == primaryBudget.Id);
        Assert.Equal(123m, updatedBudget.LimitUsd);
        Assert.True(updatedBudget.ProductIds.SetEquals(primaryBudget.ProductIds));
        Assert.True(updatedBudget.SkuIds.SetEquals(primaryBudget.SkuIds));
        Assert.Equal(primaryBudget.EffectiveFrom, updatedBudget.EffectiveFrom);
        Assert.Equal(primaryBudget.EffectiveTo, updatedBudget.EffectiveTo);
        Assert.Equal(primaryBudget.TrackingStartedAt, updatedBudget.TrackingStartedAt);
        Assert.Contains(updated.EconomicGuardrails.SpendingBudgets, budget => budget.Id == extraBudget.Id);
        Assert.Contains(updated.EconomicGuardrails.SpendingBudgets, budget =>
            budget.Id == expiredBudget.Id && budget.LimitUsd == expiredBudget.LimitUsd);
        Assert.Contains(updated.EconomicGuardrails.SpendingBudgets, budget =>
            budget.Id == mismatchedBudget.Id && budget.LimitUsd == mismatchedBudget.LimitUsd);
        Assert.Contains(updated.EconomicGuardrails.IncludedUsageControls, control =>
            control.Id == primaryControl.Id &&
            control.EffectiveFrom == primaryControl.EffectiveFrom &&
            control.EffectiveTo == primaryControl.EffectiveTo);
        Assert.Contains(updated.EconomicGuardrails.IncludedUsageControls, control =>
            control.Id == extraControl.Id);
        Assert.Contains(updated.EconomicGuardrails.IncludedUsageControls, control =>
            control.Id == expiredControl.Id &&
            control.ConsumedCredits == expiredControl.ConsumedCredits);
        Assert.Contains(updated.EconomicGuardrails.UserLevelBudgets, budget =>
            budget.Id == primaryUlb.Id &&
            budget.EffectiveFrom == primaryUlb.EffectiveFrom &&
            budget.EffectiveTo == primaryUlb.EffectiveTo);
        Assert.Contains(updated.EconomicGuardrails.UserLevelBudgets, budget => budget.Id == extraUlb.Id);
        Assert.Contains(updated.EconomicGuardrails.UserLevelBudgets, budget =>
            budget.Id == expiredUlb.Id && budget.LimitCredits == expiredUlb.LimitCredits);
        Assert.Contains(updated.ActionsGuardrails!.Budgets, budget => budget.Id == primaryActionsBudget.Id);
        Assert.Contains(updated.ActionsGuardrails.Budgets, budget => budget.Id == extraActionsBudget.Id);
        Assert.Equal(2, updated.Calls.Count);
        Assert.Equal(primaryCall.EnabledMultiplierIds, updated.Calls[0].EnabledMultiplierIds);
        Assert.Equal("primary", updated.Calls[0].Metadata["phase"]);
        Assert.Equal(extraCall.ModelId, updated.Calls[1].ModelId);
        Assert.Equal("secondary", updated.Calls[1].Metadata["phase"]);
        Assert.Equal(
            ["org-engineering", "org-secondary"],
            updated.Attribution!.LicensingOrganizationIds);
        Assert.Equal(2, updated.Attribution.DirectAssignments.Count);
        Assert.Equal(historicalAssignment, updated.Attribution.DirectAssignments[0]);
        Assert.Equal(currentAssignment, updated.Attribution.DirectAssignments[1]);
        Assert.Equal(2, updated.BillingContext!.SeatAssignments.Count(seat => seat.UserId == "user-1"));
        Assert.Contains(updated.BillingContext.SeatAssignments, seat => seat == historicalSeat);
        Assert.Contains(updated.BillingContext.SeatAssignments, seat => seat == currentSeat);
    }

    [Fact]
    public void DocumentationLinksUseOfficialGitHubPagesSafely()
    {
        var cut = Render<Home>();
        var links = cut.FindAll("a.docs-link");

        Assert.True(links.Count >= 20);
        Assert.All(links, link =>
        {
            var uri = new Uri(link.GetAttribute("href")!);
            Assert.Equal("https", uri.Scheme);
            Assert.Equal("docs.github.com", uri.Host);
            Assert.Equal("_blank", link.GetAttribute("target"));
            Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
            Assert.StartsWith("Open GitHub documentation for", link.GetAttribute("title"));
        });
    }

    [Fact]
    public void CostRelatedInputsAndGroupsAreMarked()
    {
        var cut = Render<Home>();
        var badges = cut.FindAll(".cost-related-badge");

        Assert.True(badges.Count >= 8);
        Assert.All(badges, badge => Assert.Equal("Cost related", badge.TextContent.Trim()));
        Assert.Contains("Cost related", cut.Find("label[for=operation]").TextContent);
        Assert.Contains("Cost related", cut.Find("label[for=plan]").TextContent);
        Assert.Contains("Cost related", cut.Find("label[for=model]").TextContent);
        Assert.DoesNotContain("Cost related", cut.Find("label[for=task]").TextContent);

        cut.Find(".scope-card input[type=checkbox]").Change(false);

        var runtimeHeading = cut.FindAll("h3")
            .Single(heading => heading.TextContent.Contains("Runtime guardrails", StringComparison.Ordinal));
        Assert.DoesNotContain("Cost related", runtimeHeading.TextContent);
    }

    [Fact]
    public void GuardrailRowsUseSharedCostClassification()
    {
        var cut = Render<Home>();
        var rows = cut.FindAll(".check-list article");

        Assert.Contains(rows, row =>
            row.TextContent.Contains("included-pool", StringComparison.Ordinal) &&
            row.TextContent.Contains("Cost related", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row =>
            row.TextContent.Contains("runtime", StringComparison.Ordinal) &&
            row.TextContent.Contains("Cost related", StringComparison.Ordinal));
    }

    [Fact]
    public void ChatTemplateHidesActionsSpecificInputs()
    {
        var cut = Render<Home>();

        FindButton(cut, "Chat").Click();

        Assert.Equal("chat", cut.Find("#operation").GetAttribute("value"));
        Assert.Contains("Allowed", cut.Find(".decision-banner").TextContent);
        Assert.Empty(cut.FindAll("#visibility"));
        Assert.Empty(cut.FindAll("#actions-minutes-settings"));
        Assert.DoesNotContain("Actions budget", cut.Markup);
        Assert.DoesNotContain("actions-budget", cut.Find(".check-list").TextContent);
    }

    [Fact]
    public void SixCostBlockedScenariosAreVisible()
    {
        var cut = Render<Home>();

        var buttons = cut.FindAll(".cost-blocked-grid .template-cost-blocked");

        Assert.Equal(6, buttons.Count);
        Assert.Contains(buttons, button => button.TextContent.Contains("User-level budget exceeded"));
        Assert.Contains(buttons, button => button.TextContent.Contains("Included-use overflow prohibited"));
        Assert.Contains(buttons, button => button.TextContent.Contains("Paid usage not authorized for GitHub Copilot"));
        Assert.Contains(buttons, button => button.TextContent.Contains("Paid usage disabled"));
        Assert.Contains(buttons, button => button.TextContent.Contains("AI spending budget exceeded"));
        Assert.Contains(buttons, button => button.TextContent.Contains("Actions spending budget exceeded"));
    }

    [Fact]
    public void SelectingStarterShowsItsSourceAndHighlightsItsButton()
    {
        var cut = Render<Home>();

        var chat = FindButton(cut, "Chat");
        chat.Click();

        chat = FindButton(cut, "Chat");
        Assert.Equal("Chat", cut.Find(".scenario-origin strong").TextContent.Trim());
        Assert.Equal("true", chat.GetAttribute("aria-pressed"));
        Assert.Contains("selected-template", chat.ClassList);
        Assert.Contains("Template defaults", cut.Find(".scenario-state").TextContent);
    }

    [Fact]
    public void ChangingScenarioShowsCustomizedAndSelectingStarterResetsIt()
    {
        var cut = Render<Home>();

        FindInputByLabel(cut, "Pool consumed").Input("501");

        Assert.Contains("Customized", cut.Find(".scenario-state").TextContent);
        Assert.Contains("customized", cut.Find(".scenario-state").ClassList);

        FindButton(cut, "Chat").Click();

        Assert.Contains("Template defaults", cut.Find(".scenario-state").TextContent);
        Assert.DoesNotContain("customized", cut.Find(".scenario-state").ClassList);
    }

    [Fact]
    public void PaidUsageApplicabilityCanBeConfiguredInGuidedFields()
    {
        var cut = Render<Home>();
        cut.Find("button[data-cost-scenario='PaidUsageNotApplicable']").Click();

        Assert.Equal("github-actions", cut.Find("#paid-usage-products").GetAttribute("value"));

        cut.Find("#paid-usage-products").Change("github-copilot");
        FindButton(cut, "Apply overrides and simulate").Click();

        Assert.Contains("Allowed", cut.Find(".decision-banner").TextContent);

        cut.Find("#paid-usage-products").Change("github-actions");
        FindButton(cut, "Apply overrides and simulate").Click();

        Assert.Contains("Blocked", cut.Find(".decision-banner").TextContent);
        Assert.Contains("paid-usage.not-applicable", cut.Find(".decision-banner").TextContent);
        Assert.Equal(2, cut.FindAll(".paid-usage-scope .docs-link").Count);
    }

    [Theory]
    [InlineData(ExampleScenarioVariant.UserLevelBudgetExceeded, "ulb-user-1")]
    [InlineData(ExampleScenarioVariant.IncludedUseOverflowProhibited, "included-cc-engineering")]
    [InlineData(ExampleScenarioVariant.PaidUsageNotApplicable, "paid-usage.not-applicable")]
    [InlineData(ExampleScenarioVariant.PaidUsageDisabled, "paid-usage")]
    [InlineData(ExampleScenarioVariant.AiSpendingBudgetExceeded, "budget-cost-center")]
    [InlineData(ExampleScenarioVariant.ActionsSpendingBudgetExceeded, "actions-enterprise")]
    public void CostBlockedScenarioRemainsBlockedAfterApplyingOverrides(
        ExampleScenarioVariant variant,
        string expectedGate)
    {
        var cut = Render<Home>();

        cut.Find($"button[data-cost-scenario='{variant}']").Click();

        var selectedButton = cut.Find($"button[data-cost-scenario='{variant}']");
        Assert.Equal("true", selectedButton.GetAttribute("aria-pressed"));
        Assert.Equal(selectedButton.TextContent.Trim(), cut.Find(".scenario-origin strong").TextContent.Trim());
        Assert.Contains("Blocked", cut.Find(".decision-banner").TextContent);
        Assert.Contains(expectedGate, cut.Find(".decision-banner").TextContent);

        FindButton(cut, "Apply overrides and simulate").Click();

        Assert.Contains("Blocked", cut.Find(".decision-banner").TextContent);
        Assert.Contains(expectedGate, cut.Find(".decision-banner").TextContent);
    }

    [Fact]
    public void ApplyingCatalogUsesCatalogDefinedPreferredTemplate()
    {
        var serializer = new ScenarioJson();
        var original = EngineConfigurationLoader.LoadDefault();
        var custom = original with
        {
            ExampleScenario = original.ExampleScenario with
            {
                PreferredOperationId = "custom-operation"
            },
            Models = original.Models.Select(model => model with
            {
                Availability = model.Availability is null ? null : model.Availability with
                {
                    AutoModelSelectionOperationIds = []
                }
            }).ToArray(),
            Operations =
            [
                new OperationDefinition
                {
                    Id = "custom-operation",
                    IsBilled = false,
                    ExampleLabel = "Custom operation",
                    ExampleTask = "Execute the custom operation."
                }
            ],
            Gates = [],
            Multipliers = []
        };
        var cut = Render<Home>();
        cut.Find("textarea.catalog-editor").Change(serializer.SerializeConfiguration(custom));

        FindButton(cut, "Apply catalog").Click();

        Assert.NotNull(FindButton(cut, "Custom operation"));
        Assert.Empty(cut.FindAll(".cost-blocked-grid .template-cost-blocked"));
        Assert.Equal("custom-operation", cut.Find("#operation").GetAttribute("value"));
        Assert.Contains("Catalog", cut.Find(".message.success").TextContent);
    }

    public static IEnumerable<object[]> OperationCapabilities()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        return configuration.Operations.Select(operation => new object[]
        {
            operation.Id,
            operation.IsBilled,
            operation.ActionsMetering != ActionsMeteringMode.None,
            operation.ActionsMetering == ActionsMeteringMode.PrivateRepositories
        });
    }

    [Theory]
    [MemberData(nameof(OperationCapabilities))]
    public void OperationSelectionShowsOnlyApplicableFields(
        string operationId,
        bool isBilled,
        bool usesActions,
        bool usesRepositoryVisibility)
    {
        var cut = Render<Home>();

        cut.Find("#operation").Change(operationId);

        Assert.Equal(isBilled, cut.FindAll("#model").Count == 1);
        Assert.Equal(isBilled, cut.FindAll("#individual-ulb-settings").Count == 1);
        Assert.Equal(isBilled, cut.Markup.Contains("Cost-center budget", StringComparison.Ordinal));
        Assert.Equal(usesActions, cut.FindAll("#actions-minutes-settings").Count == 1);
        Assert.Equal(usesActions, cut.Markup.Contains("Actions budget", StringComparison.Ordinal));
        Assert.Equal(usesRepositoryVisibility, cut.FindAll("#visibility").Count == 1);

        cut.Find(".scope-card input[type=checkbox]").Change(false);
        Assert.Equal(isBilled, cut.FindAll("#runtime-settings").Count == 1);
    }

    private static IElement FindInputByLabel(
        IRenderedComponent<Home> cut,
        string label,
        string scope = ".configuration-panel")
    {
        var field = cut.FindAll($"{scope} .field")
            .Single(element =>
                string.Equals(
                    element.QuerySelector("label")?.TextContent.Trim(),
                    label,
                    StringComparison.Ordinal));
        return field.QuerySelector("input")
            ?? throw new InvalidOperationException($"No input found for '{label}'.");
    }

    private static IElement FindButton(IRenderedComponent<Home> cut, string text) =>
        cut.FindAll("button")
            .Single(button =>
                string.Equals(button.TextContent.Trim(), text, StringComparison.Ordinal));

    private static string FindMetricValue(IRenderedComponent<Home> cut, string label) =>
        cut.FindAll(".metric")
            .Single(metric =>
                string.Equals(
                    metric.QuerySelector("small")?.TextContent.Trim(),
                    label,
                    StringComparison.Ordinal))
            .QuerySelector("strong")!
            .TextContent
            .Trim();
}
