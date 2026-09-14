using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Reference;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class ModelEligibilityTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly EngineConfiguration _configuration = EngineConfigurationLoader.LoadDefault();

    [Theory]
    [InlineData("business")]
    [InlineData("enterprise")]
    public void SelectableModelsAreANarrowVerifiedList(string plan)
    {
        var models = Evaluator().GetSelectableModels(plan, "chat", ReferenceTime);

        Assert.Equal(
            ["gpt-5.6-sol", "gpt-6-astra", "gemini-3.8-flash", "mai-code-1.1-flash"],
            models.Select(x => x.Id));
        Assert.All(models, model => Assert.False(string.IsNullOrWhiteSpace(model.DisplayName)));
    }

    [Fact]
    public void OldModelPricingDoesNotAssertVerifiedEligibility()
    {
        var evaluator = Evaluator();

        Assert.Equal(ModelEligibilityStatus.Available,
            evaluator.EvaluatePricing("gpt-5-mini", ReferenceTime).Status);
        var result = evaluator.Evaluate("gpt-5-mini", "business", "chat", ReferenceTime);
        Assert.Equal(ModelEligibilityStatus.Unsupported, result.Status);
        Assert.Equal(ModelEligibilityReasonCodes.Unverified, result.ReasonCode);
    }

    [Fact]
    public void AstraIsNotEligibleForPro()
    {
        var result = Evaluator().Evaluate("gpt-6-astra", "pro", "chat", ReferenceTime);

        Assert.Equal(ModelEligibilityStatus.Unavailable, result.Status);
        Assert.Equal(ModelEligibilityReasonCodes.PlanIneligible, result.ReasonCode);
        Assert.Contains(result.EvidenceSources, source => source.Id == "S16");
    }

    [Theory]
    [InlineData("pro-plus")]
    [InlineData("max")]
    [InlineData("business")]
    [InlineData("enterprise")]
    public void AstraSupportsItsAnnouncedPlans(string plan)
    {
        Assert.Equal(ModelEligibilityStatus.Available,
            Evaluator().Evaluate("gpt-6-astra", plan, "chat", ReferenceTime).Status);
    }

    [Fact]
    public void APartialEligibilityListDoesNotDeclareOtherPlansIneligible()
    {
        var configuration = UpdateModel("gpt-6-astra", model => model with
        {
            Availability = model.Availability! with
            {
                EligiblePlanIds = ["business"],
                EligiblePlansAreExhaustive = false
            }
        });
        var evaluator = new ModelEligibilityEvaluator(configuration);

        Assert.Equal(ModelEligibilityStatus.Available,
            evaluator.Evaluate("gpt-6-astra", "business", "chat", ReferenceTime).Status);
        var unknown = evaluator.Evaluate("gpt-6-astra", "enterprise", "chat", ReferenceTime);
        Assert.Equal(ModelEligibilityStatus.Unsupported, unknown.Status);
        Assert.Equal(ModelEligibilityReasonCodes.Unverified, unknown.ReasonCode);
    }

    [Theory]
    [InlineData("gpt-6-astra", 4)]
    [InlineData("gemini-3.8-flash", 3)]
    public void AnnouncedAvailabilityStartsInclusively(string modelId, int day)
    {
        var boundary = new DateTimeOffset(2026, 9, day, 0, 0, 0, TimeSpan.Zero);
        var evaluator = Evaluator();

        var before = evaluator.EvaluateAvailability(modelId, boundary.AddTicks(-1));
        Assert.Equal(ModelEligibilityStatus.Unavailable, before.Status);
        Assert.Equal(ModelEligibilityReasonCodes.NotYetAvailable, before.ReasonCode);
        Assert.Equal(ModelEligibilityStatus.Available, evaluator.EvaluateAvailability(modelId, boundary).Status);
        Assert.Equal(ModelEligibilityStatus.Available,
            evaluator.Evaluate(modelId, "business", "chat", boundary).Status);
    }

    [Fact]
    public void PositiveEvidenceCoverageIsNotAnInventedLaunchDate()
    {
        var model = _configuration.Models.Single(x => x.Id == "gpt-5.6-sol");
        Assert.Null(model.Availability!.AvailableFrom);

        var historical = Evaluator().EvaluateAvailability(model.Id, ReferenceTime.AddDays(-1));
        Assert.Equal(ModelEligibilityStatus.Unsupported, historical.Status);
        Assert.Equal(ModelEligibilityReasonCodes.Unverified, historical.ReasonCode);
        Assert.Equal(ModelEligibilityStatus.Available, Evaluator().EvaluatePricing(model.Id, ReferenceTime.AddDays(-1)).Status);
    }

    [Fact]
    public void RetiredMaiStillHasHistoricalTariffsButIsNotSelectable()
    {
        var boundary = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var evaluator = Evaluator();
        var result = evaluator.Evaluate("mai-code-1-flash", "business", "chat", boundary);

        Assert.Equal(ModelEligibilityStatus.Unavailable, result.Status);
        Assert.Equal(ModelEligibilityReasonCodes.Retired, result.ReasonCode);
        Assert.Contains(result.EvidenceSources, source => source.Id == "S18");
        Assert.Equal(ModelEligibilityStatus.Available,
            evaluator.EvaluatePricing("mai-code-1-flash", boundary).Status);
        Assert.DoesNotContain(evaluator.GetSelectableModels("business", "chat", ReferenceTime),
            model => model.Id == "mai-code-1-flash");
    }

    [Theory]
    [InlineData("gemini-3.5-flash")]
    [InlineData("gemini-3.6-flash")]
    [InlineData("kimi-k2.7-code")]
    [InlineData("claude-opus-4.7")]
    public void OctoberRetirementsAreNotAppliedEarly(string modelId)
    {
        var boundary = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var evaluator = Evaluator();

        Assert.Equal(ModelEligibilityStatus.Available, evaluator.EvaluateAvailability(modelId, ReferenceTime).Status);
        Assert.Equal(ModelEligibilityStatus.Available,
            evaluator.EvaluateAvailability(modelId, boundary.AddTicks(-1)).Status);
        foreach (var timestamp in new[] { boundary, boundary.AddTicks(1) })
        {
            var result = evaluator.EvaluateAvailability(modelId, timestamp);
            Assert.Equal(ModelEligibilityStatus.Unavailable, result.Status);
            Assert.Equal(ModelEligibilityReasonCodes.Retired, result.ReasonCode);
            Assert.Contains(result.EvidenceSources, source => source.Id == "S22");
            Assert.Equal(ModelEligibilityStatus.Available, evaluator.EvaluatePricing(modelId, timestamp).Status);
        }
    }

    [Fact]
    public void Gemini37IsNotRetiredByInference()
    {
        var model = _configuration.Models.Single(x => x.Id == "gemini-3.7-flash");

        Assert.Null(model.Availability?.RetiredAt);
        Assert.NotEqual(ModelEligibilityReasonCodes.Retired,
            Evaluator().EvaluateAvailability(model.Id, ReferenceTime.AddMonths(1)).ReasonCode);
    }

    [Fact]
    public void GeminiPromotionExpiryIsNotRetirement()
    {
        var boundary = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var evaluator = Evaluator();

        Assert.Equal(ModelEligibilityStatus.Available,
            evaluator.Evaluate("gemini-3.8-flash", "business", "chat", boundary.AddTicks(-1)).Status);
        var expired = evaluator.Evaluate("gemini-3.8-flash", "business", "chat", boundary);
        Assert.Equal(ModelEligibilityStatus.Unsupported, expired.Status);
        Assert.Equal(ModelEligibilityReasonCodes.PricingNotEffective, expired.ReasonCode);
        Assert.Equal(ModelEligibilityStatus.Available,
            evaluator.EvaluateAvailability("gemini-3.8-flash", boundary).Status);
        Assert.Single(_configuration.Models.Single(x => x.Id == "gemini-3.8-flash").PricePeriods);
    }

    [Theory]
    [InlineData("gemini-3.8-flash")]
    [InlineData("mai-code-1.1-flash")]
    public void UnpublishedCacheWriteIsUnsupportedRatherThanFree(string modelId)
    {
        var evaluator = Evaluator();
        var call = new ModelCallInput { ModelId = modelId, CacheWriteTokens = 1 };

        var result = evaluator.Evaluate(modelId, "business", "chat", ReferenceTime, call);
        Assert.Equal(ModelEligibilityStatus.Unsupported, result.Status);
        Assert.Equal(ModelEligibilityReasonCodes.TokenComponentUnpriced, result.ReasonCode);
        Assert.Contains(result.EvidenceSources, source => source.Id == "S1");
        Assert.Null(result.PriceTier);
        Assert.Equal(ModelEligibilityStatus.Available,
            evaluator.Evaluate(modelId, "business", "chat", ReferenceTime, call with { CacheWriteTokens = 0 }).Status);
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("cli")]
    [InlineData("copilot-app")]
    [InlineData("cloud-agent")]
    public void AutoSupportsOnlyDocumentedPaidPlansAndExperiences(string operation)
    {
        var multiplier = _configuration.Multipliers.Single(x => x.Id == "auto-model-selection");
        Assert.Equal(0.9m, multiplier.Factor);
        Assert.Equal(["pro", "pro-plus", "max", "business", "enterprise"], multiplier.ApplicablePlanIds);
        Assert.DoesNotContain("free", multiplier.ApplicablePlanIds!);
        Assert.DoesNotContain("student", multiplier.ApplicablePlanIds!);

        foreach (var plan in multiplier.ApplicablePlanIds!)
        {
            Assert.Equal(ModelEligibilityStatus.Available,
                Evaluator().Evaluate("mai-code-1.1-flash", plan, operation, ReferenceTime,
                    AutoCall("mai-code-1.1-flash")).Status);
        }
    }

    [Theory]
    [InlineData("code-review")]
    [InlineData("ide-agent")]
    [InlineData("spaces")]
    public void AutoDoesNotApplyToUnlistedExperiences(string operation)
    {
        var result = Evaluator().Evaluate("mai-code-1.1-flash", "business", operation, ReferenceTime,
            AutoCall("mai-code-1.1-flash"));

        Assert.Equal(ModelEligibilityStatus.Unsupported, result.Status);
        Assert.Equal(ModelEligibilityReasonCodes.MultiplierNotApplicable, result.ReasonCode);
    }

    [Fact]
    public void AutoDoesNotInventASupportedActionsOperation()
    {
        Assert.DoesNotContain("actions",
            _configuration.Multipliers.Single(x => x.Id == "auto-model-selection").ApplicableOperationIds);
        Assert.Equal(ModelEligibilityStatus.Unsupported,
            Evaluator().Evaluate("mai-code-1.1-flash", "business", "actions", ReferenceTime,
                AutoCall("mai-code-1.1-flash")).Status);
    }

    [Theory]
    [InlineData("gpt-5.6-sol", "cloud-agent", "auto-model-not-supported")]
    [InlineData("gpt-6-astra", "chat", "auto-model-eligibility-unverified")]
    [InlineData("gemini-3.8-flash", "chat", "auto-model-eligibility-unverified")]
    public void AutoDoesNotInferModelRoutes(string modelId, string operation, string reason)
    {
        var result = Evaluator().Evaluate(modelId, "business", operation, ReferenceTime, AutoCall(modelId));

        Assert.Equal(ModelEligibilityStatus.Unsupported, result.Status);
        Assert.Equal(reason, result.ReasonCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComplianceAndStackingAreExplicitlyUnsupported(bool useAuto)
    {
        var call = new ModelCallInput
        {
            ModelId = "gpt-5.6-sol",
            EnabledMultiplierIds = useAuto
                ? ["auto-model-selection", "data-residency-fedramp"]
                : ["data-residency-fedramp"]
        };
        var result = Evaluator().Evaluate(call.ModelId, "enterprise", "chat", ReferenceTime, call);

        Assert.Equal(ModelEligibilityStatus.Unsupported, result.Status);
        Assert.Equal(ModelEligibilityReasonCodes.MultiplierUnsupported, result.ReasonCode);
        Assert.Contains(result.EvidenceSources, source => source.Id == "S8");
        Assert.Contains(result.EvidenceSources, source => source.Id == "S9");
    }

    [Fact]
    public void EvaluationUsesStableCaseInsensitiveIdsAndOrderedEvidence()
    {
        var evaluator = Evaluator();
        var first = evaluator.Evaluate("GPT-6-ASTRA", "BUSINESS", "CHAT", ReferenceTime);
        var second = evaluator.Evaluate("gpt-6-astra", "business", "chat", ReferenceTime);

        Assert.Equal(ModelEligibilityStatus.Available, first.Status);
        Assert.Equal("gpt-6-astra", first.ModelId);
        Assert.Equal(first.ReasonCode, second.ReasonCode);
        Assert.Equal(["S1", "S16", "S24"], first.EvidenceSources.Select(x => x.Id));
        Assert.Equal(first.EvidenceSources, second.EvidenceSources);
        Assert.Equal(first.Assumptions, second.Assumptions);
    }

    [Theory]
    [InlineData("wrong-model")]
    [InlineData("negative-tokens")]
    [InlineData("duplicate-modifiers")]
    [InlineData("null-modifiers")]
    public void DirectEvaluationRejectsMalformedCalls(string mutation)
    {
        var call = AutoCall("gpt-5.6-sol");
        call = mutation switch
        {
            "wrong-model" => call with { ModelId = "gpt-6-astra" },
            "negative-tokens" => call with { CacheWriteTokens = -1 },
            "duplicate-modifiers" => call with { EnabledMultiplierIds = ["auto-model-selection", "AUTO-MODEL-SELECTION"] },
            "null-modifiers" => call with { EnabledMultiplierIds = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        Assert.Throws<ArgumentException>(() =>
            Evaluator().Evaluate("gpt-5.6-sol", "business", "chat", ReferenceTime, call));
    }

    private ModelEligibilityEvaluator Evaluator() => new(_configuration);

    private EngineConfiguration UpdateModel(string id, Func<ModelDefinition, ModelDefinition> update) =>
        _configuration with
        {
            Models = _configuration.Models.Select(model => model.Id == id ? update(model) : model).ToArray()
        };

    private static ModelCallInput AutoCall(string modelId) =>
        new() { ModelId = modelId, FreshInputTokens = 1_000, EnabledMultiplierIds = ["auto-model-selection"] };
}
