using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Reference;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class ReferenceConfigurationValidationTests
{
    [Fact]
    public void DefaultCatalogExportsItsMatchingVersionedReferenceAndSources()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        var restored = Load(JsonSerializer.Serialize(configuration, Options()));
        var snapshot = Assert.IsType<ReferenceSnapshotDefinition>(restored.ReferenceSnapshot);

        Assert.Equal(configuration.Version, snapshot.CatalogVersion);
        Assert.Equal(new DateOnly(2026, 9, 11), snapshot.VerifiedOn);
        Assert.Equal(configuration.ReferenceSnapshot!.Id, snapshot.Id);
        Assert.Equal(configuration.ReferenceSnapshot.Sources, snapshot.Sources);
        Assert.Equal(configuration.ReferenceSnapshot.Assumptions, snapshot.Assumptions);
        Assert.All(snapshot.Sources, source => Assert.StartsWith("https://", source.Url));
        Assert.Contains(snapshot.Sources, source => source.Id == "S2" &&
            source.Url.EndsWith("9b446626f08f19ad2aa81538373d0d070b9a4abf", StringComparison.Ordinal));
        Assert.Contains(snapshot.Assumptions, text => text.Contains("midnight UTC", StringComparison.Ordinal));
        Assert.Contains(snapshot.Assumptions, text => text.Contains("qualified", StringComparison.Ordinal));

        var gemini = restored.Models.Single(x => x.Id == "gemini-3.8-flash");
        Assert.DoesNotContain(TokenComponent.CacheWrite, gemini.SupportedTokenComponents!);
        Assert.Equal(ModelEligibilityReasonCodes.TokenComponentUnpriced,
            new ModelEligibilityEvaluator(restored).EvaluatePricing(
                gemini.Id,
                new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero),
                new() { ModelId = gemini.Id, CacheWriteTokens = 1 }).ReasonCode);
    }

    [Fact]
    public void LegacyCatalogWithoutOptionalMetadataRemainsValidAndLoadable()
    {
        const string json = """
            {
              "version": "legacy",
              "plans": [{
                "id": "business", "isPooled": true,
                "allowancePeriods": [{"effectiveFrom": "2026-06-01T00:00:00Z", "includedCreditsPerUser": 1900}]
              }],
              "operations": [{"id": "chat"}],
              "models": [{
                "id": "legacy-model",
                "pricePeriods": [{
                  "effectiveFrom": "2026-06-01T00:00:00Z",
                  "tiers": [{"id": "default", "inputUsdPerMillion": 1, "outputUsdPerMillion": 2}]
                }]
              }]
            }
            """;
        var configuration = Load(json);
        EngineConfigurationValidator.Validate(configuration);

        Assert.Null(configuration.ReferenceSnapshot);
        Assert.Null(configuration.Models[0].Availability);
        Assert.Null(configuration.Models[0].SupportedTokenComponents);
        var result = new ModelEligibilityEvaluator(configuration).Evaluate(
            "legacy-model", "business", "chat", new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(ModelEligibilityStatus.Unsupported, result.Status);
        Assert.Equal(ModelEligibilityReasonCodes.Unverified, result.ReasonCode);
        Assert.Empty(result.EvidenceSources);
    }

    [Theory]
    [InlineData("blank-snapshot-id", "referenceSnapshot.id")]
    [InlineData("mismatched-version", "catalogVersion")]
    [InlineData("missing-verification", "verifiedOn")]
    [InlineData("missing-sources", "sources")]
    [InlineData("null-source", "sources")]
    [InlineData("duplicate-source", "source")]
    [InlineData("empty-assumptions", "assumption")]
    [InlineData("blank-assumption", "assumptions")]
    [InlineData("empty-source-title", "title")]
    [InlineData("source-verified-later", "evidence dates")]
    [InlineData("source-published-later", "evidence dates")]
    [InlineData("missing-source-date", "verifiedOn")]
    [InlineData("unknown-model-source", "source")]
    [InlineData("source-without-snapshot", "source")]
    [InlineData("blank-model-name", "displayName")]
    [InlineData("empty-availability", "empty availability")]
    [InlineData("reversed-availability", "availability effective date range")]
    [InlineData("empty-availability-interval", "availability effective date range")]
    [InlineData("reversed-verification-interval", "availability effective date range")]
    [InlineData("verification-before-release", "availability effective date range")]
    [InlineData("missing-availability-source", "source")]
    [InlineData("empty-eligibility", "eligible plan")]
    [InlineData("unknown-eligible-plan", "eligible plan")]
    [InlineData("duplicate-eligible-plan", "eligible plan")]
    [InlineData("exhaustive-without-plans", "exhaustive")]
    [InlineData("unknown-auto-operation", "Auto operation")]
    [InlineData("undefined-token-component", "supportedTokenComponents")]
    [InlineData("empty-token-components", "token component")]
    [InlineData("unsupported-priced-component", "unsupported token component")]
    [InlineData("unproven-token-components", "source")]
    [InlineData("unknown-period-source", "source")]
    [InlineData("unknown-multiplier-plan", "plan")]
    [InlineData("empty-multiplier-plans", "plan")]
    [InlineData("missing-multiplier-source", "source")]
    public void ValidatorRejectsMalformedOptionalReferenceMetadata(string mutation, string expected)
    {
        var configuration = Mutate(EngineConfigurationLoader.LoadDefault(), mutation);

        var exception = Assert.Throws<ConfigurationException>(() => EngineConfigurationValidator.Validate(configuration));

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://docs.github.com/en/copilot")]
    [InlineData("https://docs.github.com.attacker.example/")]
    [InlineData("https://github.blog.attacker.example/")]
    [InlineData("https://github.com/unverified-owner/pricing")]
    [InlineData("https://user@docs.github.com/en/copilot")]
    [InlineData("https://docs.github.com:444/en/copilot")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/catalog.json")]
    [InlineData("/relative/path")]
    [InlineData("")]
    public void LoaderRejectsBadOrUnofficialEvidenceUrls(string url)
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        configuration = WithSource(configuration, source => source with { Url = url });

        var exception = Assert.Throws<ConfigurationException>(() =>
            Load(JsonSerializer.Serialize(configuration, Options())));

        Assert.Contains("official GitHub HTTPS URL", exception.Message);
    }

    [Theory]
    [InlineData("numeric-component")]
    [InlineData("unknown-component")]
    [InlineData("invalid-reference-date")]
    [InlineData("missing-required-snapshot-id")]
    [InlineData("invalid-availability-date")]
    public void LoaderRejectsMalformedNewJsonFields(string mutation)
    {
        var document = JsonSerializer.SerializeToNode(EngineConfigurationLoader.LoadDefault(), Options())!;
        var snapshot = document["referenceSnapshot"]!;
        var model = document["models"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == "gpt-6-astra")!;
        switch (mutation)
        {
            case "numeric-component":
                model["supportedTokenComponents"] = new JsonArray(JsonValue.Create(42));
                break;
            case "unknown-component":
                model["supportedTokenComponents"] = new JsonArray(JsonValue.Create("unknownComponent"));
                break;
            case "invalid-reference-date":
                snapshot["verifiedOn"] = "not-a-date";
                break;
            case "missing-required-snapshot-id":
                snapshot.AsObject().Remove("id");
                break;
            case "invalid-availability-date":
                model["availability"]!["availableFrom"] = "not-a-date";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.Throws<JsonException>(() => Load(document.ToJsonString()));
    }

    private static EngineConfiguration Mutate(EngineConfiguration configuration, string mutation)
    {
        var snapshot = configuration.ReferenceSnapshot!;
        return mutation switch
        {
            "blank-snapshot-id" => configuration with { ReferenceSnapshot = snapshot with { Id = " " } },
            "mismatched-version" => configuration with { ReferenceSnapshot = snapshot with { CatalogVersion = "different" } },
            "missing-verification" => configuration with { ReferenceSnapshot = snapshot with { VerifiedOn = default } },
            "missing-sources" => configuration with { ReferenceSnapshot = snapshot with { Sources = null! } },
            "null-source" => configuration with { ReferenceSnapshot = snapshot with { Sources = [null!] } },
            "duplicate-source" => configuration with
            {
                ReferenceSnapshot = snapshot with { Sources = [.. snapshot.Sources, snapshot.Sources[0] with { Id = "s1" }] }
            },
            "empty-assumptions" => configuration with { ReferenceSnapshot = snapshot with { Assumptions = [] } },
            "blank-assumption" => configuration with { ReferenceSnapshot = snapshot with { Assumptions = [" "] } },
            "empty-source-title" => WithSource(configuration, source => source with { Title = "" }),
            "source-verified-later" => WithSource(configuration, source => source with { VerifiedOn = snapshot.VerifiedOn.AddDays(1) }),
            "source-published-later" => WithSource(configuration, source => source with { PublishedOn = source.VerifiedOn.AddDays(1) }),
            "missing-source-date" => WithSource(configuration, source => source with { VerifiedOn = default }),
            "unknown-model-source" => WithModel(configuration, model => model with { SourceIds = ["unknown-source"] }),
            "source-without-snapshot" => configuration with { ReferenceSnapshot = null },
            "blank-model-name" => WithModel(configuration, model => model with { DisplayName = " " }),
            "empty-availability" => WithModel(configuration, model => model with
            {
                Availability = new ModelAvailabilityDefinition { SourceIds = ["S16"] }
            }),
            "reversed-availability" => WithAvailability(configuration, value => value with
            {
                RetiredAt = value.AvailableFrom!.Value.AddTicks(-1)
            }),
            "empty-availability-interval" => WithAvailability(configuration, value => value with { RetiredAt = value.AvailableFrom }),
            "reversed-verification-interval" => WithAvailability(configuration, value => value with
            {
                RetiredAt = value.VerifiedFrom!.Value
            }),
            "verification-before-release" => WithAvailability(configuration, value => value with
            {
                VerifiedFrom = value.AvailableFrom!.Value.AddTicks(-1)
            }),
            "missing-availability-source" => WithAvailability(configuration, value => value with { SourceIds = null! }),
            "empty-eligibility" => WithAvailability(configuration, value => value with { EligiblePlanIds = [] }),
            "unknown-eligible-plan" => WithAvailability(configuration, value => value with { EligiblePlanIds = ["unknown-plan"] }),
            "duplicate-eligible-plan" => WithAvailability(configuration, value => value with { EligiblePlanIds = ["business", "BUSINESS"] }),
            "exhaustive-without-plans" => WithAvailability(configuration, value => value with { EligiblePlanIds = null, EligiblePlansAreExhaustive = true }),
            "unknown-auto-operation" => WithAvailability(configuration, value => value with { AutoModelSelectionOperationIds = ["unknown-operation"] }),
            "undefined-token-component" => WithModel(configuration, model => model with { SupportedTokenComponents = new HashSet<TokenComponent> { (TokenComponent)42 } }),
            "empty-token-components" => WithModel(configuration, model => model with { SupportedTokenComponents = new HashSet<TokenComponent>() }),
            "unsupported-priced-component" => WithModel(configuration, model => model with
            {
                SupportedTokenComponents = new HashSet<TokenComponent> { TokenComponent.FreshInput, TokenComponent.Output }
            }),
            "unproven-token-components" => WithModel(configuration, model => model with { SourceIds = null }),
            "unknown-period-source" => WithModel(configuration, model => model with
            {
                PricePeriods = [model.PricePeriods[0] with { SourceIds = ["unknown-source"] }]
            }),
            "unknown-multiplier-plan" => WithMultiplier(configuration, multiplier => multiplier with { ApplicablePlanIds = new HashSet<string> { "unknown-plan" } }),
            "empty-multiplier-plans" => WithMultiplier(configuration, multiplier => multiplier with { ApplicablePlanIds = new HashSet<string>() }),
            "missing-multiplier-source" => WithMultiplier(configuration, multiplier => multiplier with { SourceIds = null }),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
    }

    private static EngineConfiguration WithModel(EngineConfiguration configuration, Func<ModelDefinition, ModelDefinition> update) =>
        configuration with
        {
            Models = configuration.Models.Select(model => model.Id == "gpt-6-astra" ? update(model) : model).ToArray()
        };

    private static EngineConfiguration WithAvailability(
        EngineConfiguration configuration, Func<ModelAvailabilityDefinition, ModelAvailabilityDefinition> update) =>
        WithModel(configuration, model => model with { Availability = update(model.Availability!) });

    private static EngineConfiguration WithSource(
        EngineConfiguration configuration, Func<ReferenceSourceDefinition, ReferenceSourceDefinition> update) =>
        configuration with
        {
            ReferenceSnapshot = configuration.ReferenceSnapshot! with
            {
                Sources = [update(configuration.ReferenceSnapshot.Sources[0]), .. configuration.ReferenceSnapshot.Sources.Skip(1)]
            }
        };

    private static EngineConfiguration WithMultiplier(
        EngineConfiguration configuration, Func<MultiplierDefinition, MultiplierDefinition> update) =>
        configuration with
        {
            Multipliers = [update(configuration.Multipliers[0]), .. configuration.Multipliers.Skip(1)]
        };

    private static EngineConfiguration Load(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return EngineConfigurationLoader.Load(stream);
    }

    private static JsonSerializerOptions Options() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };
}
